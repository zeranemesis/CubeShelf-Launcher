param(
    [string]$Version = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Version) {
    # VERSION at the repository root is the single source of truth (see Directory.Build.props).
    $Version = (Get-Content -LiteralPath (Join-Path $repo "VERSION") -Raw).Trim()
}
if (-not $Version) { throw "VERSION est vide ou introuvable." }
$publish = Join-Path $repo "publish\CubeShelf"
$updater = Join-Path $repo "publish\Updater"
$dist = Join-Path $repo "dist"
$localDotnet = Join-Path $repo ".tools\dotnet\dotnet.exe"
$workspaceDotnet = Join-Path (Split-Path -Parent $repo) ".tools\dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } elseif (Test-Path -LiteralPath $workspaceDotnet) { $workspaceDotnet } else { "dotnet" }
$nugetConfig = Join-Path $repo "NuGet.Config"
$numericVersion = ($Version -split '-', 2)[0]
if ($numericVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "Version numérique invalide : $numericVersion"
}

$repoRoot = [IO.Path]::GetFullPath($repo).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($generatedDirectory in @($publish, $updater)) {
    $resolvedGeneratedDirectory = [IO.Path]::GetFullPath($generatedDirectory)
    if (-not $resolvedGeneratedDirectory.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Dossier de publication hors du dépôt : $resolvedGeneratedDirectory"
    }
    if (Test-Path -LiteralPath $resolvedGeneratedDirectory) {
        Remove-Item -LiteralPath $resolvedGeneratedDirectory -Recurse -Force
    }
}

& $dotnet publish `
    (Join-Path $repo "src\CubeShelf.Desktop\CubeShelf.Desktop.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    "-p:Version=$Version" "-p:DebugType=None" "-p:DebugSymbols=false" `
    --configfile $nugetConfig -o $publish

& $dotnet publish `
    (Join-Path $repo "src\CubeShelf.Updater\CubeShelf.Updater.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    "-p:Version=$Version" "-p:PublishSingleFile=true" `
    "-p:DebugType=None" "-p:DebugSymbols=false" `
    --configfile $nugetConfig -o $updater

Copy-Item -LiteralPath (Join-Path $updater "CubeShelf.Updater.exe") `
    -Destination (Join-Path $publish "CubeShelf.Updater.exe") -Force

$compilerCandidates = @(
    (Join-Path (Split-Path -Parent $repo) ".tools\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)

$compiler = $compilerCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1

if (-not $compiler) {
    throw "Inno Setup 6 est requis. Installe-le puis relance BUILD_INSTALLER.ps1."
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
Get-ChildItem -LiteralPath $publish -Filter "*.pdb" -Recurse -File |
    Remove-Item -Force
Compress-Archive -Path (Join-Path $publish "*") `
    -DestinationPath (Join-Path $dist "CubeShelf-win-x64.zip") -Force

& $compiler `
    "/DMyAppVersion=$Version" `
    "/DMyAppNumericVersion=$numericVersion" `
    "/DPublishDirectory=$publish" `
    "/DInstallerOutputDirectory=$dist" `
    (Join-Path $repo "installer\CubeShelf.iss")

if ($LASTEXITCODE -ne 0) {
    throw "La création de l'installateur CubeShelf a échoué."
}

$installer = Join-Path $dist "CubeShelf-Setup-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "L'installateur attendu n'a pas été produit."
}

$artifacts = @(
    (Join-Path $dist "CubeShelf-win-x64.zip"),
    $installer
)

$checksums = foreach ($artifact in $artifacts) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($artifact))"
}

$checksums | Out-File (Join-Path $dist "checksums.txt") -Encoding ascii
$artifacts | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 }
