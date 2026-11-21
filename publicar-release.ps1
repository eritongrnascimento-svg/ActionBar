# Gera um pacote do ActionBar para enviar a outras pessoas.
#
#   .\publicar-release.ps1 -Notas "O que mudou 1","O que mudou 2"     (rodar dentro do PowerShell)
#
# • Executável único (já inclui o .NET; quem recebe não precisa instalar nada).
# • Tudo fica em .\releases: um .zip por versão (os antigos NUNCA são apagados nem substituídos)
#   e HISTORICO-DE-ATUALIZACOES.txt, que ganha uma entrada nova a cada pacote.
# • A montagem usa sempre a mesma pasta temporária (obj\release-staging), apagada no fim,
#   para não acumular cópias do projeto.
# • A versão vem de <Version> no BarraInteligente.csproj; aumente-a antes de gerar um pacote novo.
param(
    [Parameter(Mandatory = $true)][string[]]$Notas,
    [string]$Versao
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'BarraInteligente.csproj'
if (-not $Versao) { $Versao = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if (-not $Versao) { throw 'Defina <Version> no BarraInteligente.csproj ou use -Versao.' }

$releases = Join-Path $root 'releases'
$zip = Join-Path $releases "ActionBar-$Versao-win-x64.zip"
$history = Join-Path $releases 'HISTORICO-DE-ATUALIZACOES.txt'
if (Test-Path $zip) { throw "Já existe um pacote da versão $Versao. Aumente <Version> no BarraInteligente.csproj para gerar outro (os pacotes antigos são mantidos)." }
New-Item -ItemType Directory -Force $releases | Out-Null

$staging = Join-Path $root 'obj\release-staging'
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
Write-Host "Gerando ActionBar $Versao…"
dotnet publish $project -c Release -r win-x64 --self-contained true -o $staging `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=$Versao | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar o executável.' }
if (-not (Test-Path (Join-Path $staging 'ActionBar.exe'))) { throw 'ActionBar.exe não foi gerado.' }

# Histórico: entrada nova no topo, as anteriores continuam abaixo.
$date = Get-Date -Format 'dd/MM/yyyy HH:mm'
$entry = @("ActionBar $Versao  ($date)") + ($Notas | ForEach-Object { "  - $_" }) + ''
$previous = if (Test-Path $history) { Get-Content $history -Encoding UTF8 } else { @('HISTÓRICO DE ATUALIZAÇÕES — ActionBar', '======================================', '') }
$header = $previous | Select-Object -First 3
$body = $previous | Select-Object -Skip 3
Set-Content -Path $history -Value ($header + $entry + $body) -Encoding UTF8

@"
ActionBar $Versao
=================

Barra de pesquisa e ações rápidas para Windows 10/11 (64 bits).

COMO USAR
  1. Extraia este .zip numa pasta só para ele (ex.: Documentos\ActionBar).
     O ActionBar guarda as configurações numa pasta "data" ao lado do executável.
  2. Abra ActionBar.exe. Não precisa instalar nada.
  3. Use Alt + Espaço para abrir ou esconder a barra. O ícone "AB" fica na bandeja,
     perto do relógio (Abrir, Configurações, Sair).

AVISO DO WINDOWS
  Como o programa ainda não tem assinatura digital, o Windows pode mostrar
  "O Windows protegeu o computador". Clique em "Mais informações" e depois em
  "Executar assim mesmo".

DICAS
  - Digite o nome de um app, arquivo, pasta ou configuração do Windows.
  - "obs d" procura "obs" dando prioridade à unidade D.
  - → no fim do texto troca de aba; "volume" mostra uma barra de volume.
  - Na primeira abertura a pesquisa de arquivos indexa Documentos, Área de Trabalho
    e Downloads; outras unidades podem ser marcadas em Configurações.
"@ | Set-Content -Path (Join-Path $staging 'LEIA-ME.txt') -Encoding UTF8
Copy-Item $history (Join-Path $staging 'HISTORICO-DE-ATUALIZACOES.txt')

Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item -Recurse -Force $staging
$size = '{0:N1} MB' -f ((Get-Item $zip).Length / 1MB)
Write-Host "Pacote: $zip ($size)"
Write-Host "Histórico: $history"
Get-ChildItem $releases -Filter '*.zip' | Sort-Object Name | ForEach-Object { Write-Host ("  {0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
