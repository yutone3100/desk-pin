namespace DeskPin.Services;

public interface IStartupManager
{
    bool IsEnabled();
    StartupOperationResult SetEnabled(bool enabled);
}

public sealed record StartupOperationResult(bool Succeeded, string Message)
{
    public static StartupOperationResult Success() => new(true, string.Empty);
    public static StartupOperationResult Failure(string message) => new(false, message);
}
