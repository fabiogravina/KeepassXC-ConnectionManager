# Pubblicazione autonoma (self-contained) di KXC-ConnectionManager: non serve installare .NET.
#
# Uso:
#   pwsh build/publish.ps1 [-Runtime win-x64|linux-x64] [-Version 0.1.0]
#        [-CertificateThumbprint <impronta>] [-TimestampServer <url>] [-RequireSignature]
#        [-GpgKey <id chiave>]
#
# S16 (analisi di sicurezza): con un certificato di firma del codice, eseguibili e DLL dell'app vengono
# firmati con Authenticode (Smart App Control di Windows 11 blocca i binari non firmati e sconosciuti).
# L'impronta del certificato si può dare anche con la variabile KXC_SIGN_THUMBPRINT; il certificato
# va cercato in Cert:\CurrentUser\My o Cert:\LocalMachine\My. Con -RequireSignature lo script si
# ferma invece di produrre un pacchetto non firmato. Per Linux, -GpgKey produce una firma .asc.
# In ogni caso publish/SHA256SUMS riporta l'hash di ogni archivio.
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string] $Runtime = 'win-x64',
    [string] $Version = '0.1.0',
    [string] $CertificateThumbprint = $env:KXC_SIGN_THUMBPRINT,
    [string] $TimestampServer = 'http://timestamp.digicert.com',
    [switch] $RequireSignature,
    [string] $GpgKey
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$name = "KXC-ConnectionManager-$Version-$Runtime"
$publishDir = Join-Path $root 'publish'
$out = Join-Path $publishDir $name

function Get-SigningCertificate {
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) { return $null }
    $thumbprint = $CertificateThumbprint -replace '\s', ''
    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert |
        Where-Object Thumbprint -eq $thumbprint | Select-Object -First 1
    if (-not $certificate) { throw "Certificato di firma del codice $thumbprint non trovato." }
    if (-not $certificate.HasPrivateKey) { throw "Il certificato $thumbprint non ha la chiave privata." }
    return $certificate
}

# Solo i binari dell'app: quelli di terze parti (runtime .NET, Avalonia, Skia) hanno la loro firma.
function Get-OwnBinaries([string] $directory) {
    Get-ChildItem $directory -File | Where-Object {
        $_.Name -in @('KXC-ConnectionManager.exe', 'KXC-ConnectionManager.dll', 'kxc-askpass.exe') -or $_.Name -like 'Kxc.*.dll'
    }
}

function Invoke-Signing([string] $directory, $certificate) {
    foreach ($file in Get-OwnBinaries $directory) {
        $arguments = @{ FilePath = $file.FullName; Certificate = $certificate; HashAlgorithm = 'SHA256' }
        if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) { $arguments.TimestampServer = $TimestampServer }
        $result = Set-AuthenticodeSignature @arguments
        if (-not $result.SignerCertificate) { throw "Firma di $($file.Name) non riuscita: $($result.StatusMessage)" }
        if ($RequireSignature -and $result.Status -ne 'Valid') {
            throw "Firma di $($file.Name) non valida ($($result.Status)): serve un certificato di un'autorità riconosciuta."
        }
        Write-Host ("Firmato {0} ({1})" -f $file.Name, $result.Status)
    }
}

function Update-Checksums([string] $archive) {
    $sums = Join-Path $publishDir 'SHA256SUMS'
    $fileName = Split-Path -Leaf $archive
    $hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
    $lines = @()
    if (Test-Path $sums) { $lines = @(Get-Content $sums | Where-Object { $_ -notmatch "  $([regex]::Escape($fileName))$" }) }
    $lines += "$hash  $fileName"
    Set-Content -Path $sums -Value ($lines | Sort-Object { $_.Substring(66) }) -Encoding ascii
    Write-Host "SHA256 $fileName = $hash"
}

$certificate = if ($Runtime -eq 'win-x64') { Get-SigningCertificate } else { $null }
if ($Runtime -eq 'win-x64' -and -not $certificate -and $RequireSignature) {
    throw 'Firma richiesta ma nessun certificato indicato (-CertificateThumbprint o KXC_SIGN_THUMBPRINT).'
}

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
    if ($certificate) {
        Invoke-Signing $out $certificate
    }
    else {
        Write-Warning 'Pacchetto NON firmato: Smart App Control può bloccare kxc-askpass e l''app. Vedi -CertificateThumbprint.'
    }

    $archive = Join-Path $publishDir "$name.zip"
    if (Test-Path $archive) { Remove-Item -Force $archive }
    Compress-Archive -Path "$out/*" -DestinationPath $archive
}
else {
    Copy-Item (Join-Path $root 'packaging/linux/kxc-connectionmanager.desktop') $out
    Copy-Item -Recurse (Join-Path $root 'packaging/linux/icons') $out
    $archive = Join-Path $publishDir "$name.tar.gz"
    tar -czf $archive -C $publishDir $name
    if ($LASTEXITCODE -ne 0) { throw 'Creazione del tar.gz non riuscita.' }

    if ($GpgKey) {
        gpg --batch --yes --local-user $GpgKey --armor --detach-sign --output "$archive.asc" $archive
        if ($LASTEXITCODE -ne 0) { throw 'Firma GPG non riuscita.' }
        Write-Host "Firma GPG: $archive.asc"
    }
}

Update-Checksums $archive
Write-Host "Archivio: $archive"
