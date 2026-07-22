using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using DirectN;
using DirectN.Extensions.Com;

namespace NativeWebHost.Windows;

/// <summary>提供 Native AOT 兼容、按提交顺序异步播放的 Windows SAPI 文本转语音能力。</summary>
public sealed partial class WindowsTextToSpeech : IDisposable
{
    private const uint CoinitMultithreaded = 0;
    private const double MaximumThreadJoinTimeoutMilliseconds = int.MaxValue;
    private const string DesktopVoiceCategoryId = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices";
    private const string OneCoreVoiceCategoryId = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
    private static readonly Guid VoiceTokenCategoryClassId =
        new("A910187F-0C7A-45AC-92CC-59EDAFB77B53");
    private static readonly string[] VoiceCategoryIds =
    [
        DesktopVoiceCategoryId,
        OneCoreVoiceCategoryId
    ];

    private readonly object _syncRoot = new();
    private readonly BlockingCollection<string> _messages =
        new(new ConcurrentQueue<string>());
    private readonly Thread _speechThread;
    private readonly Action<WindowsTextToSpeechErrorEventArgs>? _errorCallback;
    private readonly int _rate;
    private readonly ushort _volume;
    private readonly int? _preferredLanguageLcid;
    private readonly TimeSpan _shutdownTimeout;
    private bool _disposed;

    /// <summary>创建并启动专用 SAPI COM 线程；等待线程退出超时时，错误回调由释放线程执行。</summary>
    public WindowsTextToSpeech(
        WindowsTextToSpeechOptions? options = null,
        Action<WindowsTextToSpeechErrorEventArgs>? errorCallback = null)
    {
        options ??= new WindowsTextToSpeechOptions();
        ValidateOptions(options);

        _rate = options.Rate;
        _volume = checked((ushort)options.Volume);
        _preferredLanguageLcid = options.PreferredLanguageLcid;
        _shutdownTimeout = options.ShutdownTimeout;
        _errorCallback = errorCallback;
        _speechThread = new Thread(RunSpeechLoop)
        {
            IsBackground = true,
            Name = "NativeWebHost Windows TTS"
        };
        _speechThread.Start();
    }

    /// <summary>报告 TTS 失败；通常由语音线程触发，等待线程退出超时时由释放线程触发。</summary>
    public event EventHandler<WindowsTextToSpeechErrorEventArgs>? ErrorOccurred;

    /// <summary>把非空文本加入异步播报队列；组件已释放时返回 false。</summary>
    public bool TrySpeak(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        lock (_syncRoot)
        {
            if (_disposed || _messages.IsAddingCompleted)
            {
                return false;
            }

            _messages.Add(message);
            return true;
        }
    }

    /// <summary>在同一专用线程中初始化 COM 和 SAPI，并依次提交所有待播报文本。</summary>
    private void RunSpeechLoop()
    {
        IComObject<ISpVoice>? voice = null;
        var comInitialized = false;
        try
        {
            var initializeResult = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
            if (initializeResult < 0)
            {
                throw new COMException("Windows COM 初始化失败。", initializeResult);
            }

            comInitialized = true;
            voice = ComObject.CoCreate<ISpVoice>("SAPI.SpVoice")
                ?? throw new InvalidOperationException("Windows SAPI 语音服务不可用。");
            voice.Object.SetRate(_rate).ThrowOnError();
            voice.Object.SetVolume(_volume).ThrowOnError();
            SelectPreferredVoice(voice.Object);

            foreach (var message in _messages.GetConsumingEnumerable())
            {
                try
                {
                    SpeakMessage(voice.Object, message);
                }
                catch (Exception exception)
                {
                    // 单条文本失败不终止队列，后续通知仍有机会正常播报。
                    ReportError("提交文本播报", exception);
                }
            }
        }
        catch (Exception exception)
        {
            ReportError("初始化 Windows TTS", exception);
            CompleteAdding();
        }
        finally
        {
            StopAndReleaseVoice(voice);
            if (comInitialized)
            {
                CoUninitialize();
            }
        }
    }

    /// <summary>优先选择配置 LCID 对应的已安装语音，未找到时继续使用系统默认语音。</summary>
    private void SelectPreferredVoice(ISpVoice voice)
    {
        if (!_preferredLanguageLcid.HasValue)
        {
            return;
        }

        var requiredAttribute = $"Language={_preferredLanguageLcid.Value:X}";
        Exception? lastException = null;
        foreach (var categoryId in VoiceCategoryIds)
        {
            try
            {
                if (TrySelectVoiceFromCategory(voice, categoryId, requiredAttribute))
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                lastException = exception;
            }
        }

        if (lastException is not null)
        {
            ReportError("选择首选 TTS 语音，已回退到系统默认语音", lastException);
        }
    }

    /// <summary>从一个 SAPI token 分类中选择首个匹配语言属性的语音。</summary>
    private static unsafe bool TrySelectVoiceFromCategory(
        ISpVoice voice,
        string categoryId,
        string requiredAttribute)
    {
        using var category = ComObject.CoCreate<ISpObjectTokenCategory>(VoiceTokenCategoryClassId)
            ?? throw new InvalidOperationException("无法打开 Windows SAPI 语音分类。");

        IEnumSpObjectTokens tokenEnumeratorObject;
        fixed (char* categoryIdPointer = categoryId)
        fixed (char* requiredAttributePointer = requiredAttribute)
        {
            category.Object
                .SetId(new PWSTR(categoryIdPointer), false)
                .ThrowOnError();
            category.Object
                .EnumTokens(
                    new PWSTR(requiredAttributePointer),
                    PWSTR.Null,
                    out tokenEnumeratorObject)
                .ThrowOnError();
        }

        using var tokenEnumerator = new ComObject<IEnumSpObjectTokens>(tokenEnumeratorObject);
        var tokenCount = 0u;
        tokenEnumerator.Object.GetCount(ref tokenCount).ThrowOnError();
        if (tokenCount == 0)
        {
            return false;
        }

        tokenEnumerator.Object.Item(0, out var tokenObject).ThrowOnError();
        using var token = new ComObject<ISpObjectToken>(tokenObject);
        voice.SetVoice(token.Object).ThrowOnError();
        return true;
    }

    /// <summary>固定托管字符串并异步提交给 SAPI，保证原生调用期间指针有效。</summary>
    private static unsafe void SpeakMessage(ISpVoice voice, string message)
    {
        fixed (char* text = message)
        {
            voice.Speak(
                    new PWSTR(text),
                    (uint)(SPEAKFLAGS.SPF_ASYNC | SPEAKFLAGS.SPF_IS_NOT_XML),
                    IntPtr.Zero)
                .ThrowOnError();
        }
    }

    /// <summary>清空未完成播报并在 COM apartment 关闭前释放 SAPI 对象。</summary>
    private void StopAndReleaseVoice(IComObject<ISpVoice>? voice)
    {
        if (voice is null)
        {
            return;
        }

        try
        {
            voice.Object.Speak(
                    PWSTR.Null,
                    (uint)SPEAKFLAGS.SPF_PURGEBEFORESPEAK,
                    IntPtr.Zero)
                .ThrowOnError();
        }
        catch (Exception exception)
        {
            ReportError("停止 Windows TTS", exception);
        }
        finally
        {
            voice.Dispose();
        }
    }

    /// <summary>停止接收新文本，供后台线程结束阻塞枚举并进入资源释放流程。</summary>
    private void CompleteAdding()
    {
        lock (_syncRoot)
        {
            if (!_messages.IsAddingCompleted)
            {
                _messages.CompleteAdding();
            }
        }
    }

    /// <summary>校验公开配置，避免把无效范围传递给 SAPI 或线程等待逻辑。</summary>
    private static void ValidateOptions(WindowsTextToSpeechOptions options)
    {
        if (options.Rate is < -10 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.Rate),
                options.Rate,
                "TTS 语速必须在 -10 到 10 之间。");
        }

        if (options.Volume is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.Volume),
                options.Volume,
                "TTS 音量必须在 0 到 100 之间。");
        }

        if (options.PreferredLanguageLcid is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.PreferredLanguageLcid),
                options.PreferredLanguageLcid,
                "首选语音 LCID 必须是有效的正整数。");
        }

        if (options.ShutdownTimeout <= TimeSpan.Zero
            || options.ShutdownTimeout.TotalMilliseconds > MaximumThreadJoinTimeoutMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.ShutdownTimeout),
                options.ShutdownTimeout,
                $"TTS 线程退出等待时间必须大于零且不超过 {int.MaxValue} 毫秒。");
        }
    }

    /// <summary>把组件内部错误通知给构造回调和事件订阅者，订阅者异常不会中断语音线程。</summary>
    private void ReportError(string operation, Exception exception)
    {
        var eventArgs = new WindowsTextToSpeechErrorEventArgs(operation, exception);
        try
        {
            _errorCallback?.Invoke(eventArgs);
        }
        catch
        {
            // 调用方错误处理不能反向破坏 TTS 资源释放和后续播报。
        }

        var handlers = ErrorOccurred;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<WindowsTextToSpeechErrorEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch
            {
                // 单个事件处理器失败时仍需继续通知其他订阅者。
            }
        }
    }

    /// <summary>停止接收新文本并等待专用语音线程释放 SAPI 和 COM 资源。</summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_messages.IsAddingCompleted)
            {
                _messages.CompleteAdding();
            }
        }

        // 错误回调允许释放组件；语音线程不能等待自身，否则会一直阻塞到超时。
        if (ReferenceEquals(Thread.CurrentThread, _speechThread))
        {
            return;
        }

        if (_speechThread.Join(_shutdownTimeout))
        {
            _messages.Dispose();
            return;
        }

        ReportError(
            "等待 Windows TTS 线程退出",
            new TimeoutException("等待 Windows TTS 线程退出超时。"));
    }

    /// <summary>为专用语音线程初始化 COM 多线程 apartment。</summary>
    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr reserved, uint concurrencyModel);

    /// <summary>释放专用语音线程的 COM apartment。</summary>
    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
}
