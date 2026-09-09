using System.Windows;
using System.Windows.Threading;

namespace CubeShelf.Launcher;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static string CrashLogPath =>
        Path.Combine(AppContext.BaseDirectory, "CubeShelf-crash.log");

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
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
            $"CubeShelf a rencontré une erreur au démarrage.\n\n" +
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
