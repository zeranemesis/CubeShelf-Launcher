using Android.Media;

namespace CubeShelf.Android;

internal static class AndroidPreviewMusic
{
    private static MediaPlayer? _player;
    private static MediaPlayer? _effect;
    public static void PlayEffect(string? file)
    {
        _effect?.Release();_effect?.Dispose();_effect=null;
        if(file is null)return;
        var player=new MediaPlayer();
        try
        {
            player.SetDataSource(file);player.SetVolume(.25f,.25f);player.Prepare();
            player.Completion+=(_,_)=>{if(!ReferenceEquals(_effect,player))return;_effect=null;player.Release();player.Dispose();};
            _effect=player;player.Start();
        }
        catch{_effect=null;player.Release();player.Dispose();throw;}
    }
    public static void Play(string? file)
    {
        _player?.Release();_player?.Dispose();_player=null;
        if(file is null)return;
        var player=new MediaPlayer();
        try{player.SetDataSource(file);player.SetVolume(.2f,.2f);player.Looping=true;player.Prepare();player.Start();_player=player;}
        catch{player.Release();player.Dispose();throw;}
    }
}
