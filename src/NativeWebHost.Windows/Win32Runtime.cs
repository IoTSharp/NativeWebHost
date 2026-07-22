using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace NativeWebHost.Windows;

/// <summary>
/// AOT-compatible <see cref="IDesktopRuntime"/> that creates raw Win32 windows
/// and runs native message loops without any WinForms or WPF dependency.
/// </summary>
/// <remarks>
/// Each host window runs on its own dedicated STA thread so that COM-backed
/// browser adapters such as WebView2 always execute in a compatible apartment.
/// </remarks>
public sealed class Win32Runtime : IMultiWindowDesktopRuntime
{
    private readonly IHostWindowFactory _windowFactory;
    private readonly HostWindowCoordinator _coordinator;
    private readonly Win32RuntimeOptions _runtimeOptions;
    private readonly object _executionGate = new();
    private RuntimeExecution? _currentExecution;

    /// <summary>
    /// Creates a Win32 runtime using the default raw Win32 host window implementation.
    /// </summary>
    public Win32Runtime()
        : this(new Win32RuntimeOptions())
    {
    }

    /// <summary>使用默认原生 Win32 窗口和指定 Windows 应用集成配置创建运行时。</summary>
    public Win32Runtime(Win32RuntimeOptions runtimeOptions)
        : this(new Win32HostWindowFactory(), runtimeOptions)
    {
    }

    /// <summary>
    /// Creates a Win32 runtime with a custom host-window factory.
    /// </summary>
    public Win32Runtime(IHostWindowFactory windowFactory)
        : this(windowFactory, new Win32RuntimeOptions())
    {
    }

    /// <summary>使用自定义窗口工厂和 Windows 应用集成配置创建运行时。</summary>
    public Win32Runtime(IHostWindowFactory windowFactory, Win32RuntimeOptions runtimeOptions)
        : this(windowFactory, new HostWindowCoordinator(), runtimeOptions)
    {
    }

    /// <summary>使用默认 Windows 应用集成配置创建可注入协调器的内部运行时。</summary>
    internal Win32Runtime(IHostWindowFactory windowFactory, HostWindowCoordinator coordinator)
        : this(windowFactory, coordinator, new Win32RuntimeOptions())
    {
    }

    /// <summary>集中初始化运行时依赖，供公开构造函数和内部测试入口复用。</summary>
    internal Win32Runtime(
        IHostWindowFactory windowFactory,
        HostWindowCoordinator coordinator,
        Win32RuntimeOptions runtimeOptions)
    {
        _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));
    }

    /// <inheritdoc/>
    public void Run(
        NativeWebHostOptions options,
        IWebViewAdapterFactory adapterFactory,
        IDesktopApp? desktopApp)
        => Run(options, Array.Empty<NativeWebWindowDefinition>(), adapterFactory, desktopApp);

    /// <inheritdoc/>
    public void Run(
        NativeWebHostOptions options,
        IReadOnlyList<NativeWebWindowDefinition> additionalWindows,
        IWebViewAdapterFactory adapterFactory,
        IDesktopApp? desktopApp)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(additionalWindows);
        ArgumentNullException.ThrowIfNull(adapterFactory);

        // 提权会启动替代进程；当前进程必须在创建任何窗口前直接结束本次运行。
        if (!PrepareWindowsApplication(options))
        {
            return;
        }

        RuntimeExecution execution;
        lock (_executionGate)
        {
            if (_currentExecution is not null)
                throw new InvalidOperationException("This Win32Runtime instance is already running.");

            execution = new RuntimeExecution(this, adapterFactory, desktopApp);
            _currentExecution = execution;
        }

        try
        {
            execution.OpenMainWindow(options);

            foreach (var window in additionalWindows)
            {
                ArgumentNullException.ThrowIfNull(window);
                execution.OpenAdditionalWindow(window);
            }

            execution.WaitForCompletion();
            execution.ThrowIfFailed();
        }
        finally
        {
            execution.Complete();

            lock (_executionGate)
            {
                if (ReferenceEquals(_currentExecution, execution))
                    _currentExecution = null;
            }
        }
    }

    /// <summary>在主窗口启动前完成提权、最高权限自启动和桌面快捷方式注册。</summary>
    private bool PrepareWindowsApplication(NativeWebHostOptions hostOptions)
    {
        if (!_runtimeOptions.RequireAdministrator
            && !_runtimeOptions.EnsureElevatedAutoStart
            && !_runtimeOptions.EnsureDesktopShortcut)
        {
            return true;
        }

        var applicationPath = ResolveApplicationPath();
        if (_runtimeOptions.RequireAdministrator
            && !WindowsApplicationRegistration.IsCurrentProcessAdministrator())
        {
            WindowsApplicationRegistration.RestartAsAdministrator(
                applicationPath,
                ResolveAdministratorRestartArguments(applicationPath));
            return false;
        }

        if (_runtimeOptions.EnsureElevatedAutoStart)
        {
            WindowsApplicationRegistration.EnsureElevatedLogonTask(
                new WindowsElevatedLogonTaskOptions
                {
                    TaskName = ResolveConfiguredName(_runtimeOptions.AutoStartTaskName, hostOptions.Title),
                    ExecutablePath = applicationPath,
                    Arguments = _runtimeOptions.AutoStartArguments,
                    ObsoleteRunKeyValueName = _runtimeOptions.ObsoleteRunKeyValueName
                });
        }

        if (_runtimeOptions.EnsureDesktopShortcut)
        {
            WindowsApplicationRegistration.EnsureDesktopShortcut(
                new WindowsDesktopShortcutOptions
                {
                    ShortcutName = ResolveConfiguredName(_runtimeOptions.DesktopShortcutName, hostOptions.Title),
                    TargetPath = applicationPath,
                    Arguments = _runtimeOptions.DesktopShortcutArguments,
                    WorkingDirectory = _runtimeOptions.DesktopShortcutWorkingDirectory,
                    IconPath = _runtimeOptions.DesktopShortcutIconPath ?? hostOptions.IconPath ?? applicationPath,
                    IconIndex = _runtimeOptions.DesktopShortcutIconIndex,
                    Description = _runtimeOptions.DesktopShortcutDescription ?? hostOptions.Title
                });
        }

        return true;
    }

    /// <summary>取得调用方指定或当前进程使用的应用程序绝对路径。</summary>
    private string ResolveApplicationPath()
    {
        var path = _runtimeOptions.ApplicationPath ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("无法取得 Windows 应用程序路径。");
        }

        return Path.GetFullPath(path);
    }

    /// <summary>取得提权重启参数；普通 apphost 跳过 argv[0]，dotnet 托管模式保留程序集参数。</summary>
    private IReadOnlyList<string> ResolveAdministratorRestartArguments(string applicationPath)
    {
        if (_runtimeOptions.AdministratorRestartArguments is not null)
        {
            return _runtimeOptions.AdministratorRestartArguments;
        }

        var commandLineArguments = Environment.GetCommandLineArgs();
        if (commandLineArguments.Length == 0)
        {
            return Array.Empty<string>();
        }

        return PathsEqual(commandLineArguments[0], applicationPath)
            ? commandLineArguments.Skip(1).ToArray()
            : commandLineArguments;
    }

    /// <summary>比较可能带相对路径的 Windows 应用程序路径。</summary>
    private static bool PathsEqual(string firstPath, string secondPath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(firstPath),
                Path.GetFullPath(secondPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>优先使用显式名称，并在缺失时使用主窗口标题。</summary>
    private static string ResolveConfiguredName(string? configuredName, string hostTitle)
    {
        var value = string.IsNullOrWhiteSpace(configuredName) ? hostTitle : configuredName;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Windows 应用注册名称不能为空。");
        }

        return value.Trim();
    }

    private void RunWindow(
        HostWindowDefinition definition,
        RuntimeExecution execution)
    {
        if (definition.IsMainWindow)
        {
            _coordinator.RunMainWindow(
                definition.Options,
                execution.WindowManager,
                execution.AdapterFactory,
                _windowFactory,
                execution.DesktopApp);
            return;
        }

        _coordinator.RunAdditionalWindow(
            definition.WindowId,
            definition.Options,
            execution.WindowManager,
            execution.AdapterFactory,
            _windowFactory,
            execution.DesktopApp);
    }

    private Thread CreateWindowThread(
        HostWindowDefinition definition,
        RuntimeExecution execution)
    {
        var thread = new Thread(() =>
        {
            try
            {
                RunWindow(definition, execution);
            }
            catch (Exception ex)
            {
                execution.RecordFailure(ex);
            }
            finally
            {
                execution.OnWindowThreadCompleted(definition.WindowId);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Name = definition.IsMainWindow
            ? "NativeWebHost-UI"
            : $"NativeWebHost-UI-{definition.WindowId}";

        return thread;
    }

    private sealed class RuntimeExecution
    {
        private readonly Win32Runtime _runtime;
        private readonly ManualResetEventSlim _allWindowsClosed = new(initialState: true);
        private readonly ConcurrentQueue<Exception> _failures = new();
        private readonly object _gate = new();
        private readonly HashSet<string> _scheduledWindowIds = new(StringComparer.Ordinal);
        private bool _isCompleted;
        private int _activeWindowThreads;

        public RuntimeExecution(
            Win32Runtime runtime,
            IWebViewAdapterFactory adapterFactory,
            IDesktopApp? desktopApp)
        {
            _runtime = runtime;
            AdapterFactory = adapterFactory;
            DesktopApp = desktopApp;
            WindowManager = new RuntimeWindowManager(this);
        }

        public IWebViewAdapterFactory AdapterFactory { get; }

        public IDesktopApp? DesktopApp { get; }

        public INativeWebWindowManager WindowManager { get; }

        public void OpenMainWindow(NativeWebHostOptions options)
            => OpenWindow(new HostWindowDefinition("main", options, IsMainWindow: true));

        public void OpenAdditionalWindow(NativeWebWindowDefinition window)
        {
            ArgumentNullException.ThrowIfNull(window);
            OpenWindow(new HostWindowDefinition(window.WindowId, window.Options, IsMainWindow: false));
        }

        public void WaitForCompletion() => _allWindowsClosed.Wait();

        public void ThrowIfFailed()
        {
            if (_failures.IsEmpty)
                return;

            var failures = _failures.ToArray();
            if (failures.Length == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();

            throw new AggregateException("One or more NativeWebHost windows failed.", failures);
        }

        public void Complete()
        {
            lock (_gate)
            {
                _isCompleted = true;
            }
        }

        public void RecordFailure(Exception exception)
            => _failures.Enqueue(exception);

        public void OnWindowThreadCompleted(string windowId)
        {
            lock (_gate)
            {
                _scheduledWindowIds.Remove(windowId);
            }

            if (Interlocked.Decrement(ref _activeWindowThreads) == 0)
                _allWindowsClosed.Set();
        }

        public int OpenWindowCount => _runtime._coordinator.OpenWindowCount;

        public string? MainWindowId => _runtime._coordinator.MainWindowId;

        public IReadOnlyCollection<string> GetOpenWindowIds()
            => _runtime._coordinator.GetOpenWindowIds();

        public IReadOnlyCollection<HostWindowSnapshot> GetOpenWindows()
            => _runtime._coordinator.GetOpenWindows();

        public NativeWebWindowContext? GetWindowContext(string windowId)
            => _runtime._coordinator.GetWindowContext(windowId);

        public bool TryCloseWindow(string windowId)
            => _runtime._coordinator.TryRequestClose(windowId);

        public bool TryActivateWindow(string windowId)
            => _runtime._coordinator.TryRequestActivate(windowId);

        public Task<bool> PostEventAsync(
            string windowId,
            string eventName,
            string jsonPayload,
            CancellationToken cancellationToken = default)
            => _runtime._coordinator.PostEventAsync(windowId, eventName, jsonPayload, cancellationToken);

        public Task<int> BroadcastEventAsync(
            string eventName,
            string jsonPayload,
            CancellationToken cancellationToken = default)
            => _runtime._coordinator.BroadcastEventAsync(eventName, jsonPayload, cancellationToken);

        private void OpenWindow(HostWindowDefinition definition)
        {
            lock (_gate)
            {
                if (_isCompleted)
                    throw new InvalidOperationException("The runtime is no longer accepting new windows.");

                if (!_scheduledWindowIds.Add(definition.WindowId))
                    throw new InvalidOperationException(
                        $"A host window with id '{definition.WindowId}' is already scheduled or running.");

                if (Interlocked.Increment(ref _activeWindowThreads) == 1)
                    _allWindowsClosed.Reset();
            }

            try
            {
                var thread = _runtime.CreateWindowThread(definition, this);
                thread.Start();
            }
            catch
            {
                OnWindowThreadCompleted(definition.WindowId);
                throw;
            }
        }
    }

    private sealed class RuntimeWindowManager : INativeWebWindowManager
    {
        private readonly RuntimeExecution _execution;

        public RuntimeWindowManager(RuntimeExecution execution)
        {
            _execution = execution;
        }

        public int OpenWindowCount => _execution.OpenWindowCount;

        public string? MainWindowId => _execution.MainWindowId;

        public IReadOnlyCollection<string> GetOpenWindowIds()
            => _execution.GetOpenWindowIds();

        public IReadOnlyCollection<HostWindowSnapshot> GetOpenWindows()
            => _execution.GetOpenWindows();

        public NativeWebWindowContext? GetWindowContext(string windowId)
            => _execution.GetWindowContext(windowId);

        public void OpenWindow(NativeWebWindowDefinition window)
            => _execution.OpenAdditionalWindow(window);

        public bool TryCloseWindow(string windowId)
            => _execution.TryCloseWindow(windowId);

        public bool TryActivateWindow(string windowId)
            => _execution.TryActivateWindow(windowId);

        public Task<bool> PostEventAsync(
            string windowId,
            string eventName,
            string jsonPayload,
            CancellationToken cancellationToken = default)
            => _execution.PostEventAsync(windowId, eventName, jsonPayload, cancellationToken);

        public Task<int> BroadcastEventAsync(
            string eventName,
            string jsonPayload,
            CancellationToken cancellationToken = default)
            => _execution.BroadcastEventAsync(eventName, jsonPayload, cancellationToken);
    }
}
