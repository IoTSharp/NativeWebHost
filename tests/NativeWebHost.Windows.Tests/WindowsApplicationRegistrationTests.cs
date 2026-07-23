using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NativeWebHost.Windows.Tests;

[TestClass]
public sealed class WindowsApplicationRegistrationTests
{
    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly string[] TaskArguments = ["--autostart", "two words"];

    /// <summary>验证外部读取方可在只读守卫期间打开任务文件，同时不能写入或删除。</summary>
    [TestMethod]
    public void TemporaryTaskDefinitionAllowsSharedReadOnlyAccess()
    {
        var expectedBytes = Encoding.Unicode.GetBytes("<Task>test</Task>");
        string? temporaryPath = null;

        WindowsApplicationRegistration.UseVerifiedTemporaryTaskDefinition(
            expectedBytes,
            path =>
            {
                temporaryPath = path;
                using var reader = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                var actualBytes = new byte[expectedBytes.Length];
                reader.ReadExactly(actualBytes);
                CollectionAssert.AreEqual(expectedBytes, actualBytes);

                Assert.ThrowsExactly<IOException>(() =>
                {
                    using var writer = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite);
                });
                Assert.ThrowsExactly<IOException>(() => File.Delete(path));
            });

        Assert.IsNotNull(temporaryPath);
        Assert.IsFalse(File.Exists(temporaryPath));
    }

    /// <summary>验证外部读取方失败时仍会清理临时任务文件。</summary>
    [TestMethod]
    public void TemporaryTaskDefinitionIsDeletedAfterReaderFailure()
    {
        string? temporaryPath = null;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            WindowsApplicationRegistration.UseVerifiedTemporaryTaskDefinition(
                Encoding.Unicode.GetBytes("<Task>test</Task>"),
                path =>
                {
                    temporaryPath = path;
                    throw new InvalidOperationException("模拟任务注册失败");
                }));

        Assert.IsNotNull(temporaryPath);
        Assert.IsFalse(File.Exists(temporaryPath));
    }

    /// <summary>验证 schtasks 回读的账户名和省略默认节点仍按等价任务配置通过。</summary>
    [TestMethod]
    public void SchedulerNormalizedTaskXmlIsAccepted()
    {
        var expectations = GetCurrentTaskExpectations();
        var document = CreateSchedulerNormalizedTaskDocument(expectations);

        var result = VerifyTaskDocument(document, expectations);

        Assert.IsTrue(result.IsValid, result.FailureReason);
    }

    /// <summary>验证登录触发器回读为其他 SID 时不会被账户名兼容逻辑误接受。</summary>
    [TestMethod]
    public void SchedulerNormalizedTaskXmlRejectsDifferentTriggerUser()
    {
        var expectations = GetCurrentTaskExpectations();
        var document = CreateSchedulerNormalizedTaskDocument(expectations);
        XNamespace taskNamespace = TaskNamespace;
        var differentSid = expectations.UserSid == "S-1-5-18" ? "S-1-5-19" : "S-1-5-18";
        var differentAccountName = new SecurityIdentifier(differentSid)
            .Translate(typeof(NTAccount))
            .Value;
        document.Descendants(taskNamespace + "LogonTrigger")
            .Single()
            .Element(taskNamespace + "UserId")!
            .Value = differentAccountName;

        var result = VerifyTaskDocument(document, expectations);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.FailureReason, "登录触发器用户");
    }

    /// <summary>验证额外登录延迟仍被拒绝，兼容默认节点省略不放松触发行为。</summary>
    [TestMethod]
    public void SchedulerNormalizedTaskXmlRejectsDelayedLogon()
    {
        var expectations = GetCurrentTaskExpectations();
        var document = CreateSchedulerNormalizedTaskDocument(expectations);
        XNamespace taskNamespace = TaskNamespace;
        document.Descendants(taskNamespace + "LogonTrigger")
            .Single()
            .AddFirst(new XElement(taskNamespace + "Delay", "PT30S"));

        var result = VerifyTaskDocument(document, expectations);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.FailureReason, "登录触发器包含");
    }

    /// <summary>验证缺失非默认启动设置时仍判定任务配置不完整。</summary>
    [TestMethod]
    public void SchedulerNormalizedTaskXmlRejectsMissingNonDefaultSetting()
    {
        var expectations = GetCurrentTaskExpectations();
        var document = CreateSchedulerNormalizedTaskDocument(expectations);
        XNamespace taskNamespace = TaskNamespace;
        document.Descendants(taskNamespace + "Settings")
            .Single()
            .Element(taskNamespace + "StartWhenAvailable")!
            .Remove();

        var result = VerifyTaskDocument(document, expectations);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.FailureReason, "StartWhenAvailable");
    }

    /// <summary>验证显式覆盖 schema 默认值时按实际值校验，而不是继续套用默认值。</summary>
    [TestMethod]
    public void SchedulerNormalizedTaskXmlRejectsExplicitOppositeDefault()
    {
        var expectations = GetCurrentTaskExpectations();
        var document = CreateSchedulerNormalizedTaskDocument(expectations);
        XNamespace taskNamespace = TaskNamespace;
        document.Descendants(taskNamespace + "Settings")
            .Single()
            .Add(new XElement(taskNamespace + "Hidden", true));

        var result = VerifyTaskDocument(document, expectations);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.FailureReason, "Hidden");
    }

    /// <summary>使用同一组路径、用户和参数期望值校验代表性任务文档。</summary>
    private static (bool IsValid, string FailureReason) VerifyTaskDocument(
        XDocument document,
        TaskExpectations expectations)
        => WindowsApplicationRegistration.VerifyElevatedLogonTaskXml(
            document.ToString(SaveOptions.DisableFormatting),
            expectations.ExecutablePath,
            expectations.UserSid,
            TaskArguments);

    /// <summary>取得当前测试进程可由 Windows 账户解析的程序路径、SID 和账户名。</summary>
    private static TaskExpectations GetCurrentTaskExpectations()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new TaskExpectations(
            Environment.ProcessPath
                ?? throw new InvalidOperationException("无法取得测试进程路径。"),
            identity.User?.Value
                ?? throw new InvalidOperationException("无法取得测试用户 SID。"),
            identity.Name);
    }

    /// <summary>构造经过 schtasks 规范化的代表性回读 XML，省略默认节点并保留关键非默认值。</summary>
    private static XDocument CreateSchedulerNormalizedTaskDocument(TaskExpectations expectations)
    {
        XNamespace taskNamespace = TaskNamespace;
        var workingDirectory = Path.GetDirectoryName(expectations.ExecutablePath)
            ?? throw new InvalidOperationException("无法取得测试程序目录。");
        return new XDocument(
            new XElement(
                taskNamespace + "Task",
                new XAttribute("version", "1.2"),
                new XElement(
                    taskNamespace + "RegistrationInfo",
                    new XElement(taskNamespace + "URI", "\\NativeWebHost Test")),
                new XElement(
                    taskNamespace + "Principals",
                    new XElement(
                        taskNamespace + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(taskNamespace + "UserId", expectations.UserSid),
                        new XElement(taskNamespace + "LogonType", "InteractiveToken"),
                        new XElement(taskNamespace + "RunLevel", "HighestAvailable"))),
                new XElement(
                    taskNamespace + "Settings",
                    new XElement(taskNamespace + "DisallowStartIfOnBatteries", false),
                    new XElement(taskNamespace + "StopIfGoingOnBatteries", false),
                    new XElement(taskNamespace + "StartWhenAvailable", true),
                    new XElement(taskNamespace + "ExecutionTimeLimit", "PT0S"),
                    new XElement(
                        taskNamespace + "IdleSettings",
                        new XElement(taskNamespace + "StopOnIdleEnd", false),
                        new XElement(taskNamespace + "RestartOnIdle", false))),
                new XElement(
                    taskNamespace + "Triggers",
                    new XElement(
                        taskNamespace + "LogonTrigger",
                        new XElement(taskNamespace + "UserId", expectations.AccountName))),
                new XElement(
                    taskNamespace + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(
                        taskNamespace + "Exec",
                        new XElement(taskNamespace + "Command", expectations.ExecutablePath),
                        new XElement(taskNamespace + "Arguments", "--autostart \"two words\""),
                        new XElement(taskNamespace + "WorkingDirectory", workingDirectory)))));
    }

    /// <summary>保存 XML 回读测试所需的当前进程路径和 Windows 用户标识。</summary>
    private sealed record TaskExpectations(
        string ExecutablePath,
        string UserSid,
        string AccountName);
}
