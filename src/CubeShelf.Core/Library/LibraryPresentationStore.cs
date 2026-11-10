using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CubeShelf.Core.Library;

public sealed class LibraryPresentationStore
{
    // Style null = never chosen: a dual-screen device may then adopt HOME automatically.
    private sealed record Settings(string? Style=null,bool MusicEnabled=true,Dictionary<string,string>? Music=null,bool SoundsEnabled=true,bool LightHome=true,bool SecondaryDisplay=true,bool SwapFaceButtons=false,string? LastPlayed=null);
    private readonly object _gate=new();
    private readonly string _file,_musicRoot;
    private Settings _settings;
    public bool IsDual=>_settings.Style=="dual";
    public bool MusicEnabled=>_settings.MusicEnabled;
    public bool SoundsEnabled=>_settings.SoundsEnabled;
    public bool LightHome=>_settings.LightHome;
    public bool SecondaryDisplay=>_settings.SecondaryDisplay;
    public bool HasStyleChoice=>_settings.Style is not null;
    /// <summary>Nintendo layout: the right face button confirms (A/B and X/Y swapped).</summary>
    public bool SwapFaceButtons=>_settings.SwapFaceButtons;
    public string? LastPlayed=>_settings.LastPlayed;
    public void SetSwapFaceButtons(bool swap){lock(_gate){_settings=_settings with{SwapFaceButtons=swap};Save();}}
    public void SetLastPlayed(string id){lock(_gate){if(_settings.LastPlayed==id)return;_settings=_settings with{LastPlayed=id};Save();}}
    public void SetSecondaryDisplay(bool enabled){lock(_gate){_settings=_settings with{SecondaryDisplay=enabled};Save();}}
    public string BundledAudioDirectory=>Path.Combine(_musicRoot,"home-v1");
    public LibraryPresentationStore(string directory)
    {
        _file=Path.Combine(directory,"library-presentation.json");_musicRoot=Path.Combine(directory,"preview-music");
        try{_settings=File.Exists(_file)&&new FileInfo(_file).Length<262144?JsonSerializer.Deserialize<Settings>(File.ReadAllText(_file))??new():new();}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){_settings=new();}
    }
    public void SetStyle(bool dual){lock(_gate){_settings=_settings with{Style=dual?"dual":"classic"};Save();}}
    public void ToggleMusic(){lock(_gate){_settings=_settings with{MusicEnabled=!MusicEnabled};Save();}}
    public void ToggleSounds(){lock(_gate){_settings=_settings with{SoundsEnabled=!SoundsEnabled};Save();}}
    public void ToggleHomeAppearance(){lock(_gate){_settings=_settings with{LightHome=!LightHome};Save();}}
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);File.WriteAllText(_file+".tmp",JsonSerializer.Serialize(_settings));File.Move(_file+".tmp",_file,true);
    }
    private static string Key(string id)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    public string? Track(string id)
    {
        var extension=_settings.Music?.GetValueOrDefault(id);
        if(extension is not (".mp3" or ".wav"))return null;
        var file=Path.Combine(_musicRoot,Key(id)+extension);return File.Exists(file)?file:null;
    }
    public async Task ImportMusicAsync(string id,string extension,Stream source,CancellationToken cancellation=default)
    {
        extension=extension.ToLowerInvariant();if(extension is not (".mp3" or ".wav"))throw new InvalidDataException("Choisis une piste MP3 ou WAV.");
        Directory.CreateDirectory(_musicRoot);var target=Path.Combine(_musicRoot,Key(id)+extension);var temporary=target+".tmp";
        try
        {
            using(var output=File.Create(temporary))
            {
                var buffer=new byte[65536];long total=0;int read;
                while((read=await source.ReadAsync(buffer,cancellation).ConfigureAwait(false))>0)
                {total+=read;if(total>32*1024*1024)throw new InvalidDataException("La piste doit faire au maximum 32 Mo.");await output.WriteAsync(buffer.AsMemory(0,read),cancellation).ConfigureAwait(false);}
            }
            var header=new byte[12];using(var input=File.OpenRead(temporary))if(input.Read(header)<12)throw new InvalidDataException("Piste audio incomplète.");
            var valid=extension==".wav"?header.AsSpan(0,4).SequenceEqual("RIFF"u8)&&header.AsSpan(8,4).SequenceEqual("WAVE"u8)
                :header.AsSpan(0,3).SequenceEqual("ID3"u8)||header[0]==255&&(header[1]&224)==224;
            if(!valid)throw new InvalidDataException("Le fichier ne correspond pas au format audio annoncé.");
            cancellation.ThrowIfCancellationRequested();File.Move(temporary,target,true);
            lock(_gate){var music=new Dictionary<string,string>(_settings.Music??[]){[id]=extension};_settings=_settings with{Music=music};Save();}
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
