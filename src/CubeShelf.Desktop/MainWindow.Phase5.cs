using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private bool _phase5ApplyingWindowFit;
    private bool _phase5LoadingSettings;
    private bool _phase5ScreensHooked;
    private string _phase5LastScreen = "";
    private CancellationTokenSource? _phase5ThemeTransition;

    private void InitializePhase5Polish()
    {
        UiLocalization.Apply(_preferences.Language);
        _phase5LoadingSettings = true;
        AutoFitWindowBox.IsChecked = _preferences.AutoFitWindowToScreen;
        _phase5LoadingSettings = false;

        LanguagePicker.SelectionChanged += Phase5LanguageChanged;
        ThemePicker.SelectionChanged += Phase5ThemeChanged;
        PositionChanged += Phase5PositionChanged;
        ActualThemeVariantChanged += (_, _) => ApplyNativeTitleBarPhase5();

        VerifyContentViewsAreTagged();
        foreach (var view in ContentViews)
            view.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty && view.IsVisible)
                    Dispatcher.UIThread.Post(UpdateNavigationStatePhase5);
            };

        Dispatcher.UIThread.Post(() =>
        {
            UpdateNavigationStatePhase5();
            ApplyNativeTitleBarPhase5();
            if (_preferences.AutoFitWindowToScreen)
                FitWindowToCurrentScreenPhase5(force: true);
        }, DispatcherPriority.Background);
    }

    private void DisposePhase5Polish()
    {
        _phase5ThemeTransition?.Cancel();
        _phase5ThemeTransition?.Dispose();
        _phase5ThemeTransition = null;

        if (_phase5ScreensHooked)
        {
            try { Screens.Changed -= Phase5ScreensChanged; } catch { }
            _phase5ScreensHooked = false;
        }
    }

    private void Phase5LanguageChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_phase5LoadingSettings) return;
        var language = (LanguagePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "fr";
        UiLocalization.Apply(language);
        RefreshLocalizedPhase5State(language);
    }

    private async void Phase5ThemeChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_phase5LoadingSettings || Application.Current is null) return;

        _phase5ThemeTransition?.Cancel();
        _phase5ThemeTransition?.Dispose();
        _phase5ThemeTransition = new CancellationTokenSource();
        var token = _phase5ThemeTransition.Token;

        try
        {
            ThemeTransitionOverlay.Opacity = .20;
            await Task.Delay(80, token);
            var theme = (ThemePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "dark";
            Application.Current.RequestedThemeVariant = theme.Equals("light", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            ApplyNativeTitleBarPhase5();
            await Task.Delay(70, token);
            ThemeTransitionOverlay.Opacity = 0;
        }
        catch (OperationCanceledException)
        {
            ThemeTransitionOverlay.Opacity = 0;
        }
    }

    private void RefreshLocalizedPhase5State(string language)
    {
        var english = UiLocalization.IsEnglish(language);
        LibrarySectionTitle.Text = _favoritesOnly
            ? UiLocalization.Get(language, "FavoritesTitle")
            : UiLocalization.Get(language, "LibraryTitle");

        EmptyLibraryText.Text = UiLocalization.Get(language, "NoLibraryMatch");
        RefreshLibraryParity();
        RefreshParityGameDetails();
        RefreshPortableQueueView();
        RefreshDolphinStatusPhase4Core();

        if (english)
            SettingsStatus.Text = "Language changed. Dynamic status messages are progressively localized as their state refreshes.";
        else
            SettingsStatus.Text = UiLocalization.IsEnglish(language) ? "Language changed." : "Langue modifiée.";
    }

    private void SaveAutoFitPhase5(object? sender, RoutedEventArgs args)
    {
        if (_phase5LoadingSettings) return;
        _preferences = _preferences with { AutoFitWindowToScreen = AutoFitWindowBox.IsChecked == true };
        _preferencesStore.Save(_preferences);
        if (_preferences.AutoFitWindowToScreen)
            FitWindowToCurrentScreenPhase5(force: true);
    }

    private void FitWindowNowPhase5(object? sender, RoutedEventArgs args)
    {
        FitWindowToCurrentScreenPhase5(force: true);
        ShowToastParity(
            UiLocalization.IsEnglish(_preferences.Language) ? "Window adjusted" : "Fenêtre adaptée",
            UiLocalization.IsEnglish(_preferences.Language)
                ? "CubeShelf now fits the current screen work area."
                : "CubeShelf tient maintenant dans la zone visible de l’écran actuel.");
    }

    private void Phase5PositionChanged(object? sender, PixelPointEventArgs args)
    {
        if (_phase5ApplyingWindowFit || !_preferences.AutoFitWindowToScreen || WindowState != WindowState.Normal)
            return;

        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            var identity = $"{screen.DisplayName}|{screen.WorkingArea}|{screen.Scaling:0.###}";
            if (identity == _phase5LastScreen) return;
            _phase5LastScreen = identity;
            Dispatcher.UIThread.Post(() => FitWindowToCurrentScreenPhase5(force: false), DispatcherPriority.Background);
        }
        catch { }
    }

    private void Phase5ScreensChanged(object? sender, EventArgs args)
    {
        if (_preferences.AutoFitWindowToScreen)
            Dispatcher.UIThread.Post(() => FitWindowToCurrentScreenPhase5(force: false), DispatcherPriority.Background);
    }

    private void FitWindowToCurrentScreenPhase5(bool force)
    {
        if (_phase5ApplyingWindowFit || (!force && !_preferences.AutoFitWindowToScreen)) return;
        if (WindowState == WindowState.Maximized)
        {
            UpdateResponsiveParity();
            return;
        }

        try
        {
            if (!_phase5ScreensHooked)
            {
                Screens.Changed += Phase5ScreensChanged;
                _phase5ScreensHooked = true;
            }

            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1d;
            var work = screen.WorkingArea;
            var workWidth = work.Width / scale;
            var workHeight = work.Height / scale;
            const double outerMargin = 18;

            var availableWidth = Math.Max(720, workWidth - outerMargin * 2);
            var availableHeight = Math.Max(500, workHeight - outerMargin * 2);

            MinWidth = Math.Min(960, availableWidth);
            MinHeight = Math.Min(560, availableHeight);

            var preferredWidth = _preferences.WindowWidth >= 700 ? _preferences.WindowWidth : 1530;
            var preferredHeight = _preferences.WindowHeight >= 500 ? _preferences.WindowHeight : 930;
            var targetWidth = Math.Clamp(preferredWidth, MinWidth, availableWidth);
            var targetHeight = Math.Clamp(preferredHeight, MinHeight, availableHeight);
            var tooLarge = Width > availableWidth || Height > availableHeight;

            _phase5LastScreen = $"{screen.DisplayName}|{screen.WorkingArea}|{screen.Scaling:0.###}";
            if (!force && !tooLarge)
            {
                UpdateResponsiveParity();
                return;
            }

            _phase5ApplyingWindowFit = true;
            try
            {
                Width = targetWidth;
                Height = targetHeight;
                var physicalWidth = (int)Math.Round(targetWidth * scale);
                var physicalHeight = (int)Math.Round(targetHeight * scale);
                Position = new PixelPoint(
                    work.X + Math.Max(0, (work.Width - physicalWidth) / 2),
                    work.Y + Math.Max(0, (work.Height - physicalHeight) / 2));
            }
            finally
            {
                _phase5ApplyingWindowFit = false;
            }

            UpdateResponsiveParity();
        }
        catch
        {
            // Layout adaptation must never make the launcher unusable.
        }
    }

    private void UpdateNavigationStatePhase5()
    {
        SetNavSelectedPhase5(NavLibraryButton, GameView.IsVisible || (LibraryView.IsVisible && !_favoritesOnly));
        SetNavSelectedPhase5(NavFavoritesButton, LibraryView.IsVisible && _favoritesOnly);
        SetNavSelectedPhase5(NavModsButton, ModsView.IsVisible);
        SetNavSelectedPhase5(NavDownloadsButton, DownloadsView.IsVisible);
        SetNavSelectedPhase5(NavSettingsButton, SettingsView.IsVisible);
    }

    private static void SetNavSelectedPhase5(Button button, bool selected)
    {
        if (selected)
        {
            if (!button.Classes.Contains("selected")) button.Classes.Add("selected");
        }
        else
        {
            button.Classes.Remove("selected");
        }
    }

    private void ApplyNativeTitleBarPhase5()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = TryGetPlatformHandle()?.Handle;
            if (handle is null || handle.Value == IntPtr.Zero) return;
            var dark = ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle.Value, 20, ref dark, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
