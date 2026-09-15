using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CubeShelf.Core.Diagnostics;
using CubeShelf.Core.Platform;

namespace CubeShelf.Desktop;

public sealed partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private CrashReporter? _crashReporter;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var paths = new PlatformPaths();
        _crashReporter = new CrashReporter(paths);
        RegisterCrashHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            _singleInstance = new SingleInstanceService(paths);
            if (!_singleInstance.TryAcquire())
            {
                desktop.Shutdown(0);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            _singleInstance.ActivationRequested += (_, _) =>
                Dispatcher.UIThread.Post(ActivatePrimaryWindow);

            desktop.MainWindow = new MainWindow();
            desktop.Exit += (_, _) => DisposeApplicationServices();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RegisterCrashHandlers()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _crashReporter?.Write(e.Exception, "Avalonia.Dispatcher", terminating: true);
        e.Handled = true;
        Dispatcher.UIThread.Post(() => _desktop?.Shutdown(-1));
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _crashReporter?.Write(exception, "AppDomain", e.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _crashReporter?.Write(e.Exception, "TaskScheduler", terminating: false);
        e.SetObserved();
    }

    private void ActivatePrimaryWindow()
    {
        var window = _desktop?.MainWindow;
        if (window is null) return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        if (!window.IsVisible) window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
    }

    private void DisposeApplicationServices()
    {
        try { _singleInstance?.Dispose(); } catch { }
        _singleInstance = null;
        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }
}
