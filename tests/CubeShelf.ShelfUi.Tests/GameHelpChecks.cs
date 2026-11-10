using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CubeShelf.Desktop;
using CubeShelf.Desktop.Controls;
using CubeShelf.Assistance;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

static class GameHelpChecks
{
    public static void Run(MainWindow main)
    {
        if (!OperatingSystem.IsWindows()) return;
        CompanionLayoutChecks.Run();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var target = typeof(MainWindow).Assembly.GetType("CubeShelf.Desktop.Controls.WindowsHelpHotkey")!
            .GetMethod("GameWindowForHelp", BindingFlags.Public | BindingFlags.Static)!;
        foreach (var (foreground, expected) in new[] { (10, 10), (20, 10), (30, 10), (40, 40), (0, 0) })
        {
            var actual = (IntPtr)target.Invoke(null, [new IntPtr(foreground), new IntPtr(20), new IntPtr(30), new IntPtr(10)])!;
            if (actual != new IntPtr(expected)) throw new Exception("Companion focus lost the game or selected a game from another app.");
        }
        Console.WriteLine("OK  game, companion and help-panel focus keep the game target; unrelated apps and no foreground never reuse it.");
        var open = typeof(MainWindow).GetMethod("ShowGameHelp", flags)!;
        var field = typeof(MainWindow).GetField("_helpWindow", flags)!;
        open.Invoke(main, null);
        if (field.GetValue(main) is not null) throw new Exception("The companion opened outside a tracked foreground game.");
        var help = new Window { Width = 340, Height = 150, SystemDecorations = SystemDecorations.None, ShowInTaskbar = false };
        var panel = new GameHelpPanel("Test", Path.Combine(Path.GetTempPath(), "no-help-model-" + Guid.NewGuid()), help.Close, compact: true);
        help.Content = panel; help.Show();
        var buttons = panel.GetLogicalDescendants().OfType<Button>().ToArray();
        if (!buttons.Any(button => button.Content?.ToString()?.Contains("Préparer Bip") == true)) throw new Exception("Optional model download is inaccessible.");
        if (panel.GetLogicalDescendants().OfType<ComboBox>().Any()) throw new Exception("The helper must offer only the two direct choices.");
        var details = panel.GetLogicalDescendants().OfType<Expander>().Single(item => item.Header?.ToString() == "Options");
        if (details.IsExpanded) throw new Exception("Writing must be secondary and collapsed initially.");
        foreach (var choice in new[] { ("Indice", HintLevel.Hint), ("Solution", HintLevel.Solution) })
        {
            buttons.Single(button => button.Content?.ToString() == choice.Item1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!Equals(typeof(GameHelpPanel).GetField("_level", flags)!.GetValue(panel), choice.Item2)) throw new Exception("The two choices route to the wrong hint levels.");
            if (typeof(GameHelpPanel).GetField("_operation", flags)!.GetValue(panel) is not null) throw new Exception("A choice started inference without the verified model.");
        }
        if (help.SystemDecorations != SystemDecorations.None || help.ShowInTaskbar || help.Width > 360)
            throw new Exception("In-game help must remain a compact borderless overlay.");
        var answerField = typeof(GameHelpPanel).GetField("_answer", flags)!;
        ((TextBlock)answerField.GetValue(panel)!).Text = "Utilise le stick pour choisir une carte.";
        field.SetValue(main, help);
        typeof(MainWindow).GetMethod("SuspendGameHelp", flags)!.Invoke(main, null);
        if (help.IsVisible || !ReferenceEquals(field.GetValue(main), help) ||
            (bool)typeof(GameHelpPanel).GetField("_closed", flags)!.GetValue(panel)!)
            throw new Exception("Leaving the game destroyed the reusable answer panel.");
        for (var repeat = 0; repeat < 3; repeat++)
        {
            typeof(MainWindow).GetMethod("RevealGameHelp", flags)!.Invoke(main, null);
            if (!help.IsVisible || ((TextBlock)answerField.GetValue(panel)!).Text != "Utilise le stick pour choisir une carte.")
                throw new Exception("Returning to Bip failed to reopen the previous answer.");
            typeof(MainWindow).GetMethod("SuspendGameHelp", flags)!.Invoke(main, null);
        }
        typeof(MainWindow).GetMethod("HideGameHelp", flags)!.Invoke(main, null);
        if (!(bool)typeof(MainWindow).GetField("_helpHiddenByUser", flags)!.GetValue(main)! || help.IsVisible)
            throw new Exception("Explicitly hiding Bip was ignored.");
        typeof(MainWindow).GetMethod("RevealGameHelp", flags)!.Invoke(main, null);
        if ((bool)typeof(MainWindow).GetField("_helpHiddenByUser", flags)!.GetValue(main)! || !help.IsVisible)
            throw new Exception("The help shortcut cannot restore a hidden companion.");
        Console.WriteLine("OK  leaving and returning reopens the same answer repeatedly; explicit hide and restore remain usable.");
        field.SetValue(main, null);
        buttons.Single(button => button.Content?.ToString() == "Réduire").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (field.GetValue(main) is not null) throw new Exception("Closing help left the window registered.");
        Console.WriteLine("OK  no helper outside a tracked game; the two choices require the model and close cleanly.");
        var fake = new VoiceFixture();
        var panelWindow = new Window { Width = 320, Height = 142, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        var voicePanel = new GameHelpPanel("Super Mario 64", Path.Combine(Path.GetTempPath(), "no-help-model-" + Guid.NewGuid()), () => panelWindow.Close(), fake, compact: true);
        panelWindow.Content = voicePanel; panelWindow.Show(); Dispatcher.UIThread.RunJobs();
        var controls = voicePanel.GetLogicalDescendants().OfType<Button>().ToArray();
        var voiceDetails = voicePanel.GetLogicalDescendants().OfType<Expander>().Single(item => item.Header?.ToString() == "Options");
        var talk = controls.Single(button => button.Content?.ToString() == "Parler");
        if (fake.Listens != 0 || fake.Reads != 0) throw new Exception("Opening the companion activated speech.");
        Save("companion-idle.png");
        CheckChoicesFit();
        panelWindow.Width = 220; Dispatcher.UIThread.RunJobs();
        Save("companion-narrow.png"); CheckChoicesFit(); panelWindow.Width = 320;
        panelWindow.Height = 380;
        controls.Single(button => button.Content?.ToString() == "Indice").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        voiceDetails.IsExpanded = true;
        talk.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (fake.Listens != 1) throw new Exception("Explicit speech action did not start one listening request.");
        Save("companion-listening.png");
        fake.Done.SetResult("Comment entrer dans le tableau ?");
        Pump(() => voicePanel.GetLogicalDescendants().OfType<TextBox>().Single(item => item.MaxLength == 2000).Text == "Comment entrer dans le tableau ?");
        if (fake.Reads != 0 || typeof(GameHelpPanel).GetField("_operation", flags)!.GetValue(voicePanel) is not null)
            throw new Exception("Dictation started inference or speech without a downloaded model and explicit submission.");
        talk.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        controls.Single(button => button.Content?.ToString() == "Arrêter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(() => talk.IsEnabled);
        if (!fake.Done.Task.IsCanceled || fake.Stops == 0) throw new Exception("Stop did not cancel listening.");
        Save("companion-details.png");
        voiceDetails.IsVisible = voiceDetails.IsExpanded = false;
        panelWindow.Width = 340; panelWindow.Height = 320; Save("companion-compact.png");
        ((TextBlock)typeof(GameHelpPanel).GetField("_answer", flags)!.GetValue(voicePanel)!).Text =
            "Déplace-toi vers une ouverture de la page, puis ajuste ta position avant qu’elle ne tombe. Reste attentif à la page suivante pour anticiper ton prochain déplacement.";
        ((TextBlock)typeof(GameHelpPanel).GetField("_status", flags)!.GetValue(voicePanel)!).IsVisible = false;
        panelWindow.Width = 320; panelWindow.Height = 320; Save("companion-answer.png");
        panelWindow.UpdateLayout();
        var helpHeader = voicePanel.GetLogicalDescendants().OfType<DockPanel>().Single(control => control.Name == "HelpHeader");
        var helpBody = voicePanel.GetLogicalDescendants().OfType<ScrollViewer>().Single(control => control.Name == "HelpBody");
        if (helpHeader.IsVisible || helpBody.Bounds.Height < 50)
            throw new Exception("Compact help repeated its introduction or clipped the answer.");
        var sources = voicePanel.GetLogicalDescendants().OfType<Expander>().Single(control => control.Header?.ToString() == "Pages consultées");
        if (!sources.GetLogicalAncestors().Contains(voiceDetails) || voiceDetails.IsVisible || voiceDetails.IsExpanded)
            throw new Exception("Sources or secondary controls cluttered the direct answer.");
        panelWindow.Close();
        if (!fake.Disposed) throw new Exception("Closing the companion did not release speech resources.");
        Console.WriteLine("OK  companion stays silent until asked, dictation remains editable, Stop cancels, and close releases speech.");
        if (Environment.GetEnvironmentVariable("DECOMPSHELF_VISION_TEST_MODEL_DIR") is { } modelDirectory)
        {
            var captures = 0;
            var visualWindow = new Window { Width = 340, Height = 380 };
            var visual = new GameHelpPanel("Test visuel", modelDirectory, visualWindow.Close, compact: true,
                capture: _ => { captures++; throw new InvalidOperationException("Capture contrôlée du test"); });
            visualWindow.Content = visual; visualWindow.Show(); Dispatcher.UIThread.RunJobs();
            if (captures != 0) throw new Exception("Merely opening choices captured the game.");
            foreach (var choice in new[] { "Indice", "Solution" })
            {
                visual.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() == choice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(() => typeof(GameHelpPanel).GetField("_operation", flags)!.GetValue(visual) is null);
            }
            if (captures != 2 || visual.GetLogicalDescendants().OfType<TextBox>().Single(item => item.MaxLength == 2000).Text is { Length: > 0 })
                throw new Exception("Visual choices required writing or failed to request one capture each.");
            visualWindow.Close();
            Console.WriteLine("OK  Indice/Solution each capture on demand without writing; failures restore usable controls.");
            var minimalWindow = new Window { Width = 320, Height = 142, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
            var requests = 0;
            var minimal = new GameHelpPanel("Test visuel", modelDirectory, minimalWindow.Close, compact: true,
                capture: token => { requests++; return WaitForCancel(token); });
            minimalWindow.Content = minimal; minimalWindow.Show();
            minimal.GetLogicalDescendants().OfType<Button>().Single(item => item.Content?.ToString() == "Indice").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var status = (TextBlock)typeof(GameHelpPanel).GetField("_status", flags)!.GetValue(minimal)!;
            if (minimal.HeightForWidth(320) > 110 || status.Text != "Je réfléchis…" ||
                minimal.GetVisualDescendants().OfType<Button>().Any(item => item.IsEffectivelyVisible))
                throw new Exception($"Thinking did not reduce to a small text-only bubble: height={minimal.HeightForWidth(320)}, status={status.Text}, buttons={string.Join(',', minimal.GetLogicalDescendants().OfType<Button>().Where(item => item.IsEffectivelyVisible).Select(item => item.Content))}.");
            minimalWindow.Height = minimal.HeightForWidth(320); Snapshot("minimal-thinking.png");
            minimal.Cancel(); Pump(() => typeof(GameHelpPanel).GetField("_operation", flags)!.GetValue(minimal) is null);
            var text = (TextBlock)answerField.GetValue(minimal)!; status.IsVisible = false;
            text.Text = "Choisis une carte pour commencer.";
            var shortHeight = minimal.HeightForWidth(320);
            minimalWindow.Height = shortHeight; minimalWindow.UpdateLayout(); Snapshot("minimal-answer.png");
            minimalWindow.MouseDown(text.TranslatePoint(new Point(10, 10), minimalWindow)!.Value, Avalonia.Input.MouseButton.Left);
            minimalWindow.MouseUp(text.TranslatePoint(new Point(10, 10), minimalWindow)!.Value, Avalonia.Input.MouseButton.Left);
            var editor = (StackPanel)typeof(GameHelpPanel).GetField("_editor", flags)!.GetValue(minimal)!;
            if (!editor.IsVisible || minimal.HeightForWidth(320) <= shortHeight) throw new Exception("Clicking the answer did not open refinement.");
            minimalWindow.Height = minimal.HeightForWidth(320); Snapshot("minimal-refinement.png");
            ((TextBox)typeof(GameHelpPanel).GetField("_refinement", flags)!.GetValue(minimal)!).Text = "Je suis dans Booksquirm.";
            minimal.GetLogicalDescendants().OfType<Button>().Single(item => item.Content?.ToString() == "Préciser").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (requests != 2 || !((TextBox)typeof(GameHelpPanel).GetField("_question", flags)!.GetValue(minimal)!).Text!.Contains("Booksquirm"))
                throw new Exception("Refinement was not included in the next request.");
            minimal.Cancel(); Pump(() => typeof(GameHelpPanel).GetField("_operation", flags)!.GetValue(minimal) is null);
            status.IsVisible = false; text.Text = string.Join(" ", Enumerable.Repeat("Avance vers l’ouverture de la page.", 8));
            if (minimal.HeightForWidth(320) <= shortHeight || minimal.HeightForWidth(320) > 440) throw new Exception("Answer height did not follow text length.");
            minimalWindow.Close();
            Console.WriteLine("OK  minimal thinking and answer bubbles, text-driven height, click-to-refine and contextual retry.");
            void Snapshot(string name)
            {
                if (Environment.GetEnvironmentVariable("DECOMPSHELF_HELP_SNAPSHOTS") is not { } folder) return;
                Dispatcher.UIThread.RunJobs(); minimalWindow.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                using var shot = minimalWindow.CaptureRenderedFrame(); shot!.Save(Path.Combine(folder, name));
            }
            static async Task<GameFrame> WaitForCancel(CancellationToken token) { await Task.Delay(Timeout.Infinite, token); throw new Exception("Cancellation expected."); }
        }
        void Save(string name)
        {
            if (Environment.GetEnvironmentVariable("DECOMPSHELF_HELP_SNAPSHOTS") is not { } directory) return;
            Directory.CreateDirectory(directory); Dispatcher.UIThread.RunJobs(); panelWindow.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var frame = panelWindow.CaptureRenderedFrame(); frame!.Save(Path.Combine(directory, name));
        }
        void CheckChoicesFit()
        {
            Dispatcher.UIThread.RunJobs();
            var hint = controls.Single(button => button.Content?.ToString() == "Indice");
            var solution = controls.Single(button => button.Content?.ToString() == "Solution");
            var reduce = controls.Single(button => button.Content?.ToString() == "Réduire");
            var bottom = reduce.TranslatePoint(new Avalonia.Point(0, reduce.Bounds.Height), panelWindow)!.Value.Y;
            if (Math.Abs(hint.Bounds.Width - solution.Bounds.Width) > 1 || hint.Bounds.Height != solution.Bounds.Height ||
                hint.Bounds.Height < 44 || bottom > panelWindow.ClientSize.Height ||
                hint.HorizontalContentAlignment != Avalonia.Layout.HorizontalAlignment.Center ||
                solution.HorizontalContentAlignment != Avalonia.Layout.HorizontalAlignment.Center)
                throw new Exception("Companion choices are misaligned, clipped or too small.");
        }
        static void Pump(Func<bool> ready)
        {
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!ready() && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            if (!ready()) throw new Exception("Speech UI did not complete its asynchronous transition.");
        }
    }

    private sealed class VoiceFixture : IGameHelpVoice
    {
        public string? UnavailableReason => null;
        public int Listens, Reads, Stops;
        public bool Disposed;
        public TaskCompletionSource<string> Done = new();
        public Task<string> ListenAsync(CancellationToken cancellation)
        {
            Listens++; Done = new(); cancellation.Register(() => Done.TrySetCanceled(cancellation)); return Done.Task;
        }
        public Task SpeakAsync(string text, CancellationToken cancellation) { Reads++; return Task.CompletedTask; }
        public void Stop() { Stops++; }
        public void Dispose() { Disposed = true; Stop(); }
    }
}
