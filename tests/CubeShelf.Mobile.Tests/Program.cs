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
using Avalonia.VisualTree;
using CubeShelf.Mobile;

var root=Path.Combine(Path.GetTempPath(),"decompshelf-mobile-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
using var json=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"android-ports.json"));
var ports=MobileNativePort.Load(json);
Check(ports.Length==23&&ports.Select(p=>p.PackageName).Distinct().Count()==23,"23 distinct Android ports and package identities");
Check(ports.All(p=>p.Abis.Contains("arm64-v8a")),"all published mobile ports have a verified ARM64 build");
Check(ports.Single(p=>p.Id=="n64-dk64").RequiresVulkan11 && ports.Single(p=>p.Id=="n64-harvestmoon").RequiresVulkan11,"Vulkan 1.1 requirements survive catalogue loading");
var manifest=System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory,"AndroidManifest.xml"));
System.Xml.Linq.XNamespace android="http://schemas.android.com/apk/res/android";
var visiblePackages=manifest.Root!.Element("queries")!.Elements("package").Select(p=>(string?)p.Attribute(android+"name")).ToHashSet();
Check(ports.Where(p=>p.Id!="n64-sm64").All(p=>visiblePackages.Contains(p.PackageName)),"Android 11 can discover every external port package");
Check(ports.Single(p=>p.Id=="wiiu-windwakerhd").RequiresVulkan13,"Wind Waker HD requires Vulkan 1.3");
foreach(var (subtype,label) in new[]{(13,"4G"),(19,"4G"),(20,"5G"),(3,"3G"),(2,"2G"),(0,"Mobile"),(18,"Mobile")})
    Check(MobileDeviceStatus.AndroidCellularLabel(subtype)==label,"cellular label preserves the reported network technology");
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

await Sm64Tests.Run(root);
RunUi();
Console.WriteLine("Mobile Android catalogue, download integrity, install handoff, cancellation and layout checks passed.");
void RunUi()
{
    AppBuilder.Configure<MobileApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions {UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
    foreach(var port in ports)
        foreach(var face in new[]{port.Front,port.Back,port.Spine,port.Right,port.Top,port.Bottom,port.Media}.Where(p=>p.Length>0))
        {using var asset=Avalonia.Platform.AssetLoader.Open(new Uri("avares://CubeShelf.Mobile/"+face));Check(asset.Length>0,"bundled exact cover: "+face);}
    var host=new FakeGames(root,ports);var view=new MobileLibraryView(host);var window=new Window {Width=390,Height=844,Content=view};window.Show();Jobs();
    Field<Button>("_appUpdatesButton").IsVisible=true;
    var picker=Field<ComboBox>("_consolePicker");
    var home=Field<CubeShelf.Desktop.Controls.PlatformHomeControl>("_platformHome");
    Check(home.IsVisible,"Android starts with the 3D console carousel");
    Check(Field<TextBlock>("_deviceTime").Text=="14:32"&&Field<TextBlock>("_deviceConnection").Text=="Wi-Fi"
        &&Field<TextBlock>("_deviceBattery").Text=="82 %","console home shows the device status supplied by Android");
    view.SetActive(false);Check(!Field<DispatcherTimer>("_deviceStatusTimer").IsEnabled,"device status polling stops while the game is in front");
    host.DeviceStatus=new("21:05",12,true,"4G");view.SetActive(true);Jobs();
    Check(Field<TextBlock>("_deviceTime").Text=="21:05"&&Field<TextBlock>("_deviceConnection").Text=="4G"
        &&Field<TextBlock>("_deviceBattery").Text=="12 %"&&Field<Avalonia.Controls.Shapes.Path>("_chargingMark").IsVisible,
        "resume immediately refreshes time, battery, charging and the active connection");
    host.DeviceStatus=new("21:05",101,false,"Hors ligne");
    view.GetType().GetMethod("RefreshDeviceStatus",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,null);Jobs();
    Check(Field<TextBlock>("_deviceBattery").Text=="— %"&&!Field<Avalonia.Controls.Shapes.Path>("_chargingMark").IsVisible
        &&Field<TextBlock>("_deviceConnection").Text=="Hors ligne","unavailable battery is not displayed as a fabricated percentage");
    host.DeviceStatus=new("14:32",82,false,"Wi-Fi");view.SetActive(true);Jobs();
    view.Padding=new Thickness(0,20,36,0);Jobs();CheckDeviceStatus();
    view.Padding=default;Jobs();
    var selectedConsole=home.SelectedConsole;
    for(var sort=0;sort<5;sort++){home.CycleSort();Check(home.SelectedConsole==selectedConsole,"sorting preserves the selected console");}
    home.CycleSort();home.CycleSort();home.Move(1);
    Check(home.SelectedConsole=="wii","chronological sorting puts Wii after GameCube among Android platforms");
    home.Move(1);Check(home.SelectedConsole=="wiiu","chronological sorting puts Wii U after Wii");
    home.Move(1);Check(home.SelectedConsole=="gb","chronological sorting wraps from the latest to the oldest platform");
    for(var sort=0;sort<3;sort++)home.CycleSort();
    if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } startupFolder)
    {
        Directory.CreateDirectory(startupFolder);
        foreach(var size in new[]{new Size(390,844),new Size(320,700),new Size(844,390)})
        {window.Width=size.Width;window.Height=size.Height;Jobs();CheckDeviceStatus();using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(startupFolder,$"mobile-platforms-{size.Width}.png"));}
        window.Width=390;window.Height=844;Jobs();
    }
    var platforms=CubeShelf.Core.Library.ShelfProduct.All.Where(p=>p.Id=="gamecube"||ports.Any(port=>port.ShelfConsole==p.Id)).ToArray();
    var n64=Array.FindIndex(platforms,p=>p.Id=="n64");
    var n64Count=ports.Count(p=>p.ShelfConsole=="n64");
    for(var step=0;step<platforms.Length&&home.SelectedConsole!="n64";step++)view.BrowseFromController(1);
    view.ActivateFromController();Check(!home.IsVisible&&picker.SelectedIndex==n64,"controller confirms the 3D console and opens its games");Jobs();
    Check(picker.ItemCount == 8 && picker.Items.Cast<string>().SequenceEqual(platforms.Select(p=>p.Console)), "Android only shows platforms backed by Android games");
    Check(Field<TextBlock>("_platformCaption").Text == $"{n64Count} jeu(x) · Android", "platform caption reflects the Android catalogue");
    for(var index=0;index<platforms.Length;index++)
    {
        picker.SelectedIndex=index;Jobs();
        var platform=platforms[index].Id;
        Check(Field<CubeShelf.Desktop.Controls.ConsoleBrandMark>("_consoleLogo").Platform==platform &&
            Field<TextBlock>("_platformCaption").Text==$"{ports.Count(p=>p.ShelfConsole==platform)+(platform=="gamecube"?1:0)} jeu(x) · Android", "verified Android platform selects its own catalogue: "+platform);
    }
    foreach(var port in ports)host.Native.Installed.Add(port.Id);
    host.Native.OnLaunch=()=>{view.SetActive(false);view.SetActive(true);};
    foreach(var native in ports)
    {
        var index=Array.FindIndex(platforms,p=>p.Id==native.ShelfConsole);
        picker.SelectedIndex=index;Jobs();
        var games=Field<Array>("_catalog").Cast<object>().ToArray();
        var gameIndex=Array.FindIndex(games,game=>(string?)game.GetType().GetProperty("Id")!.GetValue(game)==native.Id);
        Check(gameIndex>=0,"native port appears in its console catalogue");
        for(var step=0;step<games.Length&&Field<int>("_selected")!=gameIndex;step++)view.BrowseFromController(1);
        view.RefreshAvailability();
        var launched=host.Native.Launched.Count;
        Field<Button>("_play").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var transition=Field<CubeShelf.Desktop.Controls.GameLaunchTransition>("_launchTransition");
        transition.GetType().GetMethod("Advance",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(transition,[2.2]);
        for(var wait=0;wait<400&&host.Native.Launched.Count==launched;wait++){Jobs();Thread.Sleep(5);}
        Check(host.Native.Launched.Count==launched+1&&host.Native.Launched.Last()==native.Id,"Play launches the installed Android port: "+native.Id);
        Check(picker.SelectedIndex==index&&!home.IsVisible&&picker.IsEnabled,"return from the game preserves the catalogue and unlocks navigation: "+native.Id);
    }
    host.Native.OnLaunch=()=>throw new InvalidOperationException("Android launch failed");
    var beforeFailure=host.Native.Launched.Count;
    Field<Button>("_play").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Field<CubeShelf.Desktop.Controls.GameLaunchTransition>("_launchTransition").GetType().GetMethod("Advance",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Field<CubeShelf.Desktop.Controls.GameLaunchTransition>("_launchTransition"),[2.2]);
    for(var wait=0;wait<400&&!picker.IsEnabled;wait++){Jobs();Thread.Sleep(5);}
    Check(picker.IsEnabled&&Field<TextBlock>("_status").Text=="Android launch failed"&&host.Native.Launched.Count==beforeFailure,"failed Android handoff restores navigation and reports the failure");
    host.Native.OnLaunch=null;
    Field<Button>("_play").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Field<CubeShelf.Desktop.Controls.GameLaunchTransition>("_launchTransition").GetType().GetMethod("Advance",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Field<CubeShelf.Desktop.Controls.GameLaunchTransition>("_launchTransition"),[2.2]);
    for(var wait=0;wait<400&&host.Native.Launched.Count==beforeFailure;wait++){Jobs();Thread.Sleep(5);}
    Check(host.Native.Launched.Count==beforeFailure+1,"a failed Android launch can be retried");
    host.Native.Installed.Clear();host.Native.Launched.Clear();
    picker.SelectedIndex=0;picker.SelectedIndex=n64;Jobs();
    Check(Field<TextBlock>("_position").Text==$"1 / {n64Count}","Nintendo 64 selector exposes every Android port in the same mobile shell");
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
    foreach(var size in new[]{new Size(320,700),new Size(390,844),new Size(844,390)})
    {
        window.Width=size.Width;window.Height=size.Height;Jobs();
        var play=Field<Button>("_download");var origin=play.TranslatePoint(default,view)!.Value;
        CheckDeviceStatus();
        Check(origin.Y>=0&&origin.Y+play.Bounds.Height<=view.Bounds.Height+1,"primary action fits in portrait and landscape");
        if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } folder)
        {Directory.CreateDirectory(folder);using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(folder,$"mobile-n64-{size.Width}.png"));}
    }
    view.SetActive(false);view.SetActive(true);Jobs();Check(picker.SelectedIndex==n64&&!picker.IsDropDownOpen,"selected catalogue survives pause/resume without reopening the startup list");
    Check(view.DismissOverlay()&&home.IsVisible,"system Back returns from the library to available consoles");
    Check(!view.DismissOverlay()&&home.IsVisible,"system Back at console home is delegated to Android");
    view.ActivateFromController();Jobs();view.BackFromController();Jobs();Check(home.IsVisible,"controller B also returns to console home");
    home.IsVisible=false;picker.SelectedIndex=0;Jobs();Check(Field<Button>("_play").IsVisible&&Field<StackPanel>("_onlinePanel").IsVisible,"embedded PartyBoard actions remain available alongside native GameCube ports");
    var disc=new Uri("avares://CubeShelf.Mobile/Assets/Covers/Media/gamecube-GMPE01.png");
    using(var asset=Avalonia.Platform.AssetLoader.Open(disc))Check(asset.Length>0,"Mario Party 4 opening uses its own disc artwork");
    foreach(var size in new[]{new Size(320,700),new Size(390,844),new Size(844,390)})
    {
        window.Width=size.Width;window.Height=size.Height;Jobs();
        Field<Button>("_play").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
        var transition=Field<CubeShelf.Desktop.Controls.GameLaunchTransition>("_launchTransition");
        transition.GetType().GetMethod("Advance",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(transition,[.8]);Jobs();
        CheckDeviceStatus();
        var box=transition.GetType().GetField("_case",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(transition) as Control;
        Check(box!.Bounds.Width<=size.Width-48&&box.Bounds.Height<=size.Height-200,$"opening box fits the mobile viewport ({size}: {box.Bounds}; window {window.Bounds})");
        var back=transition.GetVisualDescendants().OfType<Button>().Single();
        var backOrigin=back.TranslatePoint(default,view)!.Value;
        Check(backOrigin.Y>=0&&backOrigin.Y+back.Bounds.Height<=view.Bounds.Height,"opening animation keeps its Back button on screen");
        if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } folder)
        {using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(folder,$"mobile-opening-{size.Width}.png"));}
        Check(host.Opened==0,"PartyBoard waits for its disc opening animation");
        view.BackFromController();Jobs();Check(!transition.IsVisible&&host.Opened==0,"Back cancels the disc animation before PartyBoard starts");
    }
    host.OnOpen=()=>{view.SetActive(false);view.SetActive(true);};
    for(var attempt=0;attempt<2;attempt++)
    {
        Field<Button>("_play").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
        var transition=Field<CubeShelf.Desktop.Controls.GameLaunchTransition>("_launchTransition");
        transition.GetType().GetMethod("Advance",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(transition,[2.2]);
        for(var wait=0;wait<100&&host.Opened==attempt;wait++){Jobs();Thread.Sleep(5);}
        Check(host.Opened==attempt+1&&picker.IsEnabled&&!home.IsVisible,"PartyBoard launches again after returning from Android");
    }
    window.Close();
    var embeddedHost=new FakeGames(root,ports);
    embeddedHost.Native.Embedded.Add(ports[0].Id);
    embeddedHost.Native.EmbeddedPreparation=new(TaskCreationOptions.RunContinuationsAsynchronously);
    view=new MobileLibraryView(embeddedHost);window=new Window {Width=390,Height=844,Content=view};window.Show();Jobs();
    picker=Field<ComboBox>("_consolePicker");Field<CubeShelf.Desktop.Controls.PlatformHomeControl>("_platformHome").IsVisible=false;picker.SelectedIndex=n64;Jobs();
    Check(Field<TextBlock>("_availability").Text!.Contains("Moteur intégré"),"embedded game requests a ROM without Android installation text");
    Field<Button>("_download").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
    Check(!picker.IsEnabled&&Field<Button>("_cancelNativePreparation").IsVisible&&!Field<Grid>("_downloadsOverlay").IsVisible,
        "embedded preparation stays in the library and exposes cancellation");
    foreach(var size in new[]{new Size(390,844),new Size(844,390)})
    {
        window.Width=size.Width;window.Height=size.Height;Jobs();
        var cancel=Field<Button>("_cancelNativePreparation");var origin=cancel.TranslatePoint(default,view)!.Value;
        Check(origin.Y>=0&&origin.Y+cancel.Bounds.Height<=view.Bounds.Height+1,"preparation cancellation fits in portrait and landscape");
        if(Environment.GetEnvironmentVariable("CUBESHELF_UI_SNAPSHOT_DIR") is { } folder)
        {Directory.CreateDirectory(folder);using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(folder,$"mobile-sm64-prepare-{size.Width}.png"));}
    }
    view.BrowseFromController(1);Jobs();Check(Field<TextBlock>("_position").Text==$"1 / {n64Count}","game selection cannot change under an active ROM preparation");
    view.SetActive(false);view.SetActive(true);Jobs();
    Field<Button>("_cancelNativePreparation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    for(var i=0;i<100&&!picker.IsEnabled;i++){Jobs();Thread.Sleep(5);}
    Check(picker.IsEnabled&&!Field<Button>("_play").IsVisible&&embeddedHost.Native.Launched.Count==0,"cancelling preparation cannot mark or launch the game");
    embeddedHost.Native.EmbeddedPreparation=new(TaskCreationOptions.RunContinuationsAsynchronously);
    Field<Button>("_download").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
    embeddedHost.Native.EmbeddedPreparation.SetResult("Préparation terminée");
    for(var i=0;i<100&&!Field<Button>("_play").IsVisible;i++){Jobs();Thread.Sleep(5);}
    Check(Field<Button>("_play").IsVisible&&!Field<Button>("_cancelNativePreparation").IsVisible
        &&embeddedHost.Engines[ports[0].Id].Snapshot.State==RuntimeDownloadState.NotInstalled,
        "embedded ROM preparation enables Play without enqueuing or installing an APK");
    window.Close();
    Check(!Field<DispatcherTimer>("_deviceStatusTimer").IsEnabled,"detaching the mobile view stops status polling");
    void CheckDeviceStatus()
    {
        var time=Field<TextBlock>("_deviceTime");var origin=time.TranslatePoint(default,view)!.Value;
        Check(Field<Border>("_deviceStatusBar").IsEffectivelyVisible&&origin.Y>=0&&origin.Y+time.Bounds.Height<=32
            +view.Padding.Top&&origin.X>=0&&origin.X+time.Bounds.Width<=view.Bounds.Width-view.Padding.Right
            &&origin.X+time.Bounds.Width>=view.Bounds.Width-32-view.Padding.Right,
            "device status stays at the top right, above console home and game overlays");
    }
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
    public MobileDeviceStatus? DeviceStatus {get;set;}=new("14:32",82,false,"Wi-Fi");
    public IRuntimeDownloads Downloads=>Queue;
    public string? LastError=>null;
    public bool IsInstalled()=>true;
    public bool DeviceSupportsPublishedGame()=>true;
    public int Opened {get;private set;}
    public Action? OnOpen {get;set;}
    public bool OpenGame(){Opened++;OnOpen?.Invoke();return true;}
    public bool OpenOnline(string? invitation=null)=>true;
    public bool OpenDownloadPage()=>true;
}
sealed class FakeNative(MobileNativePort[] ports):IMobileNativePorts
{
    public IReadOnlyList<MobileNativePort> Catalog=>ports;
    public HashSet<string> Installed {get;}=[];
    public HashSet<string> Embedded {get;}=[];
    public TaskCompletionSource<string>? EmbeddedPreparation {get;set;}
    public HashSet<string> Blocked {get;}=[];
    public List<string> Launched {get;}=[];
    public List<string> Prepared {get;}=[];
    public RuntimeDownloadQueue Queue {get;set;}=null!;
    public Dictionary<string,FakeTransfer> Engines {get;set;}=null!;
    public bool IsInstalled(string id)=>Installed.Contains(id);
    public bool IsEmbedded(string id)=>Embedded.Contains(id);
    public bool IsUpdateAvailable(string id)=>false;
    public bool IsPackageReady(string id)=>Engines[id].Snapshot.State==RuntimeDownloadState.Ready;
    public string? CompatibilityError(string id)=>Blocked.Contains(id)?"Incompatible":null;
    public async Task<string> PrepareAsync(string id,CancellationToken cancellation=default)
    {
        Prepared.Add(id);
        if(IsEmbedded(id)) {var message=await EmbeddedPreparation!.Task.WaitAsync(cancellation);Installed.Add(id);return message;}
        if(!IsPackageReady(id))Queue.Enqueue(id);return "Confirmation Android requise";
    }
    public Action? OnLaunch {get;set;}
    public void Launch(string id){OnLaunch?.Invoke();Launched.Add(id);}
}
