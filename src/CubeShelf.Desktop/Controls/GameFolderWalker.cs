using Avalonia.Platform.Storage;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop.Controls;

/// <summary>Walks a folder picked with the storage provider (local folder on PC, SAF tree on Android).</summary>
public static class GameFolderWalker
{
    // shortcut: depth 6 / 20 000 entries bound a scan of a whole drive; raise if players keep deeper ROM trees.
    public static async IAsyncEnumerable<GameFileMatcher.ScannedFile> Files(IStorageFolder root,int maxDepth=6,int maxEntries=20000,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation=default)
    {
        var pending=new Stack<(IStorageFolder Folder,int Depth)>();pending.Push((root,0));var entries=0;
        while(pending.Count>0&&entries<maxEntries)
        {
            var (folder,depth)=pending.Pop();
            var items=new List<IStorageItem>();
            try{await foreach(var item in folder.GetItemsAsync().WithCancellation(cancellation))items.Add(item);}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException){continue;}
            foreach(var item in items)
            {
                if(++entries>maxEntries)yield break;
                if(item is IStorageFolder child){if(depth<maxDepth)pending.Push((child,depth+1));}
                else if(item is IStorageFile file&&GameFileMatcher.IsCandidate(file.Name))
                    yield return new(file.Name,file.TryGetLocalPath()??file.Path.ToString(),file.OpenReadAsync);
            }
        }
    }
}
