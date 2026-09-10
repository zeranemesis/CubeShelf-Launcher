using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CubeShelf.Launcher;

public partial class MainWindow
{
    private const uint MonitorDefaultToNearest = 2;

    private bool _applyingWindowFit;
    private nint _lastWindowMonitor;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(
        nint hwnd,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref NativeMonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(
        nint hwnd);

    private void InitializeAdaptiveWindow()
    {
        SourceInitialized += (_, _) =>
        {
            RestorePreferredWindowSize();
            FitWindowToCurrentScreen(force: true);
        };

        LocationChanged += (_, _) =>
        {
            if (!_preferences.AutoFitWindowToScreen ||
                _applyingWindowFit ||
                WindowState != WindowState.Normal)
            {
                return;
            }

            var handle =
                new WindowInteropHelper(this).Handle;

            if (handle == 0)
                return;

            var monitor =
                MonitorFromWindow(
                    handle,
                    MonitorDefaultToNearest);

            if (monitor == 0 ||
                monitor == _lastWindowMonitor)
            {
                return;
            }

            _lastWindowMonitor = monitor;

            Dispatcher.BeginInvoke(
                new Action(() =>
                    FitWindowToCurrentScreen(force: false)),
                System.Windows.Threading.DispatcherPriority.Background);
        };

        SizeChanged += (_, _) =>
        {
            if (_applyingWindowFit)
                return;

            UpdateResponsiveWindowContent();
        };

        Closing += (_, _) =>
        {
            SavePreferredWindowSize();
        };
    }

    private void RestorePreferredWindowSize()
    {
        if (_preferences.WindowWidth >= 700)
            Width = _preferences.WindowWidth;

        if (_preferences.WindowHeight >= 500)
            Height = _preferences.WindowHeight;
    }

    private void SavePreferredWindowSize()
    {
        if (_applyingWindowFit ||
            WindowState != WindowState.Normal)
        {
            return;
        }

        if (ActualWidth >= 700)
            _preferences.WindowWidth = ActualWidth;

        if (ActualHeight >= 500)
            _preferences.WindowHeight = ActualHeight;

        _preferencesService.Save(_preferences);
    }

    private void FitWindowNow_Click(
        object sender,
        RoutedEventArgs e)
    {
        FitWindowToCurrentScreen(force: true);

        ShowToast(
            IsEnglish ? "Window adjusted" : "FenÃªtre adaptÃ©e",
            IsEnglish
                ? "CubeShelf has been resized to fit the current screen."
                : "CubeShelf a Ã©tÃ© redimensionnÃ© pour tenir dans l'Ã©cran actuel.");
    }

    private void FitWindowToCurrentScreen(
        bool force)
    {
        if (!force &&
            !_preferences.AutoFitWindowToScreen)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            UpdateResponsiveWindowContent();
            return;
        }

        var handle =
            new WindowInteropHelper(this).Handle;

        if (handle == 0)
            return;

        var monitor =
            MonitorFromWindow(
                handle,
                MonitorDefaultToNearest);

        if (monitor == 0)
            return;

        var info =
            new NativeMonitorInfo
            {
                Size = Marshal.SizeOf<NativeMonitorInfo>()
            };

        if (!GetMonitorInfo(
                monitor,
                ref info))
        {
            return;
        }

        _lastWindowMonitor = monitor;

        var dpi = 96d;

        try
        {
            var nativeDpi =
                GetDpiForWindow(handle);

            if (nativeDpi > 0)
                dpi = nativeDpi;
        }
        catch
        {
            dpi = 96d;
        }

        var scale = dpi / 96d;

        var workLeft = info.Work.Left / scale;
        var workTop = info.Work.Top / scale;
        var workWidth =
            (info.Work.Right - info.Work.Left) / scale;
        var workHeight =
            (info.Work.Bottom - info.Work.Top) / scale;

        const double outerMargin = 18;

        var availableWidth =
            Math.Max(
                720,
                workWidth - outerMargin * 2);

        var availableHeight =
            Math.Max(
                500,
                workHeight - outerMargin * 2);

        MinWidth =
            Math.Min(
                960,
                availableWidth);

        MinHeight =
            Math.Min(
                560,
                availableHeight);

        var preferredWidth =
            _preferences.WindowWidth >= 700
                ? _preferences.WindowWidth
                : 1530;

        var preferredHeight =
            _preferences.WindowHeight >= 500
                ? _preferences.WindowHeight
                : 930;

        var targetWidth =
            Math.Clamp(
                preferredWidth,
                MinWidth,
                availableWidth);

        var targetHeight =
            Math.Clamp(
                preferredHeight,
                MinHeight,
                availableHeight);

        var tooLarge =
            Width > availableWidth ||
            Height > availableHeight;

        if (!force && !tooLarge)
        {
            UpdateResponsiveWindowContent();
            return;
        }

        _applyingWindowFit = true;

        try
        {
            Width = targetWidth;
            Height = targetHeight;

            Left =
                workLeft +
                (workWidth - Width) / 2;

            Top =
                workTop +
                (workHeight - Height) / 2;
        }
        finally
        {
            _applyingWindowFit = false;
        }

        UpdateResponsiveWindowContent();
    }

    private void UpdateResponsiveWindowContent()
    {
        if (!IsLoaded)
            return;

        if (ActualWidth < 1120)
        {
            GameCase.Width = 300;
            GameCase.Height = 375;
            SelectedTitle.FontSize = 36;
        }
        else if (ActualWidth < 1360)
        {
            GameCase.Width = 380;
            GameCase.Height = 475;
            SelectedTitle.FontSize = 44;
        }
        else
        {
            GameCase.Width = 465;
            GameCase.Height = 580;
            SelectedTitle.FontSize = 54;
        }
    }
}

