using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace CubeShelf.Mobile;

/// <summary>Resumable, pinned APK transfer; Android alone performs application installation.</summary>
public sealed class NativeApkDownload : IRuntimeDownloads
{
    private readonly MobileNativePort _port;
    private readonly string _partial,_marker;
    private readonly HttpClient _http;
    private readonly object _gate=new();
    private RuntimeDownloadSnapshot _snapshot;
    private CancellationTokenSource? _cancellation;
    private bool _running,_discard;
    public string ApkFile { get; }
    public RuntimeDownloadSnapshot Snapshot { get { lock(_gate)return _snapshot; } }
    public event Action<RuntimeDownloadSnapshot>? Changed;
    public bool IsReady
    {
        get
        {
            if(Snapshot.State!=RuntimeDownloadState.Ready)return false;
            if(File.Exists(ApkFile)&&new FileInfo(ApkFile).Length==_port.ApkSize)return true;
            Publish(new(RuntimeDownloadState.Failed,0,_port.Size,Error:"Le paquet n’est plus disponible. Relance le téléchargement."));return false;
        }
    }

    public async Task<string> VerifiedApkAsync()
    {
        if(IsReady && await MatchesAsync(ApkFile,_port.ApkSha256).ConfigureAwait(false))return ApkFile;
        File.Delete(ApkFile);File.Delete(_marker);
        Publish(new(RuntimeDownloadState.Failed,0,_port.Size,Error:"L’APK a changé. Relance son téléchargement."));
        throw new InvalidDataException("L’APK a changé depuis sa vérification. Installation refusée.");
    }

    public NativeApkDownload(MobileNativePort port,string directory,bool compatible,HttpClient? http=null)
    {
        port.Validate();_port=port;
        directory=Path.Combine(directory,"native-port-packages",port.Id,port.Sha256[..12]);
        Directory.CreateDirectory(directory);
        _partial=Path.Combine(directory,"download.part");ApkFile=Path.Combine(directory,"runtime.apk");_marker=Path.Combine(directory,"verified.txt");
        _http=http??new HttpClient {Timeout=TimeSpan.FromMinutes(30)};
        var ready=File.Exists(ApkFile)&&new FileInfo(ApkFile).Length==port.ApkSize&&File.Exists(_marker)&&File.ReadAllText(_marker)==port.ApkSha256;
        _snapshot=new(!compatible?RuntimeDownloadState.Unavailable:ready?RuntimeDownloadState.Ready:File.Exists(_partial)?RuntimeDownloadState.Paused:RuntimeDownloadState.NotInstalled,
            ready?port.Size:PartialLength(),port.Size);
    }

    public void Start()
    {
        lock(_gate)
        {
            if(_running||_snapshot.State is RuntimeDownloadState.Unavailable or RuntimeDownloadState.Ready)return;
            _running=true;_discard=false;_cancellation=new();
        }
        Publish(new(RuntimeDownloadState.Downloading,PartialLength(),_port.Size));
        _=RunAsync(_cancellation.Token);
    }
    public void Pause() { lock(_gate)_cancellation?.Cancel(); }
    public void Cancel()
    {
        lock(_gate) { _discard=true;_cancellation?.Cancel();if(_running)return; }
        File.Delete(_partial);Publish(new(RuntimeDownloadState.NotInstalled,0,_port.Size));
    }
    private long PartialLength()=>File.Exists(_partial)?new FileInfo(_partial).Length:0;
    private void Publish(RuntimeDownloadSnapshot snapshot) { lock(_gate)_snapshot=snapshot;Changed?.Invoke(snapshot); }

    private async Task RunAsync(CancellationToken token)
    {
        RuntimeDownloadSnapshot result;
        var temporary=ApkFile+".tmp";
        try
        {
            var offset=PartialLength();if(offset>_port.Size) {File.Delete(_partial);offset=0;}
            if(offset<_port.Size)
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,_port.Url);
                request.Headers.UserAgent.ParseAdd("DecompShelf-Android/1.0");
                if(offset>0)request.Headers.Range=new RangeHeaderValue(offset,null);
                using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException("Redirection non HTTPS refusée.");
                if(response.StatusCode==HttpStatusCode.PartialContent)
                {
                    var range=response.Content.Headers.ContentRange;
                    if(range?.From!=offset||range.Length!=_port.Size)throw new InvalidDataException("Reprise du paquet incohérente.");
                }
                else offset=0;
                if(response.Content.Headers.ContentLength is { } size && size!=_port.Size-offset)throw new InvalidDataException("Taille du paquet différente de la release.");
                await using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var output=new FileStream(_partial,offset>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,131072,true);
                var received=offset;var previous=offset;var timer=Stopwatch.StartNew();var buffer=new byte[131072];int read;
                while((read=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0)
                {
                    received+=read;if(received>_port.Size)throw new InvalidDataException("Paquet trop volumineux.");
                    await output.WriteAsync(buffer.AsMemory(0,read),token).ConfigureAwait(false);
                    if(timer.ElapsedMilliseconds<180)continue;
                    Publish(new(RuntimeDownloadState.Downloading,received,_port.Size,(received-previous)/timer.Elapsed.TotalSeconds));
                    previous=received;timer.Restart();
                }
            }
            Publish(new(RuntimeDownloadState.Verifying,_port.Size,_port.Size));
            if(PartialLength()!=_port.Size || !await MatchesAsync(_partial,_port.Sha256,token).ConfigureAwait(false))
            {File.Delete(_partial);throw new InvalidDataException("Empreinte du paquet incorrecte ; téléchargement refusé.");}
            if(_port.Name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))
            {
                using var zip=ZipFile.OpenRead(_partial);
                var apks=zip.Entries.Where(e=>e.FullName.EndsWith(".apk",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(apks.Length!=1 || apks[0].Length!=_port.ApkSize)throw new InvalidDataException("Archive Android inattendue.");
                await using(var input=apks[0].Open())
                await using(var output=File.Create(temporary))
                {
                    var buffer=new byte[131072];long total=0;int read;
                    while((read=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0)
                    {total+=read;if(total>_port.ApkSize)throw new InvalidDataException("APK trop volumineux.");await output.WriteAsync(buffer.AsMemory(0,read),token).ConfigureAwait(false);}
                }
            }
            else File.Copy(_partial,temporary,true);
            if(new FileInfo(temporary).Length!=_port.ApkSize || !await MatchesAsync(temporary,_port.ApkSha256,token).ConfigureAwait(false))
                throw new InvalidDataException("L’APK extrait ne correspond pas au paquet vérifié.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary,ApkFile,true);File.WriteAllText(_marker,_port.ApkSha256);File.Delete(_partial);
            result=new(RuntimeDownloadState.Ready,_port.Size,_port.Size);
        }
        catch(OperationCanceledException) { result=new(_discard?RuntimeDownloadState.NotInstalled:RuntimeDownloadState.Paused,_discard?0:PartialLength(),_port.Size); }
        catch(Exception error) {result=new(RuntimeDownloadState.Failed,PartialLength(),_port.Size,Error:error.Message);}
        finally
        {
            File.Delete(temporary);if(_discard)File.Delete(_partial);
            lock(_gate) {_running=false;_cancellation?.Dispose();_cancellation=null;}
        }
        Publish(result);
    }

    public static async Task<bool> MatchesAsync(string file,string expected,CancellationToken token=default)
    {
        await using var stream=File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,token).ConfigureAwait(false)).Equals(expected,StringComparison.OrdinalIgnoreCase);
    }
}
