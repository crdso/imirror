using System.Windows.Input;
using System.Windows.Threading;

namespace iMirror.App;

public sealed class AsyncRelayCommand(Func<Task> execute, Action<Exception> onError, Dispatcher dispatcher) : ICommand
{
    public bool IsExecuting { get; private set; }
    public Task ExecutionTask { get; private set; } = Task.CompletedTask;
    public bool CanExecute(object? parameter) => !IsExecuting;
    public event EventHandler? CanExecuteChanged;
    public void Execute(object? parameter)
    {
        dispatcher.VerifyAccess();
        if (!CanExecute(parameter)) { return; }
        ExecutionTask = RunAsync();
    }
    private async Task RunAsync()
    {
        IsExecuting = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await dispatcher.InvokeAsync(() => onError(ex)); }
        finally { await dispatcher.InvokeAsync(() => { IsExecuting = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }); }
    }
}
