# Pubblicazione autonoma (self-contained) di KXC-ConnectionManager: non serve installare .NET.
# Uso: pwsh build/publish.ps1 [-Runtime win-x64|linux-x64] [-Version 0.1.0]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string] $Runtime = 'win-x64',
    [string] $Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$name = "KXC-ConnectionManager-$Version-$Runtime"
$out = Join-Path $root "publish/$name"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish (Join-Path $root 'src/Kxc.App/Kxc.App.csproj') -c Release -r $Runtime --self-contained `
    -p:Version=$Version -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'Pubblicazione dell''app non riuscita.' }

# L'app porta con sé una copia di kxc-askpass che richiede il runtime: la si sostituisce con un
# eseguibile singolo e autonomo.
Get-ChildItem $out -Filter 'kxc-askpass*' | Remove-Item -Force
dotnet publish (Join-Path $root 'src/Kxc.AskPass/Kxc.AskPass.csproj') -c Release -r $Runtime --self-contained `
    -p:PublishSingleFile=true -p:PublishTrimmed=true -p:Version=$Version -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'Pubblicazione di kxc-askpass non riuscita.' }

Copy-Item (Join-Path $root 'LICENSE') $out
Copy-Item (Join-Path $root 'README.md') $out

if ($Runtime -eq 'win-x64') {
    $zip = Join-Path $root "publish/$name.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path "$out/*" -DestinationPath $zip
    Write-Host "Archivio: $zip"
}
else {
    Copy-Item (Join-Path $root 'packaging/linux/kxc-connectionmanager.desktop') $out
    Copy-Item -Recurse (Join-Path $root 'packaging/linux/icons') $out
    $tar = Join-Path $root "publish/$name.tar.gz"
    tar -czf $tar -C (Join-Path $root 'publish') $name
    Write-Host "Archivio: $tar"
}
