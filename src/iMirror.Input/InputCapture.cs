using System.ComponentModel;
using System.Runtime.InteropServices;
using iMirror.Bluetooth;

namespace iMirror.Input;

// Own hook thread (MTA + message pump); WinRT sends never execute in a hook callback.
public sealed class InputCapture(IBluetoothController bluetooth, BluetoothControlLog log)
{
    private InputReportBuffer _buffer = new();
    private readonly KeyboardState _keys = new();
    private readonly WheelAccumulator _wheel = new();
    private readonly HashSet<int> _localKeys = [];
    private CancellationTokenSource? _stop;
    private Task _finished = Task.CompletedTask;
    private uint _threadId;
    private int _active;
    private VideoWindow? _window;
    private Func<(int Width, int Height)>? _dimensions;
    private Func<int>? _ownedPid;
    private bool _keyboard;
    private int _intensity;
    private byte _buttons;
    private (double X, double Y)? _previous;
    private (VideoViewport Viewport, int Width, int Height) _lastGeometry;
    private readonly RelativeMotion _motion = new();
    private VideoCursor? _cursor;
    private int _inside;
    private int _sendingComplete;
    private double _speed = 1;
    private KeyboardLayoutMode _layout;
    private readonly HashSet<int> _forwardedKeys = [];
    private string _reason = "controle desativado";
    public bool IsActive => Volatile.Read(ref _active) != 0;
    public bool KeyboardActive => IsActive && _keyboard;
    public event Action<string>? Stopped;
    public Task Completion => _finished;
    public string CursorDiagnostic
    { get { Native.GetCursorPos(out var point); var root=Native.GetAncestor(Native.WindowFromPoint(point),2);
        return $"Active={IsActive}; Inside={Volatile.Read(ref _inside)}; Foreground={_window?.IsForeground}; SurfaceVisible={_cursor?.IsVisible}; PointerOnSurface={_cursor?.Owns(root)}; HookThreadPointerOnSurface={_cursor?.PointerOnSurface}; PointerInside={PointerInside()}"; } }

    public async Task StartAsync(Func<int> ownedPid, Func<(int Width, int Height)> dimensions, bool keyboard, int intensity,
        double speed = 1, KeyboardLayoutMode layout = KeyboardLayoutMode.Auto)
    {
        await StopAsync("nova sessão");
        if (!bluetooth.Status.MouseConnected) { throw new InputBlockedException("Mouse HID sem subscriber ativo no host selecionado."); }
        if (keyboard && !bluetooth.Status.KeyboardConnected) { throw new InputBlockedException("Teclado HID sem subscriber ativo."); }
        _window = VideoWindow.Find(ownedPid()) ?? throw new InputBlockedException("A janela de vídeo do receiver deste iMirror ainda não está disponível.");
        var size = dimensions();
        if (size.Width <= 0 || size.Height <= 0) { throw new InputBlockedException("A resolução do stream ainda não foi recebida."); }
        _dimensions = dimensions; _ownedPid = ownedPid; _keyboard = keyboard; _intensity = Math.Clamp(intensity, 1, 5);
        _speed = Math.Clamp(speed,0.25,3); _layout = layout;
        _keys.Reset(); _wheel.Reset(); _buttons = 0; _previous = null; _motion.Reset(); _buffer = new(); _localKeys.Clear(); _forwardedKeys.Clear(); _inside = 0;
        // Keys already held before activation stay local, including their matching key-up.
        for (int key = 1; key <= 255; key++) { if (Native.GetAsyncKeyState(key) < 0) { _localKeys.Add(key); } }
        await bluetooth.ReleaseAsync();
        bool activation = Native.SetForegroundWindow(_window.Handle);
        // Activation crosses into the receiver's GUI thread and may finish asynchronously.
        for (int attempt = 0; attempt < 25 && !_window.IsForeground; attempt++) { await Task.Delay(20); }
        if (!_window.IsForeground)
        {
            log.Write("capture", $"Activation requested={activation}; owned video foreground=False; no hooks installed");
            throw new InputBlockedException("Selecione a janela de vídeo e tente ativar o controle novamente.");
        }
        _stop = new(); _sendingComplete = 0; var token = _stop.Token;
        Volatile.Write(ref _active, 1);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Pump(ready, exited)) { IsBackground = true, Name = "iMirror video input" };
        thread.SetApartmentState(ApartmentState.MTA);
        var worker = Task.Run(() => SendLoop(token));
        _finished = FinishAsync(exited.Task, worker);
        thread.Start();
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { await StopAsync("falha ao instalar captura"); throw; }
        log.Write("capture", $"Active; own Gst video foreground + viewport only; relative mouse speed={_speed:F2}x; keyboard layout={KeyboardLayout.ForWindow(_layout,_window)}; bounded queue=128; pacing=16ms; ESC/Ctrl+Alt+Q release input; no keystroke logging");
    }

    private async Task FinishAsync(Task exited, Task worker)
    {
        await Task.WhenAll(exited, worker);
        _keys.Reset(); _wheel.Reset(); _motion.Reset(); _buttons = 0; _previous = null; _inside = 0; _forwardedKeys.Clear();
        _stop?.Dispose(); _stop = null; _threadId = 0;
        log.Write("capture", $"Stopped; reason={_reason}; hooks removed; neutral release attempted");
        Stopped?.Invoke(_reason);
    }

    public async Task StopAsync(string reason = "controle desativado")
    { RequestStop(reason); await _finished; }

    public void RequestStop(string reason)
    {
        if (Interlocked.Exchange(ref _active, 0) == 0) { return; }
        _reason = reason; _buffer.Clear(); _stop?.Cancel();
        try { _cursor?.SetHidden(false); } catch (Exception error) { log.Error("cursor-restore-error",error); }
        // Hooks immediately fail open. Keep their pump alive while the sender
        // awaits keyboard then mouse neutral; it posts WM_QUIT after completion.
    }

    private bool Allowed(bool mouse)
    {
        var state = bluetooth.Status;
        return IsActive && _window is { IsForeground: true } && _ownedPid?.Invoke() == _window.ProcessId &&
            state.MouseConnected && (!mouse ? !_keyboard || state.KeyboardConnected : true);
    }

    private void Pump(TaskCompletionSource ready, TaskCompletionSource exited)
    {
        nint mouse = 0, keyboard = 0, foreground = 0; nuint timer = 0;
        Native.HookCallback onMouse = MouseHook, onKey = KeyHook;
        Native.EventCallback onForeground = (_,_,_,_,_,_,_) =>
        { try { if (IsActive && !Allowed(false)) { RequestStop("foreground perdido"); } }
          catch (Exception error) { log.Error("foreground-error",error); RequestStop("erro de foreground"); } };
        var priorDpi = Native.SetThreadDpiAwarenessContext(-4); // physical coordinates across monitors.
        try
        {
            _threadId = Native.GetCurrentThreadId(); Native.PeekMessage(out _, 0, 0, 0, 0);
            if (!IsActive) { ready.TrySetCanceled(); return; }
            _cursor = new VideoCursor(_window!,_dimensions!,error => { log.Error("cursor-error",error); RequestStop("erro do cursor"); });
            mouse = Native.SetWindowsHookEx(14, onMouse, Native.GetModuleHandle(null), 0);
            keyboard = Native.SetWindowsHookEx(13, onKey, Native.GetModuleHandle(null), 0);
            if (mouse == 0 || keyboard == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            foreground = Native.SetWinEventHook(3,3,0,onForeground,0,0,0);
            if (foreground==0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            timer = Native.SetTimer(0, 0, 50, 0);
            if (timer == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            ready.TrySetResult();
            while (Native.GetMessage(out var message, 0, 0, 0) > 0)
            {
                Native.TranslateMessage(ref message); Native.DispatchMessage(ref message);
                if (!IsActive) { if (Volatile.Read(ref _sendingComplete) != 0) { break; } continue; }
                if (!Allowed(false)) { RequestStop("foco, stream ou conexão HID perdido"); }
                else if (!PointerInside()) { LeaveViewport(); }
                else { Volatile.Write(ref _inside,1); _cursor.SetHidden(true); }
            }
        }
        catch (Exception error) { log.Error("hook-error", error); ready.TrySetException(error); RequestStop("erro da captura"); }
        finally
        {
            RequestStop("fim da captura");
            try { _cursor?.SetHidden(false); } catch (Exception error) { log.Error("cursor-restore-error",error); }
            if (mouse != 0) { Native.UnhookWindowsHookEx(mouse); }
            if (keyboard != 0) { Native.UnhookWindowsHookEx(keyboard); }
            if (foreground != 0) { Native.UnhookWinEvent(foreground); }
            if (timer != 0) { Native.KillTimer(0, timer); }
            _cursor?.Dispose(); _cursor=null;
            Native.SetThreadDpiAwarenessContext(priorDpi);
            GC.KeepAlive(onMouse); GC.KeepAlive(onKey); GC.KeepAlive(onForeground);
            exited.TrySetResult();
        }
    }

    private nint MouseHook(int code, nint message, nint data)
    {
        nint Next() => Native.CallNextHookEx(0, code, message, data);
        if (code < 0 || !IsActive) { return Next(); }
        try
        {
            var mouse = Marshal.PtrToStructure<Native.Mouse>(data);
            if ((mouse.Flags & 1) != 0) { return Next(); }
            if (!Allowed(true)) { RequestStop("janela ou mouse HID indisponível"); return Next(); }
            if (!PointOwned(mouse.Point)) { LeaveViewport(); return Next(); }
            var dims = _dimensions!(); var position = _window!.Read(mouse.Point.X, mouse.Point.Y, dims.Width, dims.Height);
            var geometry = (position.Viewport, dims.Width, dims.Height);
            if (_lastGeometry != geometry) { _previous = null; _motion.Reset(); _lastGeometry = geometry; }
            var stream = position.Viewport.ToStream(position.X, position.Y);
            if (stream is null)
            {
                LeaveViewport();
                return Next(); // letterbox, non-client and other windows never swallowed.
            }
            Volatile.Write(ref _inside,1); _cursor?.SetHidden(true);
            int msg = (int)message;
            if (msg == 0x200)
            {
                if (_previous is { } old)
                {
                    var (dx,dy) = _motion.Add(stream.Value.X - old.X,stream.Value.Y - old.Y,_speed);
                    if (dx != 0 || dy != 0) { Queue(InputReport.Mouse(_buttons, dx, dy), true); }
                }
                _previous = stream; return Next();
            }
            byte button = msg is 0x201 or 0x202 ? (byte)1 : msg is 0x204 or 0x205 ? (byte)2 : (byte)0;
            if (button != 0)
            {
                _buttons = msg is 0x201 or 0x204 ? (byte)(_buttons | button) : (byte)(_buttons & ~button);
                Queue(InputReport.Mouse(_buttons)); return IsActive ? 1 : Next();
            }
            if (msg == 0x20A)
            {
                if (bluetooth.Status.ProtocolMode == 0) { return Next(); } // boot mouse has no wheel.
                int wheel = _wheel.Add(unchecked((short)(mouse.Data >> 16)), _intensity);
                if (wheel != 0) { Queue(InputReport.Mouse(_buttons, wheel: wheel)); }
                return IsActive ? 1 : Next();
            }
        }
        catch (Exception error) { log.Error("mouse-hook", error); RequestStop("erro do mouse"); }
        return Next();
    }

    private nint KeyHook(int code, nint message, nint data)
    {
        nint Next() => Native.CallNextHookEx(0, code, message, data);
        if (code < 0 || !IsActive) { return Next(); }
        try
        {
            var key = Marshal.PtrToStructure<Native.Key>(data);
            if ((key.Flags & 0x10) != 0) { return Next(); }
            int vk = (int)key.Code;
            bool down = (int)message is 0x100 or 0x104;
            // Captured modifier events may be swallowed before Windows updates its async state.
            bool ctrl = (_keys.Modifiers & 0x11) != 0 || Native.GetAsyncKeyState(0x11) < 0;
            bool alt = (_keys.Modifiers & 0x44) != 0 || Native.GetAsyncKeyState(0x12) < 0;
            if (down && CapturePolicy.Emergency(vk, ctrl, alt))
            { RequestStop("atalho de emergência"); return 1; }
            if (!Allowed(false)) { RequestStop("foco ou teclado HID perdido"); return Next(); }
            if (!_keyboard) { return Next(); }
            if (vk == 0x10) { vk = key.Scan == 0x36 ? 0xA1 : 0xA0; }
            if (vk == 0x11) { vk = (key.Flags & 1) != 0 ? 0xA3 : 0xA2; }
            if (vk == 0x12) { vk = (key.Flags & 1) != 0 ? 0xA5 : 0xA4; }
            if (_localKeys.Contains(vk)) { if (!down) { _localKeys.Remove(vk); } return Next(); }
            // Require pointer in the actual video viewport for keyboard redirection too.
            Native.GetCursorPos(out var cursor); var dims = _dimensions!();
            var position = _window!.Read(cursor.X, cursor.Y, dims.Width, dims.Height);
            if (!position.Viewport.Contains(position.X, position.Y) || !PointOwned(cursor)) { LeaveViewport(); return Next(); }
            Volatile.Write(ref _inside,1); _cursor?.SetHidden(true);
            var layout = KeyboardLayout.ForWindow(_layout,_window);
            byte usage = KeyboardLayout.Usage(vk,key.Scan,(key.Flags & 1) != 0,layout);
            if (usage == 0 && VirtualKeyMap.Modifier(vk) == 0 && !_forwardedKeys.Contains(vk)) { return Next(); }
            if (down) { _forwardedKeys.Add(vk); } else { _forwardedKeys.Remove(vk); }
            if (_keys.Update(vk, down,usage)) { Queue(InputReport.Key(_keys.Modifiers, _keys.Usages)); }
            return IsActive ? 1 : Next();
        }
        catch (Exception error) { log.Error("keyboard-hook", error); RequestStop("erro do teclado"); }
        return Next();
    }

    private void Queue(InputReport report, bool motion = false)
    { if (!_buffer.Enqueue(report, motion)) { RequestStop("fila HID excedida; input liberado"); } }

    private bool PointerInside()
    {
        if (!Allowed(false) || !Native.GetCursorPos(out var pointer)) { return false; }
        var dims = _dimensions!(); var position = _window!.Read(pointer.X,pointer.Y,dims.Width,dims.Height);
        return position.Viewport.Contains(position.X,position.Y) && PointOwned(pointer);
    }
    private bool PointOwned(Native.Point point)
    {
        nint root=Native.GetAncestor(Native.WindowFromPoint(point),2);
        return root==_window?.Handle || _cursor?.Owns(root)==true;
    }
    private void LeaveViewport()
    {
        _cursor?.SetHidden(false);
        if (Interlocked.Exchange(ref _inside,0) == 0) { return; }
        _buffer.ClearAndRelease(); _previous = null; _motion.Reset(); _wheel.Reset(); _buttons = 0;
        // A held key remains local until its matching key-up, even after reentry.
        foreach (int key in _forwardedKeys) { _localKeys.Add(key); }
        _forwardedKeys.Clear(); _keys.Reset();
    }
    private async Task SendLoop(CancellationToken token)
    {
        try { await InputSender.RunAsync(_buffer, bluetooth, () => Allowed(false), RequestStop, log, token,
            () => Volatile.Read(ref _inside) != 0 && PointerInside()); }
        finally { Volatile.Write(ref _sendingComplete,1); if (_threadId != 0) { Native.PostThreadMessage(_threadId,0x12,0,0); } }
    }
}
