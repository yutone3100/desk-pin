namespace DeskPin.Services;

public sealed record SettingsApplyResult(bool Succeeded, string Message)
{
    public static SettingsApplyResult Success() => new(true, "设置已保存");
    public static SettingsApplyResult Failure(string message) => new(false, message);
}
