using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;
using CubeShelf.Desktop;
using CubeShelf.Desktop.Controls;

static class BigPictureSoundChecks
{
    public static void Run(MainWindow window, List<string?> played, Action advance, string? snapshots)
    {
        void Check(bool good, string message) { if (!good) throw new Exception(message); }
        long clock = 0;
        var cues = new List<string?>();
        var player = new BigPictureSounds(cue => cues.Add(cue), () => clock);
        player.Click();player.Click();clock = 79;player.Click();
        Check(cues.SequenceEqual(new[] { "click.wav" }), "Rapid input stacked UI sounds.");
        clock = 80;player.Click();player.Launch();player.Click();clock = 579;player.Click();
        Check(cues.SequenceEqual(new[] { "click.wav", "click.wav", "launch.wav" }), "The launch cue was clipped by navigation.");
        clock = 580;player.Click();player.Stop();player.Click();
        Check(cues.TakeLast(3).SequenceEqual(new string?[] { "click.wav", null, "click.wav" }), "Stopping audio did not reset its rate limit.");
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
        Check(File.Exists(Path.Combine(folder,"click.wav")) && File.Exists(Path.Combine(folder,"launch.wav")), "Published UI sound assets are missing.");
        Check(!File.ReadAllBytes(Path.Combine(folder,"click.wav")).SequenceEqual(File.ReadAllBytes(Path.Combine(folder,"launch.wav"))), "Game launch must have its own sound.");

        void Next() { advance();played.Clear(); }
        void Click(string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Next();Click("NavSettingsButton");
        Check(played.Count == 0, "Classic desktop controls unexpectedly played Big Picture audio.");
        var enabled = window.FindControl<CheckBox>("BigPictureSoundsBox")!;
        Check(enabled.IsChecked == true, "Existing preferences must enable the requested sounds by default.");
        Next();Click("BigPictureEntryButton");
        Next();window.HandleBigPictureControllerInput(12);
        Check(played.SequenceEqual(new[] { "click.wav" }), "Controller navigation did not play exactly one click.");
        Next();window.HandleBigPictureControllerInput(5);
        Next();window.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=Key.Right });
        Check(played.SequenceEqual(new[] { "click.wav" }), "Keyboard navigation did not play exactly one click.");
        Next();Click("BigPictureLayoutButton");
        Check(played.SequenceEqual(new[] { "click.wav" }), "Mouse/button activation did not play exactly one click.");

        enabled.IsChecked = false;
        Check(new UserPreferencesStore(new PlatformPaths()).Load().BigPictureSounds == false && played.Last() is null,
            "Mute must persist and stop a currently playing sound.");
        Next();window.HandleBigPictureControllerInput(12);Click("BigPictureLayoutButton");
        Check(played.Count == 0, "Muted Big Picture still played navigation sounds.");
        var feedback = typeof(MainWindow).GetMethod("PlayBigPictureSound",BindingFlags.NonPublic|BindingFlags.Instance)!;
        feedback.Invoke(window,[true]);
        Check(played.Count == 0, "Muted Big Picture still played a launch sound.");
        enabled.IsChecked = true;
        Check(new UserPreferencesStore(new PlatformPaths()).Load().BigPictureSounds, "Unmuting did not persist.");

        Next();window.HandleBigPictureControllerInput(15);
        var menu = window.FindControl<Border>("BigPictureMenuOverlay")!;
        var muteRow = menu.GetLogicalDescendants().OfType<Button>().Single(button =>
            button.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text is "Sons de Big Picture" or "Big Picture sounds"));
        muteRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(enabled.IsChecked == false, "Controller-menu mute and settings checkbox diverged.");
        menu.GetLogicalDescendants().OfType<Button>().Single(button =>
            button.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text is "Sons de Big Picture" or "Big Picture sounds"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(enabled.IsChecked == true, "Controller menu could not restore audio.");
        if (snapshots is not null)
        {
            Directory.CreateDirectory(snapshots);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(snapshots,"big-picture-sounds-menu.png"));
        }
        window.HandleBigPictureControllerInput(15);

        // A real short-lived host process exercises the success hook without any ROM or game runtime.
        var fixture = new GameCatalogEntry { Id="audio-launch-fixture", Title="Audio fixture", RuntimeOwnsSetup=true,
            Executable=OperatingSystem.IsWindows()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"):"/bin/true",
            LaunchArguments=OperatingSystem.IsWindows()?["/c","exit","0"]:[], LaunchEnvironment=[] };
        var launch = typeof(MainWindow).GetMethod("LaunchReadyGame",BindingFlags.NonPublic|BindingFlags.Instance)!;
        Next();launch.Invoke(window,[fixture]);
        Check(played.SequenceEqual(new[] { "launch.wav" }), "A successful process start did not play the distinct launch cue.");
        Next();fixture.Executable=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+"-missing.exe");
        try { launch.Invoke(window,[fixture]);throw new Exception("Missing launch unexpectedly succeeded."); }
        catch (TargetInvocationException) { }
        Check(played.Count == 0, "A failed process start played a success chime.");
        Next();Click("BigPictureExitButton");Next();feedback.Invoke(window,[true]);Click("NavSettingsButton");
        Check(played.Count == 0, "Leaving Big Picture did not silence its audio.");
        Console.WriteLine("OK  distinct click/launch cues, repeat throttling, launch priority, desktop silence, persisted mute and successful/failed process starts.");
    }
}
