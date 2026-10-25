using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace CubeShelf.Mobile;

public sealed class MobileApp : Application
{
    public static void ConfigureRenderDiagnostics(bool batch, Action<double, long>? frontend, Action<double>? backend)
    {
        Desktop.Controls.GameCaseControl.BatchGpuDraws = batch;
        Desktop.Controls.GameCaseControl.FrontendTiming = frontend;
        Desktop.Controls.GameCaseControl.BackendTiming = backend;
    }
    public static Action<Action<TimeSpan>>? AnimationFrameScheduler
    {
        get => Desktop.Controls.DemandAnimation.FrameScheduler;
        set => Desktop.Controls.DemandAnimation.FrameScheduler = value;
    }
    public static IMobileGameService? Games { get; set; }
    public override void Initialize()
    {
        Desktop.Controls.GameCaseControl.PreferGpuTextures = true;
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
