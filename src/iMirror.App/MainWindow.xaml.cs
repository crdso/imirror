using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace iMirror.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private WindowState _previousState;
    private WindowStyle _previousStyle;
    private ResizeMode _previousResizeMode;
    private bool _scrollPending;
    private bool _closed;
    private bool _shutdownComplete;
    private bool _closingRequested;
    private int _previousPage;
    private IInputElement? _beforeDiagnosticsFocus;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        // Stable automation names belong to the shell; pages keep their own WPF namescopes.
        RegisterName("VideoPlaceholder", MirrorPage.VideoPlaceholder);
        RegisterName("AirPlayButton", MirrorPage.AirPlayButton);
        RegisterName("FullscreenButton", MirrorPage.FullscreenButton);
        RegisterName("BluetoothButton", ControlPage.BluetoothButton);
        RegisterName("ControlButton", ControlPage.ControlButton);
        RegisterName("CursorSpeedSlider", ControlPage.CursorSpeedSlider);
        RegisterName("KeyboardLayoutSelector", KeyboardPage.KeyboardLayoutSelector);
        RegisterName("LogList", DiagnosticsPanel.LogList);
        viewModel.PropertyChanged += OnViewModelChanged;
        ((INotifyCollectionChanged)viewModel.Logs).CollectionChanged += OnLogsChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            viewModel.PropertyChanged -= OnViewModelChanged;
            ((INotifyCollectionChanged)viewModel.Logs).CollectionChanged -= OnLogsChanged;
        };
    }

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_shutdownComplete) { return; }
        args.Cancel = true;
        if (_closingRequested) { return; }
        _closingRequested = true;
        IsEnabled = false;
        try { await _viewModel.ShutdownAsync(); }
        catch (Exception ex)
        {
            // Keep the window open if cleanup failed; don't abandon an owned receiver.
            IsEnabled = true;
            _closingRequested = false;
            _viewModel.ReportShutdownFailure(ex);
            MessageBox.Show("Não foi possível encerrar o receiver. Tente Parar AirPlay antes de fechar e consulte os logs.", "iMirror", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _shutdownComplete = true;
        // A completed ShutdownAsync may continue inside Closing; defer the second Close.
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.DiagnosticsOpen))
        {
            if (_viewModel.DiagnosticsOpen)
            {
                _beforeDiagnosticsFocus = Keyboard.FocusedElement;
                Dispatcher.BeginInvoke(new Action(() => { if (!_closed && _viewModel.DiagnosticsOpen) { DiagnosticsPanel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); } }));
            }
            else if (_beforeDiagnosticsFocus is FrameworkElement { IsVisible: true } element) { Keyboard.Focus(element); }
            return;
        }
        if (e.PropertyName != nameof(MainViewModel.IsFullscreen)) { return; }
        if (_viewModel.IsFullscreen)
        {
            _previousPage = _viewModel.SelectedPage;
            _viewModel.SelectedPage = 0;
            _viewModel.DiagnosticsOpen = false;
            _previousState = WindowState;
            _previousStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            _viewModel.SelectedPage = _previousPage;
            WindowState = WindowState.Normal;
            WindowStyle = _previousStyle;
            ResizeMode = _previousResizeMode;
            WindowState = _previousState;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.ReleaseControl();
            if (_viewModel.DiagnosticsOpen) { _viewModel.DiagnosticsOpen = false; e.Handled = true; return; }
        }
        if (e.Key == Key.F11 || (e.Key == Key.Escape && _viewModel.IsFullscreen))
        {
            _viewModel.ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Let the ItemsControl receive CollectionChanged before asking it to generate containers.
        if (_scrollPending || _closed) { return; }
        _scrollPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _scrollPending = false;
            if (!_closed && _viewModel.DiagnosticsOpen && DiagnosticsPanel.LogList.Items.Count > 0)
            { DiagnosticsPanel.LogList.ScrollIntoView(DiagnosticsPanel.LogList.Items[^1]); }
        }));
    }
    private void OnNavigate(object sender, RoutedEventArgs args)
    { if (sender is RadioButton { Tag: string page } && DataContext is MainViewModel model) { model.SelectedPage = int.Parse(page, System.Globalization.CultureInfo.InvariantCulture); } }
}
