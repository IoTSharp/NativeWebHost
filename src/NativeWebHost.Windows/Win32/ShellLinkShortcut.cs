using System.Runtime.InteropServices;

namespace NativeWebHost.Windows.Win32;

/// <summary>通过 Shell Link 原生 COM 接口创建可用于 Native AOT 的 Windows 快捷方式。</summary>
internal static unsafe partial class ShellLinkShortcut
{
    private const uint ClassContextInProcessServer = 0x1;
    private const uint CoInitializeMultiThreaded = 0x0;
    private const uint StorageModeRead = 0x0;
    private const uint GetPathRawPath = 0x4;
    private const int MaximumWindowsPathLength = 32768;
    private const int RpcChangedMode = unchecked((int)0x80010106);

    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterfaceId = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PersistFileInterfaceId = new("0000010B-0000-0000-C000-000000000046");

    /// <summary>创建或覆盖指定快捷方式，并写入目标、参数、图标和说明。</summary>
    internal static void Create(
        string shortcutPath,
        string targetPath,
        string arguments,
        string workingDirectory,
        string iconPath,
        int iconIndex,
        string? description)
    {
        var initializationResult = CoInitializeEx(IntPtr.Zero, CoInitializeMultiThreaded);
        var shouldUninitialize = initializationResult >= 0;
        if (initializationResult < 0 && initializationResult != RpcChangedMode)
        {
            ThrowForHResult(initializationResult, "初始化 Windows COM 环境失败");
        }

        IntPtr shellLink = IntPtr.Zero;
        IntPtr persistFile = IntPtr.Zero;
        try
        {
            ThrowForHResult(
                CoCreateInstance(
                    in ShellLinkClassId,
                    IntPtr.Zero,
                    ClassContextInProcessServer,
                    in ShellLinkInterfaceId,
                    out shellLink),
                "创建 Windows Shell Link 服务失败");

            SetStringProperty(shellLink, 20, targetPath, "设置快捷方式目标失败");
            SetStringProperty(shellLink, 11, arguments, "设置快捷方式参数失败");
            SetStringProperty(shellLink, 9, workingDirectory, "设置快捷方式工作目录失败");
            if (!string.IsNullOrWhiteSpace(description))
            {
                SetStringProperty(shellLink, 7, description, "设置快捷方式说明失败");
            }
            SetIconLocation(shellLink, iconPath, iconIndex);

            persistFile = QueryInterface(shellLink, PersistFileInterfaceId);
            Save(persistFile, shortcutPath);
        }
        finally
        {
            Release(persistFile);
            Release(shellLink);
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
        }
    }

    /// <summary>读取现有快捷方式的目标路径；损坏或非 Shell Link 文件返回 false。</summary>
    internal static bool TryGetTargetPath(string shortcutPath, out string targetPath)
    {
        targetPath = string.Empty;
        var initializationResult = CoInitializeEx(IntPtr.Zero, CoInitializeMultiThreaded);
        var shouldUninitialize = initializationResult >= 0;
        if (initializationResult < 0 && initializationResult != RpcChangedMode)
        {
            ThrowForHResult(initializationResult, "初始化 Windows COM 环境失败");
        }

        IntPtr shellLink = IntPtr.Zero;
        IntPtr persistFile = IntPtr.Zero;
        try
        {
            ThrowForHResult(
                CoCreateInstance(
                    in ShellLinkClassId,
                    IntPtr.Zero,
                    ClassContextInProcessServer,
                    in ShellLinkInterfaceId,
                    out shellLink),
                "创建 Windows Shell Link 服务失败");

            persistFile = QueryInterface(shellLink, PersistFileInterfaceId);
            if (!TryLoad(persistFile, shortcutPath))
            {
                return false;
            }

            return TryReadTargetPath(shellLink, out targetPath);
        }
        finally
        {
            Release(persistFile);
            Release(shellLink);
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
        }
    }

    /// <summary>查询 COM 对象支持的接口，并返回由调用方负责释放的接口指针。</summary>
    private static IntPtr QueryInterface(IntPtr comObject, Guid interfaceId)
    {
        IntPtr interfacePointer = IntPtr.Zero;
        var method = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)GetMethod(comObject, 0);
        ThrowForHResult(method(comObject, &interfaceId, &interfacePointer), "获取快捷方式保存接口失败");
        return interfacePointer;
    }

    /// <summary>调用 Shell Link 的 UTF-16 字符串属性设置方法。</summary>
    private static void SetStringProperty(IntPtr shellLink, int methodIndex, string value, string errorMessage)
    {
        fixed (char* valuePointer = value)
        {
            var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, int>)GetMethod(shellLink, methodIndex);
            ThrowForHResult(method(shellLink, valuePointer), errorMessage);
        }
    }

    /// <summary>设置快捷方式图标文件及资源索引。</summary>
    private static void SetIconLocation(IntPtr shellLink, string iconPath, int iconIndex)
    {
        fixed (char* iconPathPointer = iconPath)
        {
            var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)GetMethod(shellLink, 17);
            ThrowForHResult(method(shellLink, iconPathPointer, iconIndex), "设置快捷方式图标失败");
        }
    }

    /// <summary>通过 IPersistFile 将快捷方式以 UTF-16 路径保存到磁盘。</summary>
    private static void Save(IntPtr persistFile, string shortcutPath)
    {
        fixed (char* shortcutPathPointer = shortcutPath)
        {
            var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)GetMethod(persistFile, 6);
            ThrowForHResult(method(persistFile, shortcutPathPointer, 1), "保存桌面快捷方式失败");
        }
    }

    /// <summary>以只读方式加载快捷方式文件，文件无效时不抛出 COM 异常。</summary>
    private static bool TryLoad(IntPtr persistFile, string shortcutPath)
    {
        fixed (char* shortcutPathPointer = shortcutPath)
        {
            var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, uint, int>)GetMethod(
                persistFile,
                5);
            return method(persistFile, shortcutPathPointer, StorageModeRead) >= 0;
        }
    }

    /// <summary>调用 IShellLinkW.GetPath 读取 UTF-16 目标路径。</summary>
    private static bool TryReadTargetPath(IntPtr shellLink, out string targetPath)
    {
        var pathBuffer = new char[MaximumWindowsPathLength];
        fixed (char* pathPointer = pathBuffer)
        {
            var method = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, IntPtr, uint, int>)GetMethod(
                shellLink,
                3);
            if (method(
                    shellLink,
                    pathPointer,
                    pathBuffer.Length,
                    IntPtr.Zero,
                    GetPathRawPath) < 0)
            {
                targetPath = string.Empty;
                return false;
            }
        }

        targetPath = new string(pathBuffer).TrimEnd('\0');
        return !string.IsNullOrWhiteSpace(targetPath);
    }

    /// <summary>释放 COM 接口引用，空指针无需处理。</summary>
    private static void Release(IntPtr comObject)
    {
        if (comObject == IntPtr.Zero)
        {
            return;
        }

        var method = (delegate* unmanaged[Stdcall]<IntPtr, uint>)GetMethod(comObject, 2);
        _ = method(comObject);
    }

    /// <summary>从 COM 接口虚表读取指定槽位的原生方法地址。</summary>
    private static IntPtr GetMethod(IntPtr comObject, int methodIndex)
    {
        if (comObject == IntPtr.Zero)
        {
            throw new InvalidOperationException("Windows COM 接口指针无效。");
        }

        var virtualTable = *(IntPtr**)comObject;
        return virtualTable[methodIndex];
    }

    /// <summary>把失败的 HRESULT 转换为保留原始错误码的可诊断异常。</summary>
    private static void ThrowForHResult(int result, string message)
    {
        if (result < 0)
        {
            throw new COMException($"{message}（HRESULT: 0x{result:X8}）。", result);
        }
    }

    /// <summary>初始化当前线程的 COM 单元。</summary>
    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr reserved, uint initializationType);

    /// <summary>结束由当前代码成功初始化的 COM 单元。</summary>
    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    /// <summary>创建 Windows Shell Link 的原生 COM 实例。</summary>
    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid classId,
        IntPtr outerObject,
        uint classContext,
        in Guid interfaceId,
        out IntPtr interfacePointer);
}
