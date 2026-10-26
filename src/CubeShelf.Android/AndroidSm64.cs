using Android.Content;
using Android.OS;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CubeShelf.Mobile;
using OperationCanceledException = System.OperationCanceledException;

namespace CubeShelf.Android;

internal static class AndroidSm64
{
    internal const string Id = "n64-sm64";
    private const string GameActivity = "dev.net64.ghostship.MainActivity";
    private const string ExtractionService = "dev.net64.ghostship.ExtractionService";
    private static readonly SemaphoreSlim Preparation = new(1);
    private static Context Context => global::Android.App.Application.Context;
    private static EmbeddedSm64Files Files => new(Context.FilesDir!.AbsolutePath);
    internal static bool EngineAvailable => global::Android.OS.Build.SupportedAbis?.Contains("arm64-v8a") == true
        && File.Exists(Path.Combine(Context.ApplicationInfo!.NativeLibraryDir!, "libghostship.so"));
    internal static bool IsPrepared => EngineAvailable && Files.IsPrepared;

    internal static async Task<string> PrepareAsync(CancellationToken cancellation)
    {
        if (!EngineAvailable) throw new InvalidOperationException("Le moteur intégré Super Mario 64 nécessite l’APK ARM64 de DecompShelf.");
        if (!await Preparation.WaitAsync(0, cancellation)) throw new InvalidOperationException("Une préparation est déjà en cours.");
        var files = Files;
        try
        {
            files.RecoverInterruptedCommit();
            if (files.IsPrepared) return "Super Mario 64 est prêt à jouer dans DecompShelf.";
            var view = (Avalonia.Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView;
            var storage = view is null ? null : TopLevel.GetTopLevel(view)?.StorageProvider;
            if (storage?.CanOpen != true) throw new InvalidOperationException("Rouvre DecompShelf pour choisir ta ROM.");
            var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Ta copie de Super Mario 64 · ROM US ou japonaise .z64", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("ROM Nintendo 64 (.z64)") { Patterns = ["*.z64"], MimeTypes = ["application/octet-stream"] }]
            });
            cancellation.ThrowIfCancellationRequested();
            if (picked.Count == 0) throw new OperationCanceledException("Choix de la ROM annulé.");
            using (var document = picked[0])
            await using (var input = await document.OpenReadAsync())
                await files.ImportRomAsync(input, cancellation);
            await Task.Run(async () =>
            {
                await using var assets = Context.Assets!.Open("ghostship/gamedata.zip");
                await files.StageAssetsAsync(assets, cancellation);
                await using var controller = Context.Assets.Open("ghostship/gamecontrollerdb.txt");
                await using var output = File.Create(Path.Combine(files.Staging, "gamecontrollerdb.txt"));
                await controller.CopyToAsync(output, cancellation);
            }, cancellation);
            // Binder can deliver the final callback after cancellation; retain its Java-managed lifetime.
            var receiver = new ExtractionResult();
            using var intent = new Intent().SetClassName(Context.PackageName!, ExtractionService);
            intent.PutExtra("receiver", receiver);
            intent.PutExtra("attempt", Path.GetFileName(files.Staging));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            Context.StartForegroundService(intent);
            try
            {
                var error = await receiver.Completion.Task.WaitAsync(timeout.Token);
                cancellation.ThrowIfCancellationRequested();
                if (error is not null) throw new InvalidDataException(error);
                await Task.Run(files.CommitPrepared, cancellation);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new IOException("La préparation n’a pas terminé. Réessaie ; ta ROM et tes sauvegardes sont conservées."); }
            finally { Context.StopService(intent); }
            return "Super Mario 64 est prêt. Touche Jouer pour le lancer dans DecompShelf.";
        }
        finally
        {
            // A cancelled extractor may still be stopping; every attempt has its own directory.
            try { files.DiscardStaging(); } catch (IOException) { }
            Preparation.Release();
        }
    }

    internal static void Launch()
    {
        if (!IsPrepared) throw new InvalidOperationException("Prépare ta ROM Super Mario 64 dans DecompShelf avant de jouer.");
        var activity = NativeGameService.CurrentActivity ?? throw new InvalidOperationException("Rouvre DecompShelf puis réessaie.");
        using var intent = new Intent().SetClassName(activity.PackageName!, GameActivity);
        activity.StartActivity(intent);
    }

    private sealed class ExtractionResult() : ResultReceiver(new Handler(Looper.MainLooper!))
    {
        internal TaskCompletionSource<string?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void OnReceiveResult(int resultCode, Bundle? resultData)
        {
            Completion.TrySetResult(resultCode == 0 ? null : resultData?.GetString("error") ?? "L’extraction a échoué.");
        }
    }
}
