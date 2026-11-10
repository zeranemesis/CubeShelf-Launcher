[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$Version,
    [string]$PreviousTag = 'v0.18.0'
)
$ErrorActionPreference='Stop'
if (!$IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or !$env:RUNNER_TEMP) {
    throw 'This installation test is reserved for the disposable Windows GitHub runner.'
}
$testRoot=Join-Path $env:RUNNER_TEMP ('decompshelf-upgrade-'+[Guid]::NewGuid().ToString('N'))
$previous=Join-Path $testRoot 'previous'
$installation=Join-Path $testRoot 'application'
New-Item -ItemType Directory -Path $previous -Force | Out-Null
& gh release download $PreviousTag --repo $env:GITHUB_REPOSITORY --pattern 'CubeShelf-Setup-x64.exe' --pattern 'checksums.txt' --dir $previous
if ($LASTEXITCODE -ne 0) {throw 'Could not retrieve the previous installer and its checksum.'}
$oldInstaller=Join-Path $previous 'CubeShelf-Setup-x64.exe'
$checksum=Get-Content -LiteralPath (Join-Path $previous 'checksums.txt') | Where-Object {$_ -match '^([a-fA-F0-9]{64})\s+\*?CubeShelf-Setup-x64\.exe$'}
if (@($checksum).Count -ne 1 -or (Get-FileHash -LiteralPath $oldInstaller -Algorithm SHA256).Hash -ne ($checksum -split '\s+')[0]) {
    throw 'The previous installer checksum is invalid.'
}
function Install([string]$File) {
    $process=Start-Process -FilePath (Resolve-Path -LiteralPath $File).Path -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$installation+'"')) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {throw "Installer failed with exit code $($process.ExitCode)."}
}
Install $oldInstaller
if (!(Test-Path -LiteralPath (Join-Path $installation 'CubeShelf.exe'))) {throw 'The previous application was not installed.'}
$profile=Join-Path $env:LOCALAPPDATA 'CubeShelf'
New-Item -ItemType Directory -Force -Path $profile | Out-Null
$marker=Join-Path $profile '__installer-upgrade-probe.txt'
$value=[Guid]::NewGuid().ToString('N')
Set-Content -LiteralPath $marker -Value $value -NoNewline
Install $Installer
foreach ($name in @('CubeShelf.exe','CubeShelf.dll','CubeShelf.Updater.exe','Catalogs/3ds.json','Catalogs/wiiu.json','Assets/Consoles/wiiu.png')) {
    $installed=Join-Path $installation $name
    $published=Join-Path $PublishDirectory $name
    if (!(Test-Path -LiteralPath $installed) -or (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $published).Hash) {
        throw "Installed file differs from the verified build: $name"
    }
}
$installedVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $installation 'CubeShelf.dll')).ProductVersion
if (!$installedVersion.StartsWith($Version)) {throw "Unexpected installed version: $installedVersion"}
if ((Get-Content -LiteralPath $marker -Raw) -ne $value) {throw 'The upgrade changed profile data.'}
$uninstaller=Get-ChildItem -LiteralPath $installation -Filter 'unins*.exe' | Select-Object -First 1
if (!$uninstaller) {throw 'Missing uninstaller.'}
$process=Start-Process -FilePath $uninstaller.FullName -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $installation 'CubeShelf.exe'))) {throw 'Uninstallation failed.'}
if ((Get-Content -LiteralPath $marker -Raw) -ne $value) {throw 'Uninstall removed the user profile.'}
Remove-Item -LiteralPath $marker -Force
Write-Output "Verified installer upgrade $PreviousTag -> $Version and uninstall preserve the user profile."
