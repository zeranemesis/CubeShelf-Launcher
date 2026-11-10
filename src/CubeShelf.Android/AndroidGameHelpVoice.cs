using Android.App;
using Android.Content;
using Android.OS;
using Android.Speech;
using Android.Speech.Tts;
using CubeShelf.Assistance;
using OperationCanceledException = System.OperationCanceledException;

namespace CubeShelf.Android;

internal sealed class AndroidGameHelpVoice(Context context, Func<CancellationToken, Task<bool>> permission, Action? listeningEnded = null) : IGameHelpVoice
{
    private SpeechRecognizer? _recognizer;
    private TextToSpeech? _speaker;
    private readonly Handler _ui = new(Looper.MainLooper!);
    public string? UnavailableReason => Availability(context);
    internal static string? Availability(Context context) => !OperatingSystem.IsAndroidVersionAtLeast(31)
        ? "Le micro local nécessite Android 12 ou plus récent. Tu peux écrire ta question."
        : !SpeechRecognizer.IsOnDeviceRecognitionAvailable(context)
            ? "Aucune reconnaissance vocale locale sur cet appareil. Le mode écrit reste disponible." : null;

    public async Task<string> ListenAsync(CancellationToken cancellation)
    {
        if (UnavailableReason is { } reason) throw new InvalidOperationException(reason);
        if (!await permission(cancellation)) throw new InvalidOperationException("Micro non autorisé. Tu peux continuer à l’écrit.");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsAndroidVersionAtLeast(31)) throw new InvalidOperationException(UnavailableReason);
            var recognizer = SpeechRecognizer.CreateOnDeviceSpeechRecognizer(context);
            _recognizer = recognizer;
            using var listener = new Recognition();
            recognizer.SetRecognitionListener(listener);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var registration = timeout.Token.Register(() => { _ui.Post(() => _recognizer?.Cancel()); listener.Done.TrySetCanceled(timeout.Token); });
            using var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraLanguage, "fr-FR");
            intent.PutExtra(RecognizerIntent.ExtraPreferOffline, true);
            cancellation.ThrowIfCancellationRequested();
            recognizer.StartListening(intent);
            var text = await listener.Done.Task;
            return text[..Math.Min(2000, text.Length)];
        }
        finally { _recognizer?.Destroy(); _recognizer?.Dispose(); _recognizer = null; listeningEnded?.Invoke(); }
    }

    public async Task SpeakAsync(string text, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var initialized = new Initialization();
        using var speaker = new TextToSpeech(context, initialized);
        _speaker = speaker;
        using var listener = new Utterance();
        try
        {
            await initialized.Done.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            var voice = speaker.Voices?.FirstOrDefault(item => !item.IsNetworkConnectionRequired && item.Locale?.Language == "fr" && item.Features?.Contains("notInstalled") != true);
            if (voice is null) throw new InvalidOperationException("Installe une voix française hors ligne dans Android pour lire les réponses. Le texte reste disponible.");
            speaker.SetVoice(voice); speaker.SetSpeechRate(1); speaker.SetPitch(1.05f);
            speaker.SetOnUtteranceProgressListener(listener);
            using var registration = cancellation.Register(() => { _ui.Post(() => _speaker?.Stop()); listener.Done.TrySetCanceled(cancellation); });
            if (speaker.Speak(text.Replace("*", "").Replace("#", "").Replace("`", ""), QueueMode.Flush, null, Guid.NewGuid().ToString("N")) != OperationResult.Success)
                throw new InvalidOperationException("La lecture vocale n’a pas démarré. La réponse reste à l’écran.");
            await listener.Done.Task;
        }
        finally { speaker.Stop(); speaker.Shutdown(); _speaker = null; }
    }
    public void Stop() { _recognizer?.Cancel(); _speaker?.Stop(); }
    public void Dispose() => Stop();

    private sealed class Initialization : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        internal TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnInit(OperationResult status) { if (status == OperationResult.Success) Done.TrySetResult(); else Done.TrySetException(new InvalidOperationException("Voix locale indisponible.")); }
    }
    private sealed class Utterance : UtteranceProgressListener
    {
        internal TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void OnStart(string? id) { }
        public override void OnDone(string? id) => Done.TrySetResult();
        [Obsolete("Android retains this abstract callback for older speech engines.")]
        public override void OnError(string? id) => Done.TrySetException(new InvalidOperationException("Lecture vocale interrompue."));
        public override void OnError(string? id, TextToSpeechError error) => Done.TrySetException(new InvalidOperationException("Lecture vocale interrompue."));
        public override void OnStop(string? id, bool interrupted) => Done.TrySetCanceled();
    }
    private sealed class Recognition : Java.Lang.Object, IRecognitionListener
    {
        internal TaskCompletionSource<string> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnResults(Bundle? results)
        {
            var text = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(text)) Done.TrySetException(new InvalidOperationException("Je n’ai pas compris. Réessaie ou écris ta question."));
            else Done.TrySetResult(text);
        }
        public void OnError(SpeechRecognizerError error) => Done.TrySetException(new InvalidOperationException("L’écoute locale a échoué. Vérifie la langue installée, ou écris ta question."));
        public void OnBeginningOfSpeech() { }
        public void OnBufferReceived(byte[]? buffer) { }
        public void OnEndOfSpeech() { }
        public void OnEvent(int type, Bundle? data) { }
        public void OnPartialResults(Bundle? results) { }
        public void OnReadyForSpeech(Bundle? parameters) { }
        public void OnRmsChanged(float rms) { }
        public void OnEndOfSegmentedSession() { }
        public void OnSegmentResults(Bundle? results) { }
    }
}
