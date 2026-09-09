using System.IO.Compression;
using System.Diagnostics;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: CubeShelf.Updater <pid> <zip> <installDir>");
    return 2;
}

var pid = int.Parse(args[0]);
var zip = Path.GetFullPath(args[1]);
var installDir = Path.GetFullPath(args[2]);

try
{
    var process = Process.GetProcessById(pid);
    await process.WaitForExitAsync();
}
catch { }

var staging = Path.Combine(Path.GetTempPath(), "CubeShelfUpdate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(staging);
ZipFile.ExtractToDirectory(zip, staging, true);

foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
{
    var rel = Path.GetRelativePath(staging, file);
    var dst = Path.Combine(installDir, rel);
    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
    File.Copy(file, dst, true);
}

var launcher = Path.Combine(installDir, "CubeShelf.exe");
if (File.Exists(launcher))
    Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = installDir, UseShellExecute = true });

try { Directory.Delete(staging, true); } catch { }
return 0;
