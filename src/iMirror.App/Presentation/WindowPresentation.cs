using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using iMirror.AirPlay;
using Forms = System.Windows.Forms;
namespace iMirror.App.Presentation;

public sealed class WindowPresentation : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainViewModel _model;
    private readonly nint _handle;
    private readonly HwndSource _source;
    private readonly BrandingIcons _branding = new("iMirror");
    private readonly RendererPresentation _renderer;
    private readonly FocusModePolicy _focus = new();
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly DispatcherTimer _timer;
    private readonly bool _hotkey;
    private readonly (nint Small, nint Big) _originalMainIcons;
    private bool _disposed;
    private string? _lastError;
    private const int HotkeyId = 0x494D;
    public bool RecoveryAvailable => _hotkey && _tray.Visible;
    public WindowPresentation(MainWindow window, MainViewModel model, WindowPreferences? initialPreferences = null)
    {
        _window = window; _model = model;
        _handle = new WindowInteropHelper(window).Handle; _source = HwndSource.FromHwnd(_handle);
        var preferences = initialPreferences ?? WindowPreferences.Load(); model.AutoSizeVideo = preferences.AutoSizeVideo; model.FocusMode = preferences.FocusMode;
        var work = WindowNative.WorkForSaved(preferences.NormalBounds?.Rect) ?? WindowNative.WorkAtCursor();
        var dpi = WindowNative.DpiForArea(work);
        window.MinWidth = Math.Min(820, work.Width * .85 * 96 / dpi); window.MinHeight = Math.Min(520, work.Height * .85 * 96 / dpi);
        WindowNative.Place(_handle, WindowGeometry.FitMain(work, dpi, preferences.NormalBounds?.Rect));
        _originalMainIcons = _branding.Apply(_handle);
        _renderer = new RendererPresentation(model.LogPresentation);
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("Mostrar iMirror", null, (_, _) => window.Dispatcher.Invoke(ShowPanel));
        _menu.Items.Add("Parar controle", null, (_, _) => window.Dispatcher.Invoke(model.ReleaseControl));
        _menu.Items.Add("Sair", null, (_, _) => window.Dispatcher.Invoke(() => { ShowPanel(); window.Close(); }));
        _tray = new Forms.NotifyIcon { Icon = _branding.Small, Text = "iMirror — Mostrar painel", ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (_, _) => window.Dispatcher.Invoke(ShowPanel);
        _source.AddHook(OnMessage);
        bool requiresShift = false;
        _hotkey = WindowNative.RegisterHotKey(_handle, HotkeyId, 0x4003, 0x49);
        if (_hotkey) { model.LogPresentation("Focus recovery hotkey: Ctrl+Alt+I; tray available."); }
        else
        {
            _hotkey = WindowNative.RegisterHotKey(_handle, HotkeyId, 0x4007, 0x49);
            requiresShift = true;
            model.LogPresentation(_hotkey ? "Focus recovery hotkey conflict: fallback Ctrl+Alt+Shift+I; tray available." : "Both focus hotkeys unavailable; focus hiding blocked; panel remains visible.");
            model.PresentationNotice(_hotkey ? "Ctrl+Alt+I em uso. Use Ctrl+Alt+Shift+I para mostrar o painel." : "Atalhos de recuperação em uso. Modo foco indisponível nesta sessão.");
        }
        model.SetPanelRecoveryHotkey(_hotkey, requiresShift);
        model.PanelRequested += ShowPanel;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => Tick(), window.Dispatcher);
    }
    private nint OnMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    { if (message == 0x312 && wParam == HotkeyId) { ShowPanel(); handled = true; } return 0; }
    private void Tick()
    {
        if (_disposed) { return; }
        try
        {
            bool streaming = _model.AirPlay.State == AirPlayState.Streaming;
            bool videoVisible = _renderer.Observe(_model.OwnedReceiverPid, _model.AirPlay.Width ?? 0, _model.AirPlay.Height ?? 0, streaming, _model.AutoSizeVideo);
            _focus.Update(_model.FocusMode, streaming, videoVisible, _hotkey && _tray.Visible, _window.Hide, () => ShowPanel(false));
            _lastError = null;
        }
        catch (Exception error)
        {
            ShowPanel();
            if (_lastError != error.Message) { _model.LogPresentation("Window presentation error: " + error.Message); _lastError = error.Message; }
        }
    }
    public void ShowPanel() => ShowPanel(true);
    private void ShowPanel(bool manual)
    {
        if (_disposed) { return; }
        if (manual) { _model.FocusMode = false; }
        _window.Show(); _focus.PanelShown(); if (_window.WindowState == WindowState.Minimized) { _window.WindowState = WindowState.Normal; } _window.Activate();
    }
    public void SavePreferences()
    {
        SavedBounds? bounds = WindowPreferences.Load().NormalBounds;
        if (!_model.IsFullscreen && _window.WindowState == WindowState.Normal && WindowNative.GetWindowRect(_handle, out var rect))
        { bounds = new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top); }
        new WindowPreferences(bounds, _model.AutoSizeVideo, _model.FocusMode).Save();
    }
    public void Dispose()
    {
        if (_disposed) { return; } _disposed = true;
        _timer.Stop(); _source.RemoveHook(OnMessage); if (_hotkey) { WindowNative.UnregisterHotKey(_handle, HotkeyId); }
        _model.PanelRequested -= ShowPanel; _model.SetPanelRecoveryHotkey(false, false);
        _tray.Visible = false; _tray.Dispose(); _menu.Dispose(); _renderer.Dispose();
        WindowNative.GetWindowThreadProcessId(_handle, out var pid); if (pid == Environment.ProcessId) { BrandingIcons.Restore(_handle, _originalMainIcons); }
        _branding.Dispose();
    }
}
