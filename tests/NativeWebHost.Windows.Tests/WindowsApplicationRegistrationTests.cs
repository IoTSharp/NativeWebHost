using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NativeWebHost.Windows.Tests;

[TestClass]
public sealed class WindowsApplicationRegistrationTests
{
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
}
