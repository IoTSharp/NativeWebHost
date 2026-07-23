namespace NativeWebHost.Windows;

/// <summary>配置 Win32 运行时启动主窗口前执行的 Windows 应用集成。</summary>
public sealed class Win32RuntimeOptions
{
    /// <summary>是否要求当前进程具有管理员权限；未提升时将通过 UAC 重新启动。</summary>
    public bool RequireAdministrator { get; set; }

    /// <summary>用于提权重启、自动启动和快捷方式的应用程序路径；默认使用当前进程路径。</summary>
    public string? ApplicationPath { get; set; }

    /// <summary>提权重启时传递的参数；为空时保留当前进程参数。</summary>
    public IReadOnlyList<string>? AdministratorRestartArguments { get; set; }

    /// <summary>是否注册当前用户登录时以最高权限交互运行的计划任务。</summary>
    public bool EnsureElevatedAutoStart { get; set; }

    /// <summary>自动启动计划任务名称；未指定时使用主窗口标题。</summary>
    public string? AutoStartTaskName { get; set; }

    /// <summary>自动启动任务绑定的 Windows 用户 SID；未指定时使用当前进程用户。</summary>
    public string? AutoStartUserSid { get; set; }

    /// <summary>自动启动计划任务传递给应用程序的参数。</summary>
    public IReadOnlyList<string> AutoStartArguments { get; set; } = Array.Empty<string>();

    /// <summary>计划任务确认成功后需要删除的旧版当前用户 Run 项名称。</summary>
    public string? ObsoleteRunKeyValueName { get; set; }

    /// <summary>是否在当前用户桌面创建或更新应用快捷方式。</summary>
    public bool EnsureDesktopShortcut { get; set; }

    /// <summary>桌面快捷方式名称；未指定时使用主窗口标题。</summary>
    public string? DesktopShortcutName { get; set; }

    /// <summary>桌面快捷方式目录；未指定时使用当前进程用户桌面。</summary>
    public string? DesktopShortcutDirectory { get; set; }

    /// <summary>桌面快捷方式启动应用时传递的参数。</summary>
    public IReadOnlyList<string> DesktopShortcutArguments { get; set; } = Array.Empty<string>();

    /// <summary>桌面快捷方式说明；未指定时使用主窗口标题。</summary>
    public string? DesktopShortcutDescription { get; set; }

    /// <summary>桌面快捷方式工作目录；未指定时使用应用程序所在目录。</summary>
    public string? DesktopShortcutWorkingDirectory { get; set; }

    /// <summary>桌面快捷方式图标文件；未指定时依次使用主窗口图标和应用程序。</summary>
    public string? DesktopShortcutIconPath { get; set; }

    /// <summary>桌面快捷方式使用的图标资源索引。</summary>
    public int DesktopShortcutIconIndex { get; set; }
}
