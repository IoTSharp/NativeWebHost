namespace NativeWebHost.Windows;

/// <summary>描述需要创建或更新的 Windows 桌面快捷方式。</summary>
public sealed class WindowsDesktopShortcutOptions
{
    /// <summary>快捷方式显示名称，可省略 .lnk 扩展名。</summary>
    public string ShortcutName { get; set; } = string.Empty;

    /// <summary>快捷方式指向的应用程序路径。</summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>快捷方式启动应用时传递的参数。</summary>
    public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

    /// <summary>快捷方式工作目录；未指定时使用目标程序所在目录。</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>快捷方式图标文件；未指定时使用目标程序。</summary>
    public string? IconPath { get; set; }

    /// <summary>快捷方式使用的图标资源索引。</summary>
    public int IconIndex { get; set; }

    /// <summary>快捷方式说明。</summary>
    public string? Description { get; set; }

    /// <summary>快捷方式目录；未指定时使用当前用户桌面。</summary>
    public string? ShortcutDirectory { get; set; }
}

/// <summary>描述指定 Windows 用户登录时以最高权限交互运行的计划任务。</summary>
public sealed class WindowsElevatedLogonTaskOptions
{
    /// <summary>计划任务名称。</summary>
    public string TaskName { get; set; } = string.Empty;

    /// <summary>计划任务启动的应用程序路径。</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>任务登录触发器和 Principal 使用的 Windows 用户 SID；未指定时使用当前进程用户。</summary>
    public string? UserSid { get; set; }

    /// <summary>计划任务传递给应用程序的参数。</summary>
    public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

    /// <summary>注册成功后需要删除的旧版当前用户 Run 项名称。</summary>
    public string? ObsoleteRunKeyValueName { get; set; }
}
