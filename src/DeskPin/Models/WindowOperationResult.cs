namespace DeskPin.Models;

public enum WindowOperationError
{
    None,
    InvalidWindow,
    AccessDenied,
    NativeFailure,
}

public sealed record WindowOperationResult(
    bool Succeeded,
    bool? IsTopmost,
    WindowOperationError Error,
    string Message)
{
    public static WindowOperationResult Success(bool isTopmost) =>
        new(true, isTopmost, WindowOperationError.None, isTopmost ? "窗口已置顶" : "已取消置顶");

    public static WindowOperationResult Failure(WindowOperationError error, string message) =>
        new(false, null, error, message);
}

public sealed record WindowActionResult(
    bool Succeeded,
    WindowOperationError Error,
    string Message)
{
    public static WindowActionResult Success(string message) =>
        new(true, WindowOperationError.None, message);

    public static WindowActionResult Failure(WindowOperationError error, string message) =>
        new(false, error, message);
}
