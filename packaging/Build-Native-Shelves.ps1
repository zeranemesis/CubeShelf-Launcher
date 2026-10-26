[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputDirectory = 'dist/native-shelves'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
if (Test-Path -LiteralPath $destination) { throw "Le dossier de sortie existe déjà : $destination. Choisir un nouveau dossier." }
$buildDirectory = Join-Path $repository ('publish/native-suite-' + [Guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $repository 'src/CubeShelf.Desktop/CubeShelf.Desktop.csproj') -c Release -r $RuntimeIdentifier --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $buildDirectory
if ($LASTEXITCODE -ne 0) { throw 'Échec de la compilation des applications.' }
New-Item -ItemType Directory -Path $destination | Out-Null
foreach ($product in @(@{Name='DecompShelf';Console='gamecube'})) {
    $directory = Join-Path $destination $product.Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    Get-ChildItem -LiteralPath $buildDirectory -Force | Copy-Item -Destination $directory -Recurse
    if ($product.Name -ne 'CubeShelf') {
        $extension = if ($RuntimeIdentifier.StartsWith('win-')) { '.exe' } else { '' }
        # Keep the original apphost for existing updater and rollback contracts.
        Copy-Item -LiteralPath (Join-Path $directory ('CubeShelf' + $extension)) -Destination (Join-Path $directory ($product.Name + $extension))
    }
    [IO.File]::WriteAllText((Join-Path $directory 'shelf-product.json'), (@{Console=$product.Console}|ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $repository 'docs/NATIVE-SHELVES.md') -Destination (Join-Path $directory 'LIRE-MOI.md')
    Copy-Item -LiteralPath (Join-Path $repository 'docs/NATIVE-PORT-INVENTORY.md') -Destination $directory
    Write-Output "Prêt : $directory"
}
Write-Output 'Une seule application : choisir GameCube, Wii ou Switch en haut à gauche. Conserver le dossier complet.'
