using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace CubeShelf.Mobile;

public sealed class MobileApp : Application
{
    public static IMobileGameService? Games { get; set; }
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single && Games is { } games)
            single.MainView = new MobileLibraryView(games);
        base.OnFrameworkInitializationCompleted();
    }
}
