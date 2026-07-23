namespace NativeWebHost.Windows;

/// <summary>描述 Windows 用户 SID 是否适合作为最高权限交互任务的 owner。</summary>
public enum WindowsUserSidValidationStatus
{
    InvalidSid = 0,
    AccountNotFound,
    NotUserAccount,
    ServiceAccount,
    NotLocalAdministrator,
    Valid
}

/// <summary>保存 SID 校验状态，以及语法有效时的规范化 SID。</summary>
public sealed record WindowsUserSidValidationResult(
    WindowsUserSidValidationStatus Status,
    string? NormalizedUserSid)
{
    /// <summary>指示 SID 是否对应仍属于本机 Administrators 的普通用户。</summary>
    public bool IsValid => Status == WindowsUserSidValidationStatus.Valid;
}
