using System.Runtime.Versioning;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using CubeShelf.Assistance;

namespace CubeShelf.Desktop.Controls;

[SupportedOSPlatform("windows")]
internal sealed class WindowsGameHelpVoice : IGameHelpVoice
{
    private SpeechRecognitionEngine? _recognizer;
    private SpeechSynthesizer? _speaker;
    public string? UnavailableReason
    {
        get
        {
            try { return FrenchRecognizer() is null ? "Installe la reconnaissance vocale française dans Windows pour parler hors ligne. L’écrit reste disponible." : null; }
            catch (Exception) { return "La reconnaissance vocale locale de Windows n’est pas disponible. Utilise le mode écrit."; }
        }
    }
    private static RecognizerInfo? FrenchRecognizer() => SpeechRecognitionEngine.InstalledRecognizers()
        .FirstOrDefault(info => info.Culture.TwoLetterISOLanguageName == "fr");

    public async Task<string> ListenAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Stop();
        var info = FrenchRecognizer() ?? throw new InvalidOperationException(UnavailableReason);
        using var recognizer = new SpeechRecognitionEngine(info);
        _recognizer = recognizer;
        try
        {
            recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(6);
            recognizer.BabbleTimeout = TimeSpan.FromSeconds(12);
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SetInputToDefaultAudioDevice();
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            recognizer.RecognizeCompleted += (_, args) =>
            {
                if (args.Error is { } error) done.TrySetException(error);
                else if (args.Cancelled) done.TrySetCanceled();
                else done.TrySetResult(args.Result?.Text ?? "");
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var registration = timeout.Token.Register(() => { recognizer.RecognizeAsyncCancel(); done.TrySetCanceled(timeout.Token); });
            recognizer.RecognizeAsync(RecognizeMode.Single);
            var text = await done.Task;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Je n’ai pas compris. Réessaie, ou écris ta question.");
            return text[..Math.Min(2000, text.Length)];
        }
        finally { _recognizer = null; }
    }

    public async Task SpeakAsync(string text, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var speaker = new SpeechSynthesizer();
        _speaker = speaker;
        try
        {
            var voice = speaker.GetInstalledVoices().FirstOrDefault(item => item.Enabled && item.VoiceInfo.Culture.TwoLetterISOLanguageName == "fr");
            if (voice is null) throw new InvalidOperationException("Aucune voix française locale installée. La réponse reste lisible à l’écran.");
            speaker.SelectVoice(voice.VoiceInfo.Name);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            speaker.SpeakCompleted += (_, args) =>
            {
                if (args.Error is { } error) done.TrySetException(error);
                else if (args.Cancelled) done.TrySetCanceled();
                else done.TrySetResult();
            };
            using var registration = cancellation.Register(() => { speaker.SpeakAsyncCancelAll(); done.TrySetCanceled(cancellation); });
            speaker.SpeakAsync(text.Replace("*", "").Replace("#", "").Replace("`", ""));
            await done.Task;
        }
        finally { _speaker = null; }
    }
    public void Stop() { _recognizer?.RecognizeAsyncCancel(); _speaker?.SpeakAsyncCancelAll(); }
    public void Dispose() => Stop();
}
