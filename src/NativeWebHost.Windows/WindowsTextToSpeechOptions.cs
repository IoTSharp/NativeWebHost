namespace NativeWebHost.Windows;

/// <summary>配置 Windows SAPI 文本转语音组件的语速、音量和首选语言。</summary>
public sealed class WindowsTextToSpeechOptions
{
    /// <summary>SAPI 语速，允许范围为 -10 到 10。</summary>
    public int Rate { get; set; } = -1;

    /// <summary>SAPI 音量，允许范围为 0 到 100。</summary>
    public int Volume { get; set; } = 100;

    /// <summary>首选语音的 Windows LCID；默认 0x0804 表示简体中文，null 表示使用系统默认语音。</summary>
    public int? PreferredLanguageLcid { get; set; } = 0x0804;

    /// <summary>释放组件时等待专用语音线程退出的最长时间，不能超过线程 API 的上限。</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>提供 Windows TTS 初始化、播报或释放失败的上下文。</summary>
public sealed class WindowsTextToSpeechErrorEventArgs : EventArgs
{
    /// <summary>创建一条包含失败阶段和原始异常的错误通知。</summary>
    public WindowsTextToSpeechErrorEventArgs(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);
        Operation = operation;
        Exception = exception;
    }

    /// <summary>发生错误时正在执行的 TTS 操作。</summary>
    public string Operation { get; }

    /// <summary>底层 SAPI、COM 或线程操作抛出的异常。</summary>
    public Exception Exception { get; }
}
