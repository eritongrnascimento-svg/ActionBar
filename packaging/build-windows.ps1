param(
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [string]$Compiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'BarraInteligente.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$payload = Join-Path $root 'obj\installer\win-x64'
$bootstrap = Join-Path $root 'obj\installer\WebView2Setup.exe'
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $bootstrap -Parent) | Out-Null
$resolved = (Resolve-Path -LiteralPath $OutputDir).Path
Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrap
$signature = Get-AuthenticodeSignature -LiteralPath $bootstrap
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw 'O bootstrapper do WebView2 não tem uma assinatura Microsoft válida.'
}
dotnet publish $project -c Release -r win-x64 --self-contained true -o $payload -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar os binários do Windows.' }
& $Compiler "-DAppVersion=$version" "-DPayload=$payload" "-DWebViewBootstrap=$bootstrap" "-O$resolved" (Join-Path $PSScriptRoot 'windows.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }

