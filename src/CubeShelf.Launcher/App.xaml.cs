using System.Windows;
using System.Windows.Threading;

namespace CubeShelf.Launcher;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\CubeShelf.Launcher.SingleInstance.v1";
    private const string ActivationEventName = @"Local\CubeShelf.Launcher.Activate.v1";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _ownsInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _instanceMutex = new Mutex(
                initiallyOwned: true,
                name: InstanceMutexName,
                createdNew: out var createdNew);

            _ownsInstanceMutex = createdNew;

            if (!createdNew)
            {
                try
                {
                    using var activation = EventWaitHandle.OpenExisting(ActivationEventName);
                    activation.Set();
                }
                catch
                {
                    // The primary instance may still be starting. Exiting this
                    // duplicate is safer than running two launchers/updaters.
                }

                Shutdown(0);
                return;
            }

            _activationEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                ActivationEventName);

            _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                (_, _) =>
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(ActivatePrimaryWindow));
                    }
                    catch
                    {
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch
        {
            // If Windows refuses named synchronization objects, CubeShelf still
            // starts rather than becoming unusable.
        }

        base.OnStartup(e);
    }

    private void ActivatePrimaryWindow()
    {
        var window = MainWindow;
        if (window is null)
            return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        if (!window.IsVisible)
            window.Show();

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _activationRegistration?.Unregister(null); } catch { }
        try { _activationEvent?.Dispose(); } catch { }

        if (_ownsInstanceMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { }
        }

        try { _instanceMutex?.Dispose(); } catch { }
        base.OnExit(e);
    }

    private static string CrashLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf",
            "Logs",
            "CubeShelf-crash.log");

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);

            File.WriteAllText(
                CrashLogPath,
                $"CubeShelf crash - {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n\r\n{ex}");
        }
        catch
        {
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);

        MessageBox.Show(
            $"CubeShelf a rencontré une erreur.\n\n" +
            $"Un journal a été créé ici :\n{CrashLogPath}\n\n" +
            $"{e.Exception.Message}",
            "CubeShelf",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown(-1);
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashLog(ex);
    }
}
