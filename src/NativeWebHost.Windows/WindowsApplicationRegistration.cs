using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using NativeWebHost.Windows.Win32;

namespace NativeWebHost.Windows;

/// <summary>提供 Native AOT 兼容的 Windows 提权、自动启动和桌面快捷方式注册能力。</summary>
public static class WindowsApplicationRegistration
{
    private const string CurrentUserRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ProfileListKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private const string UserShellFoldersKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    private const string TaskSchemaNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private const string TaskPrincipalId = "Author";
    private const string UnlimitedExecutionTime = "PT0S";

    /// <summary>检查当前进程令牌是否具有管理员权限。</summary>
    public static bool IsCurrentProcessAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>通过 Windows UAC 的 runas 动词启动提升权限的新进程。</summary>
    public static void RestartAsAdministrator(
        string executablePath,
        IReadOnlyList<string>? arguments = null)
    {
        var fullPath = NormalizeExecutablePath(executablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = fullPath,
            WorkingDirectory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };

        foreach (var argument in NormalizeArguments(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows 未能启动管理员权限进程。");
    }

    /// <summary>在当前用户桌面创建或更新快捷方式，并返回实际快捷方式路径。</summary>
    public static string EnsureDesktopShortcut(WindowsDesktopShortcutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var targetPath = NormalizeExecutablePath(options.TargetPath);
        var shortcutDirectory = ResolveShortcutDirectory(options.ShortcutDirectory);

        Directory.CreateDirectory(shortcutDirectory);
        var shortcutName = NormalizeShortcutName(options.ShortcutName, targetPath);
        var shortcutPath = Path.Combine(shortcutDirectory, $"{shortcutName}.lnk");
        var workingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
            ? Path.GetDirectoryName(targetPath)
            : Path.GetFullPath(options.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException("无法取得快捷方式工作目录。");
        }

        var iconPath = string.IsNullOrWhiteSpace(options.IconPath)
            ? targetPath
            : Path.GetFullPath(options.IconPath);

        ShellLinkShortcut.Create(
            shortcutPath,
            targetPath,
            JoinArguments(options.Arguments),
            workingDirectory,
            iconPath,
            options.IconIndex,
            options.Description);

        if (!File.Exists(shortcutPath))
        {
            throw new InvalidOperationException($"桌面快捷方式创建后未找到：{shortcutPath}");
        }

        // 新入口确认可用后再清理同目标的旧标题，避免创建失败时桌面没有可用入口。
        RemoveDesktopShortcutsCore(targetPath, shortcutDirectory, shortcutPath);
        return shortcutPath;
    }

    /// <summary>幂等删除指定目录中所有指向目标程序的桌面快捷方式。</summary>
    public static void RemoveDesktopShortcuts(
        string targetPath,
        string? shortcutDirectory = null)
    {
        var normalizedTargetPath = NormalizePath(targetPath, nameof(targetPath));
        var normalizedShortcutDirectory = ResolveShortcutDirectory(shortcutDirectory);
        RemoveDesktopShortcutsCore(normalizedTargetPath, normalizedShortcutDirectory, null);
    }

    /// <summary>验证 SID 是否对应仍存在的本机管理员用户；系统查询故障继续抛出。</summary>
    public static WindowsUserSidValidationResult ValidateLocalAdministratorUserSid(string? userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            return new WindowsUserSidValidationResult(
                WindowsUserSidValidationStatus.InvalidSid,
                null);
        }

        string normalizedUserSid;
        try
        {
            normalizedUserSid = NormalizeUserSid(userSid, nameof(userSid));
        }
        catch (ArgumentException)
        {
            return new WindowsUserSidValidationResult(
                WindowsUserSidValidationStatus.InvalidSid,
                null);
        }

        return new WindowsUserSidValidationResult(
            WindowsAccountMembership.ValidateLocalAdministrator(normalizedUserSid),
            normalizedUserSid);
    }

    /// <summary>按目标用户 SID 解析桌面目录；离线用户只能使用注册表中的可用信息。</summary>
    public static string ResolveDesktopDirectory(string userSid)
    {
        var normalizedUserSid = NormalizeUserSid(userSid, nameof(userSid));
        RejectServiceAccount(normalizedUserSid, nameof(userSid));

        using var currentIdentity = WindowsIdentity.GetCurrent();
        if (currentIdentity.User is not null
            && currentIdentity.User.Equals(new SecurityIdentifier(normalizedUserSid)))
        {
            var currentDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(currentDesktop))
            {
                return Path.GetFullPath(currentDesktop);
            }
        }

        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default);
        using var userShellFolders = users.OpenSubKey(
            $@"{normalizedUserSid}\{UserShellFoldersKeyPath}",
            writable: false);
        var configuredDesktop = userShellFolders?.GetValue(
            "Desktop",
            null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (!string.IsNullOrWhiteSpace(configuredDesktop))
        {
            var resolvedDesktop = configuredDesktop.Trim();
            if (resolvedDesktop.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
            {
                resolvedDesktop = resolvedDesktop.Replace(
                    "%USERPROFILE%",
                    ResolveUserProfileDirectory(normalizedUserSid),
                    StringComparison.OrdinalIgnoreCase);
            }

            // 其他变量属于目标用户环境，不能用 SYSTEM 或当前管理员环境代为展开。
            if (!resolvedDesktop.Contains('%') && Path.IsPathFullyQualified(resolvedDesktop))
            {
                return Path.GetFullPath(resolvedDesktop);
            }
        }

        var profileDirectory = ResolveUserProfileDirectory(normalizedUserSid);
        return Path.Combine(profileDirectory, "Desktop");
    }

    /// <summary>注册目标用户登录时以最高权限交互运行的计划任务，并回读任务配置进行校验。</summary>
    public static void EnsureElevatedLogonTask(WindowsElevatedLogonTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalizedOptions = NormalizeLogonTaskOptions(options);
        if (!VerifyElevatedLogonTask(normalizedOptions))
        {
            if (!IsCurrentProcessAdministrator())
            {
                throw new InvalidOperationException("注册最高权限登录任务前必须提升当前进程权限。");
            }

            RegisterElevatedLogonTask(normalizedOptions);

            if (!VerifyElevatedLogonTask(normalizedOptions))
            {
                throw new InvalidOperationException("登录计划任务已创建，但回读配置未通过最高权限交互启动校验。");
            }
        }

        RemoveObsoleteRunEntry(normalizedOptions.ObsoleteRunKeyValueName);
    }

    /// <summary>回读并校验计划任务的目标用户、登录触发器、运行设置和启动命令。</summary>
    public static bool VerifyElevatedLogonTask(WindowsElevatedLogonTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalizedOptions = NormalizeLogonTaskOptions(options);
        return VerifyElevatedLogonTask(normalizedOptions);
    }

    /// <summary>幂等删除指定的最高权限登录计划任务。</summary>
    public static void DeleteElevatedLogonTask(string taskName)
    {
        var normalizedTaskName = NormalizeTaskName(taskName);
        var deleteResult = RunTaskScheduler(
            throwOnFailure: false,
            "/Delete",
            "/TN", normalizedTaskName,
            "/F");
        if (deleteResult.ExitCode == 0)
        {
            return;
        }

        // 删除失败后回查；任务已不存在时视为成功，同时覆盖并发删除场景。
        var queryResult = RunTaskScheduler(
            throwOnFailure: false,
            "/Query",
            "/TN", normalizedTaskName);
        if (queryResult.ExitCode == 0)
        {
            throw CreateTaskSchedulerException(deleteResult);
        }

        if (!IsTaskDefinitionMissing(normalizedTaskName))
        {
            throw CreateTaskSchedulerException(deleteResult);
        }
    }

    /// <summary>查询任务 XML，并按当前用户、触发器、权限、运行设置和启动命令进行严格校验。</summary>
    private static bool VerifyElevatedLogonTask(NormalizedElevatedLogonTaskOptions options)
    {
        var result = RunTaskScheduler(
            throwOnFailure: false,
            "/Query",
            "/TN", options.TaskName,
            "/XML");
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return false;
        }

        try
        {
            return VerifyElevatedLogonTaskXml(result.StandardOutput, options);
        }
        catch (Exception exception) when (
            exception is XmlException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>安全解析任务 XML 并交给结构化校验器，禁止 DTD 和外部实体解析。</summary>
    private static bool VerifyElevatedLogonTaskXml(
        string taskXml,
        NormalizedElevatedLogonTaskOptions options)
    {
        using var textReader = new StringReader(taskXml);
        using var reader = XmlReader.Create(
            textReader,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            });
        return IsExpectedElevatedLogonTask(
            XDocument.Load(reader, LoadOptions.None),
            options);
    }

    /// <summary>复制并规范化任务配置，避免可变集合在注册和回读期间改变。</summary>
    private static NormalizedElevatedLogonTaskOptions NormalizeLogonTaskOptions(
        WindowsElevatedLogonTaskOptions options)
    {
        return new NormalizedElevatedLogonTaskOptions(
            NormalizeTaskName(options.TaskName, nameof(options)),
            NormalizeExecutablePath(options.ExecutablePath),
            ResolveTargetUserSid(options.UserSid),
            NormalizeArguments(options.Arguments),
            options.ObsoleteRunKeyValueName);
    }

    /// <summary>解析显式目标 SID；未指定时回退当前进程用户，并统一为 Windows 规范格式。</summary>
    private static string ResolveTargetUserSid(string? configuredUserSid)
    {
        var userSid = string.IsNullOrWhiteSpace(configuredUserSid)
            ? GetCurrentUserSid()
            : configuredUserSid.Trim();
        var validation = ValidateLocalAdministratorUserSid(userSid);
        if (!validation.IsValid || validation.NormalizedUserSid is null)
        {
            throw new ArgumentException(
                GetUserSidValidationError(validation.Status),
                nameof(configuredUserSid));
        }

        return validation.NormalizedUserSid;
    }

    /// <summary>把 SID 校验状态转换为面向任务注册调用方的明确错误。</summary>
    private static string GetUserSidValidationError(WindowsUserSidValidationStatus status)
        => status switch
        {
            WindowsUserSidValidationStatus.InvalidSid => "登录计划任务的 UserSid 不是有效的 Windows SID。",
            WindowsUserSidValidationStatus.AccountNotFound => "登录计划任务的 UserSid 没有对应的 Windows 账户。",
            WindowsUserSidValidationStatus.NotUserAccount => "登录计划任务的 UserSid 必须对应 Windows 用户账户。",
            WindowsUserSidValidationStatus.ServiceAccount => "登录计划任务不能使用 Windows 内置服务账户。",
            WindowsUserSidValidationStatus.NotLocalAdministrator => "登录计划任务的目标用户必须属于本机 Administrators 组。",
            _ => "登录计划任务的 UserSid 校验失败。"
        };

    /// <summary>把 Windows SID 统一为规范文本，并为公开入口保留明确的参数错误。</summary>
    private static string NormalizeUserSid(string userSid, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("Windows 用户 SID 不能为空。", parameterName);
        }

        try
        {
            return new SecurityIdentifier(userSid.Trim()).Value;
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "Windows 用户 SID 必须是有效的 Windows SID。",
                parameterName,
                exception);
        }
    }

    /// <summary>拒绝没有交互桌面的内置服务账户。</summary>
    private static void RejectServiceAccount(string userSid, string parameterName)
    {
        var securityIdentifier = new SecurityIdentifier(userSid);
        if (securityIdentifier.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || securityIdentifier.IsWellKnown(WellKnownSidType.LocalServiceSid)
            || securityIdentifier.IsWellKnown(WellKnownSidType.NetworkServiceSid))
        {
            throw new ArgumentException("桌面目录不能属于 Windows 内置服务账户。", parameterName);
        }
    }

    /// <summary>从机器级 ProfileList 读取指定 SID 的用户配置文件根目录。</summary>
    private static string ResolveUserProfileDirectory(string userSid)
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var profile = localMachine.OpenSubKey($@"{ProfileListKeyPath}\{userSid}", writable: false);
        var configuredPath = profile?.GetValue(
            "ProfileImagePath",
            null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException($"无法找到 Windows 用户配置文件：{userSid}");
        }

        var expandedPath = ExpandMachineProfilePath(configuredPath.Trim());
        if (!Path.IsPathFullyQualified(expandedPath))
        {
            throw new InvalidOperationException($"Windows 用户配置文件不是绝对路径：{userSid}");
        }

        return Path.GetFullPath(expandedPath);
    }

    /// <summary>只展开 ProfileList 中可靠的机器变量，拒绝借用当前进程的用户环境。</summary>
    private static string ExpandMachineProfilePath(string configuredPath)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDrive = Path.GetPathRoot(windowsDirectory)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(windowsDirectory) || string.IsNullOrWhiteSpace(systemDrive))
        {
            throw new InvalidOperationException("无法取得 Windows 系统目录。");
        }

        var expandedPath = configuredPath
            .Replace("%SystemDrive%", systemDrive, StringComparison.OrdinalIgnoreCase)
            .Replace("%SystemRoot%", windowsDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("%windir%", windowsDirectory, StringComparison.OrdinalIgnoreCase);
        if (expandedPath.Contains('%'))
        {
            throw new InvalidOperationException("Windows 用户配置文件包含无法安全展开的用户环境变量。");
        }

        return expandedPath;
    }

    /// <summary>取得当前 Windows 令牌对应的 SID，确保任务只在同一用户登录时触发。</summary>
    private static string GetCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value
            ?? throw new InvalidOperationException("无法取得当前 Windows 用户 SID。");
    }

    /// <summary>使用显式任务 XML 注册无限时长、允许电池运行的最高权限交互式登录任务。</summary>
    private static void RegisterElevatedLogonTask(NormalizedElevatedLogonTaskOptions options)
    {
        using var taskXml = new MemoryStream();
        WriteElevatedLogonTaskXml(taskXml, options);
        UseVerifiedTemporaryTaskDefinition(
            taskXml.ToArray(),
            temporaryXmlPath =>
            {
                _ = RunTaskScheduler(
                    throwOnFailure: true,
                    "/Create",
                    "/TN", options.TaskName,
                    "/XML", temporaryXmlPath,
                    "/F");
            });
    }

    /// <summary>关闭写句柄后校验并只读锁定临时任务 XML，再交给外部读取方使用。</summary>
    internal static void UseVerifiedTemporaryTaskDefinition(
        ReadOnlyMemory<byte> taskDefinition,
        Action<string> useTaskDefinition)
    {
        ArgumentNullException.ThrowIfNull(useTaskDefinition);
        if (taskDefinition.IsEmpty)
        {
            throw new ArgumentException("计划任务 XML 不能为空。", nameof(taskDefinition));
        }

        var expectedBytes = taskDefinition.ToArray();
        var temporaryXmlPath = Path.Combine(
            Path.GetTempPath(),
            $"NativeWebHost.Task.{Guid.NewGuid():N}.xml");
        var temporaryFileCreated = false;
        try
        {
            // schtasks 不接受仍有写访问的文件；必须完成落盘并关闭写句柄后再打开只读守卫。
            using (var stream = new FileStream(
                temporaryXmlPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                temporaryFileCreated = true;
                stream.Write(expectedBytes);
                stream.Flush(flushToDisk: true);
            }

            using var readGuard = new FileStream(
                temporaryXmlPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            if (readGuard.Length != expectedBytes.Length)
            {
                throw new InvalidOperationException("临时计划任务 XML 在注册前发生变化。");
            }

            var actualBytes = new byte[expectedBytes.Length];
            readGuard.ReadExactly(actualBytes);
            if (!actualBytes.AsSpan().SequenceEqual(expectedBytes))
            {
                throw new InvalidOperationException("临时计划任务 XML 在注册前发生变化。");
            }

            useTaskDefinition(temporaryXmlPath);
        }
        finally
        {
            if (temporaryFileCreated)
            {
                File.Delete(temporaryXmlPath);
            }
        }
    }

    /// <summary>以结构化 XML API 写入任务定义，所有外部文本均由 XML 编码器转义。</summary>
    private static void WriteElevatedLogonTaskXml(
        Stream stream,
        NormalizedElevatedLogonTaskOptions options)
    {
        var document = BuildElevatedLogonTaskDocument(options);
        using var writer = XmlWriter.Create(
            stream,
            new XmlWriterSettings
            {
                Encoding = Encoding.Unicode,
                Indent = true,
                CloseOutput = false,
                OmitXmlDeclaration = false
            });
        document.Save(writer);
    }

    /// <summary>构造 Task Scheduler v2 XML，显式声明常驻客户端需要的触发器和运行设置。</summary>
    private static XDocument BuildElevatedLogonTaskDocument(
        NormalizedElevatedLogonTaskOptions options)
    {
        XNamespace taskNamespace = TaskSchemaNamespace;
        var workingDirectory = Path.GetDirectoryName(options.ExecutablePath)
            ?? throw new InvalidOperationException("无法取得计划任务工作目录。");
        return new XDocument(
            new XDeclaration("1.0", "utf-16", null),
            new XElement(
                taskNamespace + "Task",
                new XAttribute("version", "1.4"),
                new XElement(
                    taskNamespace + "RegistrationInfo",
                    new XElement(
                        taskNamespace + "Description",
                        "由 NativeWebHost 注册的最高权限交互式登录任务。")),
                new XElement(
                    taskNamespace + "Triggers",
                    new XElement(
                        taskNamespace + "LogonTrigger",
                        new XElement(taskNamespace + "Enabled", true),
                        new XElement(taskNamespace + "UserId", options.UserSid))),
                new XElement(
                    taskNamespace + "Principals",
                    new XElement(
                        taskNamespace + "Principal",
                        new XAttribute("id", TaskPrincipalId),
                        new XElement(taskNamespace + "UserId", options.UserSid),
                        new XElement(taskNamespace + "LogonType", "InteractiveToken"),
                        new XElement(taskNamespace + "RunLevel", "HighestAvailable"))),
                new XElement(
                    taskNamespace + "Settings",
                    new XElement(taskNamespace + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(taskNamespace + "DisallowStartIfOnBatteries", false),
                    new XElement(taskNamespace + "StopIfGoingOnBatteries", false),
                    new XElement(taskNamespace + "AllowHardTerminate", true),
                    new XElement(taskNamespace + "StartWhenAvailable", true),
                    new XElement(taskNamespace + "RunOnlyIfNetworkAvailable", false),
                    new XElement(taskNamespace + "AllowStartOnDemand", true),
                    new XElement(taskNamespace + "Enabled", true),
                    new XElement(taskNamespace + "Hidden", false),
                    new XElement(taskNamespace + "RunOnlyIfIdle", false),
                    new XElement(taskNamespace + "WakeToRun", false),
                    new XElement(taskNamespace + "ExecutionTimeLimit", UnlimitedExecutionTime),
                    new XElement(taskNamespace + "Priority", 7)),
                new XElement(
                    taskNamespace + "Actions",
                    new XAttribute("Context", TaskPrincipalId),
                    new XElement(
                        taskNamespace + "Exec",
                        new XElement(taskNamespace + "Command", options.ExecutablePath),
                        new XElement(taskNamespace + "Arguments", JoinArguments(options.Arguments)),
                        new XElement(taskNamespace + "WorkingDirectory", workingDirectory)))));
    }

    /// <summary>严格比对任务 XML 中唯一的登录触发器、Principal、设置和执行动作。</summary>
    private static bool IsExpectedElevatedLogonTask(
        XDocument document,
        NormalizedElevatedLogonTaskOptions options)
    {
        XNamespace taskNamespace = TaskSchemaNamespace;
        var root = document.Root;
        if (root?.Name != taskNamespace + "Task")
        {
            return false;
        }

        var triggers = GetSingleChild(root, taskNamespace + "Triggers");
        var trigger = GetOnlyChild(triggers, taskNamespace + "LogonTrigger");
        var principals = GetSingleChild(root, taskNamespace + "Principals");
        var principal = GetOnlyChild(principals, taskNamespace + "Principal");
        var settings = GetSingleChild(root, taskNamespace + "Settings");
        var actions = GetSingleChild(root, taskNamespace + "Actions");
        var executeAction = GetOnlyChild(actions, taskNamespace + "Exec");
        if (trigger is null
            || principal is null
            || settings is null
            || actions is null
            || executeAction is null)
        {
            return false;
        }

        var principalId = principal.Attribute("id")?.Value.Trim();
        var actionContext = actions.Attribute("Context")?.Value.Trim();
        var expectedWorkingDirectory = Path.GetDirectoryName(options.ExecutablePath);
        if (string.IsNullOrWhiteSpace(principalId)
            || !string.Equals(principalId, actionContext, StringComparison.Ordinal)
            || !HasOnlyExpectedChildren(
                trigger,
                taskNamespace + "Enabled",
                taskNamespace + "UserId")
            || !HasBooleanValue(trigger, taskNamespace + "Enabled", expected: true)
            || !SidsEqual(GetSingleChildValue(trigger, taskNamespace + "UserId"), options.UserSid)
            || !SidsEqual(GetSingleChildValue(principal, taskNamespace + "UserId"), options.UserSid)
            || !string.Equals(
                GetSingleChildValue(principal, taskNamespace + "LogonType"),
                "InteractiveToken",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                GetSingleChildValue(principal, taskNamespace + "RunLevel"),
                "HighestAvailable",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                GetSingleChildValue(settings, taskNamespace + "MultipleInstancesPolicy"),
                "IgnoreNew",
                StringComparison.OrdinalIgnoreCase)
            || !HasBooleanValue(settings, taskNamespace + "Enabled", expected: true)
            || !HasBooleanValue(
                settings,
                taskNamespace + "DisallowStartIfOnBatteries",
                expected: false)
            || !HasBooleanValue(
                settings,
                taskNamespace + "StopIfGoingOnBatteries",
                expected: false)
            || !HasBooleanValue(settings, taskNamespace + "AllowHardTerminate", expected: true)
            || !HasBooleanValue(settings, taskNamespace + "StartWhenAvailable", expected: true)
            || !HasBooleanValue(
                settings,
                taskNamespace + "RunOnlyIfNetworkAvailable",
                expected: false)
            || !HasBooleanValue(settings, taskNamespace + "AllowStartOnDemand", expected: true)
            || !HasBooleanValue(settings, taskNamespace + "Hidden", expected: false)
            || !HasBooleanValue(settings, taskNamespace + "RunOnlyIfIdle", expected: false)
            || !HasBooleanValue(settings, taskNamespace + "WakeToRun", expected: false)
            || !string.Equals(
                GetSingleChildValue(settings, taskNamespace + "ExecutionTimeLimit"),
                UnlimitedExecutionTime,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                GetSingleChildValue(settings, taskNamespace + "Priority"),
                "7",
                StringComparison.Ordinal)
            || !PathsEqual(
                GetSingleChildValue(executeAction, taskNamespace + "Command"),
                options.ExecutablePath)
            || !TryGetOptionalChildValue(
                executeAction,
                taskNamespace + "Arguments",
                out var actualArguments)
            || !string.Equals(
                actualArguments,
                JoinArguments(options.Arguments),
                StringComparison.Ordinal)
            || !PathsEqual(
                GetSingleChildValue(executeAction, taskNamespace + "WorkingDirectory"),
                expectedWorkingDirectory ?? string.Empty))
        {
            return false;
        }

        return true;
    }

    /// <summary>确认元素只包含各一个允许的直接子元素，拒绝登录延迟、边界和重复字段。</summary>
    private static bool HasOnlyExpectedChildren(XElement parent, params XName[] expectedNames)
    {
        if (parent.Elements().Count() != expectedNames.Length)
        {
            return false;
        }

        foreach (var expectedName in expectedNames)
        {
            if (GetSingleChild(parent, expectedName) is null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>取得指定名称的唯一直接子元素；缺失或重复时返回 null。</summary>
    private static XElement? GetSingleChild(XElement parent, XName childName)
    {
        XElement? result = null;
        foreach (var child in parent.Elements(childName))
        {
            if (result is not null)
            {
                return null;
            }

            result = child;
        }

        return result;
    }

    /// <summary>取得容器内唯一且名称匹配的子元素，拒绝额外触发器、Principal 或动作。</summary>
    private static XElement? GetOnlyChild(XElement? parent, XName expectedName)
    {
        if (parent is null)
        {
            return null;
        }

        using var enumerator = parent.Elements().GetEnumerator();
        if (!enumerator.MoveNext() || enumerator.Current.Name != expectedName)
        {
            return null;
        }

        var result = enumerator.Current;
        return enumerator.MoveNext() ? null : result;
    }

    /// <summary>读取唯一直接子元素并去除任务计划程序可能添加的外围空白。</summary>
    private static string? GetSingleChildValue(XElement parent, XName childName)
        => GetSingleChild(parent, childName)?.Value.Trim();

    /// <summary>读取可省略的唯一子元素；缺失等价于空字符串，重复则判定无效。</summary>
    private static bool TryGetOptionalChildValue(
        XElement parent,
        XName childName,
        out string value)
    {
        XElement? result = null;
        foreach (var child in parent.Elements(childName))
        {
            if (result is not null)
            {
                value = string.Empty;
                return false;
            }

            result = child;
        }

        value = result?.Value.Trim() ?? string.Empty;
        return true;
    }

    /// <summary>按 XML 布尔语义校验唯一设置值。</summary>
    private static bool HasBooleanValue(XElement parent, XName childName, bool expected)
    {
        var value = GetSingleChildValue(parent, childName);
        if (value is null)
        {
            return false;
        }

        try
        {
            return XmlConvert.ToBoolean(value) == expected;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>按 Windows SID 结构比较用户标识，拒绝任务中的账户名称或无效 SID。</summary>
    private static bool SidsEqual(string? actualSid, string expectedSid)
    {
        if (string.IsNullOrWhiteSpace(actualSid))
        {
            return false;
        }

        try
        {
            return new SecurityIdentifier(actualSid)
                .Equals(new SecurityIdentifier(expectedSid));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>取得存在的可执行文件绝对路径。</summary>
    private static string NormalizeExecutablePath(string executablePath)
    {
        var fullPath = NormalizePath(executablePath, nameof(executablePath));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到需要注册的应用程序。", fullPath);
        }

        return fullPath;
    }

    /// <summary>规范化绝对路径，但不要求目标当前存在，供卸载清理场景使用。</summary>
    private static string NormalizePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("路径不能为空。", parameterName);
        }

        return Path.GetFullPath(path);
    }

    /// <summary>规范化任务名称并拒绝会破坏命令参数边界的控制字符。</summary>
    private static string NormalizeTaskName(string taskName, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(taskName))
        {
            throw new ArgumentException("计划任务名称不能为空。", parameterName ?? nameof(taskName));
        }

        var normalizedTaskName = taskName.Trim();
        if (normalizedTaskName.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
        {
            throw new ArgumentException(
                "计划任务名称不能包含空字符或换行符。",
                parameterName ?? nameof(taskName));
        }

        return normalizedTaskName;
    }

    /// <summary>取得并规范化快捷方式目录，未指定时使用当前用户桌面。</summary>
    private static string ResolveShortcutDirectory(string? shortcutDirectory)
    {
        var resolvedDirectory = string.IsNullOrWhiteSpace(shortcutDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : Path.GetFullPath(shortcutDirectory);
        if (string.IsNullOrWhiteSpace(resolvedDirectory))
        {
            throw new InvalidOperationException("无法取得当前用户桌面目录。");
        }

        return resolvedDirectory;
    }

    /// <summary>清理目录中指向同一目标程序的快捷方式，损坏或无关链接保持不变。</summary>
    private static void RemoveDesktopShortcutsCore(
        string targetPath,
        string shortcutDirectory,
        string? preservedShortcutPath)
    {
        if (!Directory.Exists(shortcutDirectory))
        {
            return;
        }

        foreach (var shortcutPath in Directory.EnumerateFiles(
                     shortcutDirectory,
                     "*.lnk",
                     new EnumerationOptions
                     {
                         RecurseSubdirectories = false,
                         MatchCasing = MatchCasing.CaseInsensitive,
                         MatchType = MatchType.Simple,
                         AttributesToSkip = 0,
                         IgnoreInaccessible = false,
                         ReturnSpecialDirectories = false
                     }))
        {
            if ((preservedShortcutPath is null
                    || !string.Equals(
                        Path.GetFullPath(shortcutPath),
                        preservedShortcutPath,
                        StringComparison.OrdinalIgnoreCase))
                && ShellLinkShortcut.TryGetTargetPath(shortcutPath, out var shortcutTargetPath)
                && PathsEqual(shortcutTargetPath, targetPath))
            {
                File.Delete(shortcutPath);
            }
        }
    }

    /// <summary>复制并验证参数列表，防止可变集合或空参数在注册期间改变命令。</summary>
    private static string[] NormalizeArguments(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new string[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            result[index] = arguments[index]
                ?? throw new ArgumentException("应用程序参数不能包含 null。", nameof(arguments));
        }

        return result;
    }

    /// <summary>规范化快捷方式文件名并移除 Windows 不允许的字符。</summary>
    private static string NormalizeShortcutName(string shortcutName, string targetPath)
    {
        var candidate = shortcutName?.Trim() ?? string.Empty;
        if (candidate.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[..^4];
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(candidate
            .Where(character => !invalidCharacters.Contains(character))
            .ToArray())
            .Trim()
            .TrimEnd(' ', '.');
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            // Windows 设备名即使带扩展名也不可作为文件名，添加前缀可保留原始标题。
            return IsReservedWindowsFileName(sanitized)
                ? $"_{sanitized}"
                : sanitized;
        }

        return Path.GetFileNameWithoutExtension(targetPath);
    }

    /// <summary>识别 Windows 对所有扩展名保留的传统 DOS 设备文件名。</summary>
    private static bool IsReservedWindowsFileName(string fileName)
    {
        var separatorIndex = fileName.IndexOf('.');
        var stem = separatorIndex >= 0 ? fileName[..separatorIndex] : fileName;
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4
            && stem[3] is >= '1' and <= '9'
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>把独立参数编码为 Windows 命令行参数文本。</summary>
    private static string JoinArguments(IReadOnlyList<string>? arguments)
        => string.Join(' ', NormalizeArguments(arguments)
            .Select(QuoteCommandLineArgument));

    /// <summary>按照 CommandLineToArgvW 兼容规则转义一个 Windows 命令行参数。</summary>
    private static string QuoteCommandLineArgument(string value)
    {
        if (value.Length > 0
            && !value.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        var backslashCount = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', (backslashCount * 2) + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            builder.Append('\\', backslashCount);
            builder.Append(character);
            backslashCount = 0;
        }

        builder.Append('\\', backslashCount * 2);
        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>比较任务计划回读的命令路径与期望路径，兼容外层引号和环境变量。</summary>
    private static bool PathsEqual(string? actualPath, string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(actualPath))
        {
            return false;
        }

        try
        {
            var expandedActualPath = Environment.ExpandEnvironmentVariables(
                actualPath.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(expandedActualPath)
                || !Path.IsPathFullyQualified(expectedPath))
            {
                return false;
            }

            var normalizedActual = Path.GetFullPath(expandedActualPath);
            var normalizedExpected = Path.GetFullPath(expectedPath);
            return string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    /// <summary>计划任务注册成功后移除可选的旧版普通权限 Run 启动项。</summary>
    private static void RemoveObsoleteRunEntry(string? valueName)
    {
        if (string.IsNullOrWhiteSpace(valueName))
        {
            return;
        }

        using var runKey = Registry.CurrentUser.OpenSubKey(CurrentUserRunKeyPath, writable: true);
        runKey?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    /// <summary>以结构化参数执行 schtasks.exe，并在需要时返回包含输出的可诊断异常。</summary>
    private static TaskSchedulerCommandResult RunTaskScheduler(
        bool throwOnFailure,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Windows 任务计划程序命令。");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var result = new TaskSchedulerCommandResult(
            process.ExitCode,
            standardOutputTask.GetAwaiter().GetResult(),
            standardErrorTask.GetAwaiter().GetResult());
        if (throwOnFailure && result.ExitCode != 0)
        {
            throw CreateTaskSchedulerException(result);
        }

        return result;
    }

    /// <summary>把任务计划命令失败结果转换为包含退出码和原始输出的异常。</summary>
    private static InvalidOperationException CreateTaskSchedulerException(
        TaskSchedulerCommandResult result)
    {
        var details = $"{result.StandardError}{result.StandardOutput}".Trim();
        return new InvalidOperationException(
            $"Windows 任务计划命令执行失败（退出码 {result.ExitCode}）：{details}");
    }

    /// <summary>通过任务定义文件确认任务确实不存在，不把查询权限错误误判为成功。</summary>
    private static bool IsTaskDefinitionMissing(string taskName)
    {
        try
        {
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windowsDirectory))
            {
                return false;
            }

            var taskStoreDirectory = Path.GetFullPath(Path.Combine(
                windowsDirectory,
                "System32",
                "Tasks"));
            var relativeTaskName = taskName.TrimStart('\\', '/')
                .Replace('\\', Path.DirectorySeparatorChar);
            var taskDefinitionPath = Path.GetFullPath(Path.Combine(
                taskStoreDirectory,
                relativeTaskName));
            var taskStorePrefix = Path.TrimEndingDirectorySeparator(taskStoreDirectory)
                + Path.DirectorySeparatorChar;
            if (!taskDefinitionPath.StartsWith(
                    taskStorePrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _ = File.GetAttributes(taskDefinitionPath);
            return false;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>保存任务计划命令的退出码及标准输出，供注册和校验流程使用。</summary>
    private sealed record TaskSchedulerCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    /// <summary>保存一次任务注册与校验期间使用的不可变规范化配置。</summary>
    private sealed record NormalizedElevatedLogonTaskOptions(
        string TaskName,
        string ExecutablePath,
        string UserSid,
        string[] Arguments,
        string? ObsoleteRunKeyValueName);
}
