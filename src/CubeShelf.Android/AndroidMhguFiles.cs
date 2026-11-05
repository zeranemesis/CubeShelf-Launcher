using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace CubeShelf.Android;

internal static class AndroidMhguFiles
{
    internal const string Id="switch-mhgu";
    private static string Root=>Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath,"native-games",Id);
    internal static bool IsPrepared=>File.Exists(Path.Combine(Root,"data","exefs","main"))&&Directory.Exists(Path.Combine(Root,"data","romfs"));

    internal static async Task ImportAsync(CancellationToken cancellation)
    {
        if(IsPrepared)return;
        var view=(Avalonia.Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView;
        var storage=view is null?null:TopLevel.GetTopLevel(view)?.StorageProvider;
        if(storage?.CanPickFolder!=true)throw new InvalidOperationException("Rouvre DecompShelf pour choisir les fichiers du jeu.");
        var picked=await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title="Ta copie de MHGU · dossier contenant exefs et romfs",AllowMultiple=false
        });
        cancellation.ThrowIfCancellationRequested();
        if(picked.Count==0)throw new OperationCanceledException("Choix des fichiers annulé.");
        using var selected=picked[0];
        var roots=new Dictionary<string,IStorageFolder>(StringComparer.Ordinal);
        await foreach(var item in selected.GetItemsAsync().WithCancellation(cancellation))
        {
            if(item is IStorageFolder folder&&item.Name is "exefs" or "romfs")roots.Add(item.Name,folder);
            else item.Dispose();
        }
        try
        {
            if(roots.Count!=2)throw new InvalidDataException("Choisis le dossier qui contient directement les dossiers exefs et romfs de ta propre copie de MHGU.");
            Directory.CreateDirectory(Root);
            var stage=Path.Combine(Root,"import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try
            {
                foreach(var (name,folder) in roots)await Task.Run(()=>CopyFolder(folder,Path.Combine(stage,name),cancellation),cancellation);
                if(!File.Exists(Path.Combine(stage,"exefs","main")))throw new InvalidDataException("Le fichier exefs/main du jeu est absent.");
                cancellation.ThrowIfCancellationRequested();
                var target=Path.Combine(Root,"data");
                if(Directory.Exists(target))Directory.Move(target,Path.Combine(Root,"data.previous-"+Guid.NewGuid().ToString("N")));
                Directory.Move(stage,target);
            }
            finally
            {
                if(Directory.Exists(stage)&&Path.GetFullPath(stage).StartsWith(Path.GetFullPath(Root)+Path.DirectorySeparatorChar,StringComparison.Ordinal))await Task.Run(()=>Directory.Delete(stage,true));
            }
        }
        finally {foreach(var folder in roots.Values)folder.Dispose();}
    }
    private static async Task CopyFolder(IStorageFolder source,string target,CancellationToken cancellation,int depth=0)
    {
        if(depth>40)throw new InvalidDataException("Arborescence des fichiers du jeu trop profonde.");
        Directory.CreateDirectory(target);
        await foreach(var item in source.GetItemsAsync().WithCancellation(cancellation))
        using(item)
        {
            var name=item.Name;
            if(string.IsNullOrWhiteSpace(name)||name is "." or ".."||name.IndexOfAny(['/', '\\', ':', '\0'])>=0)throw new InvalidDataException("Nom de fichier du jeu invalide.");
            var destination=Path.Combine(target,name);
            if(item is IStorageFolder folder)await CopyFolder(folder,destination,cancellation,depth+1);
            else if(item is IStorageFile file)
            {
                var properties=await file.GetBasicPropertiesAsync();
                CubeShelf.Core.Storage.DiskSpaceGuard.Require(destination,Convert.ToInt64(properties.Size??0));
                await using var input=await file.OpenReadAsync();
                await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true);
                await input.CopyToAsync(output,cancellation);
            }
        }
    }
}
