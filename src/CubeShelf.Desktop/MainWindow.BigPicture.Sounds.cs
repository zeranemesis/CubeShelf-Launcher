using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CubeShelf.Desktop.Controls;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private readonly BigPictureSounds _bigPictureSounds;

    private void InitializeBigPictureSounds()
    {
        AddHandler(Button.ClickEvent, (_, _) => PlayBigPictureSound(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, args) =>
        {
            if (args.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) PlayBigPictureSound();
        });
        Closed += (_, _) => _bigPictureSounds.Stop();
    }

    private void PlayBigPictureSound(bool launching = false)
    {
        if (!_bigPictureActive || _bigPictureClosed || !_preferences.BigPictureSounds || _loadingSettings) return;
        if(_presentation?.IsDual==true)return;
        if (launching) _bigPictureSounds.Launch();
        else if (!LaunchTransition.IsVisible) _bigPictureSounds.Click();
    }
}
