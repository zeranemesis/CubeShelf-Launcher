using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CubeShelf.Mobile;

var tests = 0;
await Scenario("download, hash verification and explicit install", async fixture =>
{
    await fixture.Service.CheckAsync();
    Require(fixture.Service.Snapshot.State == AppUpdateState.Available);
    Require(fixture.Installations == 0);
    await fixture.Service.DownloadAsync();
    Require(fixture.Service.Snapshot.State == AppUpdateState.Ready);
    Require(fixture.Installations == 0);
    Require(fixture.Service.Install() == "installer opened" && fixture.Installations == 1);
});
await Scenario("current version does not offer a downgrade", async fixture =>
{
    fixture.Tag = "v0.16.0"; await fixture.Service.CheckAsync();
    Require(fixture.Service.Snapshot.State == AppUpdateState.Current);
});
await Scenario("prerelease is rejected", async fixture =>
{
    fixture.Prerelease = true; await fixture.Service.CheckAsync(); RequireFailed(fixture);
});
await Scenario("wrong architecture is rejected", async fixture =>
{
    fixture.Name = "CubeShelf-Android-x86_64-preview.apk"; await fixture.Service.CheckAsync(); RequireFailed(fixture);
});
await Scenario("foreign download URL is rejected", async fixture =>
{
    fixture.ForeignUrl = true; await fixture.Service.CheckAsync(); RequireFailed(fixture);
});
await Scenario("missing checksum is rejected", async fixture =>
{
    fixture.Checksum = ""; await fixture.Service.CheckAsync(); RequireFailed(fixture);
});
await Scenario("GitHub digest and checksum must agree", async fixture =>
{
    fixture.Digest = new string('0', 64); await fixture.Service.CheckAsync(); RequireFailed(fixture);
});
await Scenario("modified APK is rejected without installation", async fixture =>
{
    await fixture.Service.CheckAsync(); fixture.Payload[0] ^= 1; await fixture.Service.DownloadAsync();
    RequireFailed(fixture); Require(fixture.Installations == 0); Require(!Directory.GetFiles(fixture.Directory, "*.part", SearchOption.AllDirectories).Any());
});
await Scenario("truncated APK is rejected", async fixture =>
{
    await fixture.Service.CheckAsync(); fixture.Payload = [1]; await fixture.Service.DownloadAsync(); RequireFailed(fixture);
});
await Scenario("cancellation removes the partial download and allows retry", async fixture =>
{
    await fixture.Service.CheckAsync(); fixture.WaitForCancellation = true;
    var download = fixture.Service.DownloadAsync();
    await fixture.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
    fixture.Service.CancelDownload(); await download;
    Require(fixture.Service.Snapshot.State == AppUpdateState.Available);
    fixture.WaitForCancellation = false; await fixture.Service.DownloadAsync();
    Require(fixture.Service.Snapshot.State == AppUpdateState.Ready);
});
await Scenario("network failure is retryable", async fixture =>
{
    fixture.Offline = true; await fixture.Service.CheckAsync(); RequireFailed(fixture);
    fixture.Offline = false; await fixture.Service.CheckAsync(); Require(fixture.Service.Snapshot.State == AppUpdateState.Available);
});
Console.WriteLine($"{tests} Android application update tests passed.");

async Task Scenario(string title, Func<Fixture, Task> test)
{
    using var fixture = new Fixture(); await test(fixture); tests++; Console.WriteLine("OK  " + title);
}
static void Require(bool condition) { if (!condition) throw new Exception("Update contract failed."); }
static void RequireFailed(Fixture fixture) => Require(fixture.Service.Snapshot.State == AppUpdateState.Failed);

sealed class Fixture : HttpMessageHandler
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "cubeshelf-update-test-" + Guid.NewGuid());
    public byte[] Payload = Encoding.UTF8.GetBytes("test apk contents");
    public string Tag = "v0.17.0", Name = "CubeShelf-Android-arm64-preview.apk", Checksum, Digest;
    public bool Prerelease, ForeignUrl, Offline, WaitForCancellation;
    public int Installations;
    public TaskCompletionSource DownloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public LauncherAppUpdates Service { get; }
    public Fixture()
    {
        Checksum = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant(); Digest = Checksum;
        Service = new("0.16.0", "arm64", Directory, _ => { Installations++; return "installer opened"; }, new HttpClient(this, false));
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Offline) throw new HttpRequestException("offline");
        HttpContent content;
        if (request.RequestUri!.AbsolutePath.EndsWith("/latest"))
            content = new StringContent(JsonSerializer.Serialize(new { tag_name = Tag, draft = false, prerelease = Prerelease,
                assets = new[] { new { name = Name, size = Payload.Length, digest = "sha256:" + Digest,
                    browser_download_url = ForeignUrl ? "https://example.org/untrusted.apk" : $"https://github.com/zeranemesis/CubeShelf-Launcher/releases/download/{Tag}/{Name}" } } }));
        else if (request.RequestUri.AbsolutePath.EndsWith("checksums.txt")) content = new StringContent(Checksum + "  " + Name);
        else
        {
            DownloadStarted.TrySetResult();
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            content = new ByteArrayContent(Payload);
        }
        return new(HttpStatusCode.OK) { Content = content, RequestMessage = request };
    }
    protected override void Dispose(bool disposing)
    {
        if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        base.Dispose(disposing);
    }
}
