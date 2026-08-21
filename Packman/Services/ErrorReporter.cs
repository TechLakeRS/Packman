namespace Packman.Services;

/// <summary>
/// Failure channel for awaited command bodies and event handlers. Both are async void and
/// would otherwise take the process down silently. App subscribes and shows the error.
/// </summary>
public static class ErrorReporter
{
    public static event Action<Exception>? Reported;

    public static void Report(Exception ex) => Reported?.Invoke(ex);

    /// <summary>Runs awaited work from a void event handler. Cancellation is not a failure.</summary>
    public static async void FireAndForget(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }
}
