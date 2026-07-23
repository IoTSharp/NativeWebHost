using System.Runtime.InteropServices;
using DirectN;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NativeWebHost.Windows.Tests;

[TestClass]
public sealed class NativeWebView2JsBridgeTests
{
    /// <summary>验证借用字符串只复制内容，原始内存仍由调用方释放。</summary>
    [TestMethod]
    public void ToBorrowedStringPreservesCallerOwnership()
    {
        var pointer = Marshal.StringToCoTaskMemUni("\"token\"");
        try
        {
            var actual = NativeWebView2JsBridge.ToBorrowedString(new PWSTR(pointer));

            Assert.AreEqual("\"token\"", actual);
            Assert.AreEqual('"', (char)Marshal.ReadInt16(pointer));
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>验证空借用指针按无结果处理。</summary>
    [TestMethod]
    public void ToBorrowedStringReturnsNullForNullPointer()
        => Assert.IsNull(NativeWebView2JsBridge.ToBorrowedString(new PWSTR(0)));
}
