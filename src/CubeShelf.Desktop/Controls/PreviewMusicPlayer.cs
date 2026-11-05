using System.Runtime.InteropServices;

namespace CubeShelf.Desktop.Controls;

internal sealed class PreviewMusicPlayer : IDisposable
{
    private const string Alias="decompshelf_preview";
    public void Play(string? file)
    {
        if(!OperatingSystem.IsWindows())return;
        _=Send("close "+Alias);
        if(file is null)return;
        if(file.Contains('"')||!File.Exists(file))throw new IOException("Piste locale indisponible.");
        if(Send("open \""+file+"\" type mpegvideo alias "+Alias)!=0)throw new IOException("Format audio non pris en charge.");
        _=Send("setaudio "+Alias+" volume to 200");
        if(Send("play "+Alias+" repeat")!=0){_=Send("close "+Alias);throw new IOException("Lecture audio indisponible.");}
    }
    public void Dispose()=>Play(null);
    private static uint Send(string command)=>mciSendStringW(command,0,0,0);
    [DllImport("winmm.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint mciSendStringW(string command,nint result,int resultLength,nint callback);
}
