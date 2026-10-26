using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CubeShelf.Mobile;

var root=Path.Combine(Path.GetTempPath(),"decompshelf-mobile-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
using var json=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"android-ports.json"));
var ports=MobileNativePort.Load(json);
Check(ports.Length==5&&ports.Select(p=>p.PackageName).Distinct().Count()==5,"five distinct Android ports and package identities");
var payload="APK fixture, never executed"u8.ToArray();
MobileNativePort Fixture(byte[] bytes,string name="test.apk")=>new()
{
    Id="fixture",Repository="owner/project",PackageName="org.example.nativeport",VersionCode=1,MinimumSdk=24,Abis=["arm64-v8a"],Name=name,
    Url="https://github.com/owner/project/releases/download/v1/"+name,Size=bytes.Length,Sha256=Hash(bytes),ApkSize=payload.Length,ApkSha256=Hash(payload)
};
using(var http=new HttpClient(new FakeHttp(payload)))
{
    var engine=new NativeApkDownload(Fixture(payload),root,true,http);engine.Start();await Finished(engine);
    Check(engine.IsReady&&File.ReadAllBytes(await engine.VerifiedApkAsync()).SequenceEqual(payload),"verified direct APK download without installation");
    File.WriteAllText(engine.ApkFile,"modified");
    try {await engine.VerifiedApkAsync();throw new Exception("Modified APK accepted");}catch(InvalidDataException){}
    Check(engine.Snapshot.State==RuntimeDownloadState.Failed,"modified cached APK is rejected and can be downloaded again");
    engine.Start();await Finished(engine);Check(engine.IsReady,"retry after verification failure");
}
var bad=Fixture(payload);bad.Sha256=new string('0',64);
using(var http=new HttpClient(new FakeHttp(payload)))
{
    var engine=new NativeApkDownload(bad,root,true,http);engine.Start();await Finished(engine);
    Check(engine.Snapshot.State==RuntimeDownloadState.Failed&&!File.Exists(engine.ApkFile),"wrong release hash never produces a ready APK");
}
byte[] Archive(params string[] names)
{
    using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        foreach(var name in names){using var output=zip.CreateEntry(name).Open();output.Write(payload);}
    return stream.ToArray();
}
var archive=Archive("../../escape.apk");
using(var http=new HttpClient(new FakeHttp(archive)))
{
    var engine=new NativeApkDownload(Fixture(archive,"test.zip"),root,true,http);engine.Start();await Finished(engine);
    Check(engine.IsReady&&File.Exists(await engine.VerifiedApkAsync())&&!File.Exists(Path.Combine(root,"escape.apk")),"ZIP APK is written only to the fixed private destination");
}
archive=Archive("one.apk","two.apk");
using(var http=new HttpClient(new FakeHttp(archive)))
{
    var engine=new NativeApkDownload(Fixture(archive,"test.zip"),root,true,http);engine.Start();await Finished(engine);
    Check(engine.Snapshot.State==RuntimeDownloadState.Failed,"ambiguous APK archive rejected");
}
var resumePort=Fixture(payload);resumePort.Id="resume";
var dir=Path.Combine(root,"native-port-packages",resumePort.Id,resumePort.Sha256[..12]);Directory.CreateDirectory(dir);File.WriteAllBytes(Path.Combine(dir,"download.part"),payload[..5]);
using(var http=new HttpClient(new FakeHttp(payload)))
{
    var engine=new NativeApkDownload(resumePort,root,true,http);engine.Start();await Finished(engine);Check(engine.IsReady,"partial APK resumes through a checked HTTP range");
}
var wrong=Fixture(payload);wrong.Url="https://example.com/test.apk";
try {wrong.Validate();throw new Exception("Foreign URL accepted");}catch(InvalidDataException){}
Check(new NativeApkDownload(Fixture(payload),root,false).Snapshot.State==RuntimeDownloadState.Unavailable,"unsupported devices cannot enqueue the port");
using(var http=new HttpClient(new SlowHttp()))
{
    var port=Fixture(payload);port.Id="cancel";
    var engine=new NativeApkDownload(port,root,true,http);engine.Start();engine.Pause();await Finished(engine);
    Check(engine.Snapshot.State==RuntimeDownloadState.Paused,"pausing a native APK transfer preserves a resumable state");
    engine.Cancel();Check(engine.Snapshot.State==RuntimeDownloadState.NotInstalled&&!File.Exists(engine.ApkFile),"cancel removes the pending APK without invoking an installer");
}

RunUi();
Console.WriteLine("Mobile Android catalogue, download integrity, install handoff, cancellation and layout checks passed.");
void RunUi()
{
    AppBuilder.Configure<MobileApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions {UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
    var host=new FakeGames(root,ports);var view=new MobileLibraryView(host);var window=new Window {Width=390,Height=844,Content=view};window.Show();Jobs();
    Field<Button>("_appUpdatesButton").IsVisible=true;
    var picker=Field<ComboBox>("_consolePicker");picker.SelectedIndex=1;Jobs();
    Check(Field<TextBlock>("_position").Text=="1 / 5","Nintendo 64 selector exposes five games in the same mobile shell");
    Check(!Field<Button>("_play").IsVisible&&Field<Button>("_download").IsVisible,"uninstalled native game offers download, not Play");
    Field<Button>("_download").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
    Check(host.Queue.Entries.Single(e=>e.Id==ports[0].Id).Transfer.State==RuntimeDownloadState.Downloading,"native action enqueues the correct game");
    host.Engines[ports[0].Id].Complete();Jobs();
    Check(Field<TextBlock>("_transferState").Text=="APK prêt à installer","download completion is not labelled as an installed game");
    Field<Button>("_transferStart").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
    Check(host.Native.Prepared.Contains(ports[0].Id)&&host.Native.Launched.Count==0,"ready APK hands off to Android installation without launching a missing app");
    host.Native.Installed.Add(ports[0].Id);view.RefreshAvailability();Check(Field<Button>("_play").IsVisible,"return from Android installer enables Play");
    Dispatcher.UIThread.Post(()=>Field<Button>("_play").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));Jobs();
    view.BackFromController();Jobs();Check(host.Native.Launched.Count==0,"B cancels the opening animation before native app launch");
    Dispatcher.UIThread.Post(()=>Field<Button>("_play").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));Jobs();
    var opening=Field<CubeShelf.Desktop.Controls.GameLaunchTransition>("_launchTransition");
    opening.GetType().GetMethod("Advance",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(opening,[2.2]);
    for(var i=0;i<100&&host.Native.Launched.Count==0;i++){Jobs();Thread.Sleep(5);}
    Check(host.Native.Launched.SequenceEqual(new[]{ports[0].Id}),"Play launches the selected native application after opening completes");
    Check(!Field<StackPanel>("_onlinePanel").IsVisible,"PartyBoard online controls are hidden for native Nintendo 64 apps");
    host.Native.Blocked.Add(ports[0].Id);host.Native.Installed.Clear();view.RefreshAvailability();
    Check(!Field<Button>("_download").IsVisible,"incompatible port download is hidden");host.Native.Blocked.Clear();view.RefreshAvailability();
    foreach(var size in new[]{new Size(390,844),new Size(844,390)})
    {
        window.Width=size.Width;window.Height=size.Height;Jobs();
        var play=Field<Button>("_download");var origin=play.TranslatePoint(default,view)!.Value;
        Check(origin.Y>=0&&origin.Y+play.Bounds.Height<=view.Bounds.Height+1,"primary action fits in portrait and landscape");
        if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } folder)
        {Directory.CreateDirectory(folder);using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(folder,$"mobile-n64-{size.Width}.png"));}
    }
    view.SetActive(false);view.SetActive(true);Jobs();Check(picker.SelectedIndex==1,"selected Nintendo 64 catalogue survives pause/resume");
    picker.SelectedIndex=0;Jobs();Check(Field<Button>("_play").IsVisible&&Field<StackPanel>("_onlinePanel").IsVisible,"PartyBoard launch and online actions remain available");
    window.Close();
    T Field<T>(string name)=>(T)typeof(MobileLibraryView).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
    void Jobs(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);Dispatcher.UIThread.RunJobs();}
}
static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static void Check(bool good,string text){if(!good)throw new Exception(text);Console.WriteLine("OK "+text);}
static async Task Finished(NativeApkDownload engine)
{
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
    while(engine.Snapshot.IsBusy)await Task.Delay(10,timeout.Token);
}
sealed class FakeHttp(byte[] payload):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        var offset=request.Headers.Range?.Ranges.First().From??0;
        var response=new HttpResponseMessage(offset>0?HttpStatusCode.PartialContent:HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(payload[(int)offset..])};
        if(offset>0)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(offset,payload.Length-1,payload.Length);
        return Task.FromResult(response);
    }
}
sealed class SlowHttp:HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {await Task.Delay(Timeout.Infinite,token);throw new InvalidOperationException();}
}
sealed class FakeTransfer:IRuntimeDownloads
{
    public RuntimeDownloadSnapshot Snapshot {get;private set;}=new(RuntimeDownloadState.NotInstalled,0,100);
    public event Action<RuntimeDownloadSnapshot>? Changed;
    public void Start(){Snapshot=new(RuntimeDownloadState.Downloading,0,100);Changed?.Invoke(Snapshot);}
    public void Complete(){Snapshot=new(RuntimeDownloadState.Ready,100,100);Changed?.Invoke(Snapshot);}
    public void Pause(){Snapshot=Snapshot with {State=RuntimeDownloadState.Paused};Changed?.Invoke(Snapshot);}
    public void Cancel(){Snapshot=new(RuntimeDownloadState.NotInstalled,0,100);Changed?.Invoke(Snapshot);}
}
sealed class FakeGames:IMobileGameService
{
    public FakeGames(string root,MobileNativePort[] ports)
    {
        DataDirectory=Path.Combine(root,"ui");Native=new(ports);Engines=ports.ToDictionary(p=>p.Id,_=>new FakeTransfer());
        Queue=new(DataDirectory,new[]{new RuntimeDownloadSource("partyboard","GMPE01_00","PartyBoard","v1",new FakeTransfer())}.Concat(ports.Select(p=>new RuntimeDownloadSource(p.Id,p.Id,p.Title,p.Version,Engines[p.Id]))).ToArray());
        Native.Queue=Queue;Native.Engines=Engines;
    }
    public string DataDirectory {get;}
    public Dictionary<string,FakeTransfer> Engines {get;}
    public RuntimeDownloadQueue Queue {get;}
    public FakeNative Native {get;}
    public IMobileNativePorts NativePorts=>Native;
    public IRuntimeDownloads Downloads=>Queue;
    public string? LastError=>null;
    public bool IsInstalled()=>true;
    public bool DeviceSupportsPublishedGame()=>true;
    public bool OpenGame()=>true;
    public bool OpenOnline(string? invitation=null)=>true;
    public bool OpenDownloadPage()=>true;
}
sealed class FakeNative(MobileNativePort[] ports):IMobileNativePorts
{
    public IReadOnlyList<MobileNativePort> Catalog=>ports;
    public HashSet<string> Installed {get;}=[];
    public HashSet<string> Blocked {get;}=[];
    public List<string> Launched {get;}=[];
    public List<string> Prepared {get;}=[];
    public RuntimeDownloadQueue Queue {get;set;}=null!;
    public Dictionary<string,FakeTransfer> Engines {get;set;}=null!;
    public bool IsInstalled(string id)=>Installed.Contains(id);
    public bool IsUpdateAvailable(string id)=>false;
    public bool IsPackageReady(string id)=>Engines[id].Snapshot.State==RuntimeDownloadState.Ready;
    public string? CompatibilityError(string id)=>Blocked.Contains(id)?"Incompatible":null;
    public Task<string> PrepareAsync(string id){Prepared.Add(id);if(!IsPackageReady(id))Queue.Enqueue(id);return Task.FromResult("Confirmation Android requise");}
    public void Launch(string id)=>Launched.Add(id);
}
