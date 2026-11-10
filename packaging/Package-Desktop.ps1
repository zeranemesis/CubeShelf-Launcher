[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$sourceDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$destinationDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$sourcePrefix = $sourceDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($destinationDirectory.Equals($sourceDirectory, $pathComparison) -or $destinationDirectory.StartsWith($sourcePrefix, $pathComparison)) {
    throw 'The package output directory must be outside the published source directory.'
}
$version = (Get-Content -LiteralPath (Join-Path $repositoryDirectory 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid VERSION file.' }
$isWindowsPackage = $RuntimeIdentifier.StartsWith('win-')
$isMacPackage = $RuntimeIdentifier.StartsWith('osx-')
$executableName = if ($isWindowsPackage) { 'CubeShelf.exe' } else { 'CubeShelf' }
if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory $executableName) -PathType Leaf)) {
    throw "Missing published launcher: $executableName"
}
if ($isWindowsPackage -and -not (Test-Path -LiteralPath (Join-Path $sourceDirectory 'CubeShelf.Updater.exe') -PathType Leaf)) {
    throw 'Publish and include the Windows updater for the same architecture first.'
}
if ($isMacPackage -and -not $IsMacOS) { throw 'Assemble macOS bundles on a macOS host to preserve Unix permissions.' }
if ($RuntimeIdentifier.StartsWith('linux-') -and -not $IsLinux) { throw 'Assemble Linux archives on a Linux host to preserve Unix permissions.' }

# Refuse to reuse staging, so packaging cannot accidentally include stale binaries.
$stagingDirectory = Join-Path $destinationDirectory 'staging'
if (Test-Path -LiteralPath $stagingDirectory) { throw "Staging already exists: $stagingDirectory. Choose a new output directory." }
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
$packageName = if ($isMacPackage) { 'CubeShelf.app' } else { 'CubeShelf' }
$packageDirectory = Join-Path $stagingDirectory $packageName
$binaryDirectory = if ($isMacPackage) { Join-Path $packageDirectory 'Contents/MacOS' } else { $packageDirectory }
New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $sourceDirectory -Force | Copy-Item -Destination $binaryDirectory -Recurse -Force
foreach ($documentName in @('README.md', 'QUICKPLAY.md', 'DOWNLOADS.md')) {
    $documentPath = Join-Path $repositoryDirectory $documentName
    if (Test-Path -LiteralPath $documentPath) { Copy-Item -LiteralPath $documentPath -Destination $binaryDirectory }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DESKTOP-PORTABLE.md') -Destination $binaryDirectory

if ($isMacPackage) {
    $numericVersion = $version.Split('-')[0]
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>CubeShelf</string>
  <key>CFBundleIdentifier</key><string>io.github.zeranemesis.cubeshelf</string>
  <key>CFBundleName</key><string>CubeShelf</string>
  <key>CFBundleDisplayName</key><string>CubeShelf</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$numericVersion</string>
  <key>CFBundleVersion</key><string>$numericVersion</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
    Set-Content -LiteralPath (Join-Path $packageDirectory 'Contents/Info.plist') -Value $plist -Encoding utf8NoBOM
}

$archiveName = "CubeShelf-$RuntimeIdentifier-portable-preview"
if ($isWindowsPackage) {
    $archivePath = Join-Path $destinationDirectory ($archiveName + '.zip')
    Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archivePath -Force
}
else {
    & chmod +x (Join-Path $binaryDirectory 'CubeShelf')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot set launcher executable permission.' }
    $archivePath = Join-Path $destinationDirectory ($archiveName + '.tar.gz')
    & tar -C $stagingDirectory -czf $archivePath $packageName
    if ($LASTEXITCODE -ne 0) { throw 'Archive creation failed.' }
}
$digest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archivePath + '.sha256') -Value "$digest  $([IO.Path]::GetFileName($archivePath))" -Encoding ascii
Write-Host "Packaged $archivePath"
