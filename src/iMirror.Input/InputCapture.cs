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
    private double _fractionX, _fractionY;
    private string _reason = "controle desativado";
    public bool IsActive => Volatile.Read(ref _active) != 0;
    public bool KeyboardActive => IsActive && _keyboard;
    public event Action<string>? Stopped;
    public Task Completion => _finished;

    public async Task StartAsync(Func<int> ownedPid, Func<(int Width, int Height)> dimensions, bool keyboard, int intensity)
    {
        await StopAsync("nova sessão");
        if (!bluetooth.Status.MouseConnected) { throw new InputBlockedException("Mouse HID sem subscriber ativo no host selecionado."); }
        if (keyboard && !bluetooth.Status.KeyboardConnected) { throw new InputBlockedException("Teclado HID sem subscriber ativo."); }
        _window = VideoWindow.Find(ownedPid()) ?? throw new InputBlockedException("A janela de vídeo do receiver deste iMirror ainda não está disponível.");
        var size = dimensions();
        if (size.Width <= 0 || size.Height <= 0) { throw new InputBlockedException("A resolução do stream ainda não foi recebida."); }
        _dimensions = dimensions; _ownedPid = ownedPid; _keyboard = keyboard; _intensity = Math.Clamp(intensity, 1, 5);
        _keys.Reset(); _wheel.Reset(); _buttons = 0; _previous = null; _fractionX = _fractionY = 0; _buffer = new(); _localKeys.Clear();
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
        _stop = new(); var token = _stop.Token;
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
        log.Write("capture", "Active; own Gst video foreground + viewport only; relative mouse; bounded queue=128; pacing=16ms; ESC/Ctrl+Alt+Q release input; no keystroke logging");
    }

    private async Task FinishAsync(Task exited, Task worker)
    {
        await Task.WhenAll(exited, worker);
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
        if (_threadId != 0) { Native.PostThreadMessage(_threadId, 0x12, 0, 0); }
    }

    private bool Allowed(bool mouse)
    {
        var state = bluetooth.Status;
        return IsActive && _window is { IsForeground: true } && _ownedPid?.Invoke() == _window.ProcessId &&
            state.MouseConnected && (!mouse ? !_keyboard || state.KeyboardConnected : true);
    }

    private void Pump(TaskCompletionSource ready, TaskCompletionSource exited)
    {
        nint mouse = 0, keyboard = 0; nuint timer = 0;
        Native.HookCallback onMouse = MouseHook, onKey = KeyHook;
        var priorDpi = Native.SetThreadDpiAwarenessContext(-4); // physical coordinates across monitors.
        try
        {
            _threadId = Native.GetCurrentThreadId(); Native.PeekMessage(out _, 0, 0, 0, 0);
            if (!IsActive) { ready.TrySetCanceled(); return; }
            mouse = Native.SetWindowsHookEx(14, onMouse, Native.GetModuleHandle(null), 0);
            keyboard = Native.SetWindowsHookEx(13, onKey, Native.GetModuleHandle(null), 0);
            if (mouse == 0 || keyboard == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            timer = Native.SetTimer(0, 0, 50, 0);
            if (timer == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            ready.TrySetResult();
            while (IsActive && Native.GetMessage(out _, 0, 0, 0) > 0)
            { if (!Allowed(false)) { RequestStop("foco, stream ou conexão HID perdido"); } }
        }
        catch (Exception error) { log.Error("hook-error", error); ready.TrySetException(error); RequestStop("erro da captura"); }
        finally
        {
            if (mouse != 0) { Native.UnhookWindowsHookEx(mouse); }
            if (keyboard != 0) { Native.UnhookWindowsHookEx(keyboard); }
            if (timer != 0) { Native.KillTimer(0, timer); }
            Native.SetThreadDpiAwarenessContext(priorDpi);
            GC.KeepAlive(onMouse); GC.KeepAlive(onKey);
            RequestStop("fim da captura"); exited.TrySetResult();
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
            var dims = _dimensions!(); var position = _window!.Read(mouse.Point.X, mouse.Point.Y, dims.Width, dims.Height);
            var geometry = (position.Viewport, dims.Width, dims.Height);
            if (_lastGeometry != geometry) { _previous = null; _fractionX = _fractionY = 0; _lastGeometry = geometry; }
            var stream = position.Viewport.ToStream(position.X, position.Y);
            if (stream is null)
            {
                _previous = null; _fractionX = _fractionY = 0;
                if (_buttons != 0 || _keys.Modifiers != 0 || _keys.Usages.Length != 0)
                { _buttons = 0; _keys.Reset(); Queue(InputReport.Mouse(0)); Queue(InputReport.Key(0, [])); }
                return Next(); // letterbox, non-client and other windows never swallowed.
            }
            int msg = (int)message;
            if (msg == 0x200)
            {
                if (_previous is { } old)
                {
                    _fractionX += stream.Value.X - old.X; _fractionY += stream.Value.Y - old.Y;
                    int dx = (int)_fractionX, dy = (int)_fractionY; _fractionX -= dx; _fractionY -= dy;
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
            if (!position.Viewport.Contains(position.X, position.Y))
            { if (_keys.Modifiers != 0 || _keys.Usages.Length != 0) { _keys.Reset(); Queue(InputReport.Key(0, [])); } return Next(); }
            if (VirtualKeyMap.Usage(vk) == 0 && VirtualKeyMap.Modifier(vk) == 0) { return Next(); }
            if (_keys.Update(vk, down)) { Queue(InputReport.Key(_keys.Modifiers, _keys.Usages)); }
            return IsActive ? 1 : Next();
        }
        catch (Exception error) { log.Error("keyboard-hook", error); RequestStop("erro do teclado"); }
        return Next();
    }

    private void Queue(InputReport report, bool motion = false)
    { if (!_buffer.Enqueue(report, motion)) { RequestStop("fila HID excedida; input liberado"); } }

    private Task SendLoop(CancellationToken token) => InputSender.RunAsync(_buffer, bluetooth, () => Allowed(false), RequestStop, log, token);
}
