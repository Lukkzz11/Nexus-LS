<#
.SYNOPSIS
  Gera e envia uma nova versão do Nexus LS para o repositório de atualizações (GitHub Releases).

.DESCRIPTION
  1. confere que o código está commitado e que a versão é maior que a última publicada
  2. roda todos os testes (se algum falhar, para tudo: nada é enviado)
  3. gera o NexusLS.exe com o número da versão gravado dentro dele
  4. calcula o SHA-256 (o programa instalado confere o arquivo baixado contra ele)
  5. cria o release no repositório de atualizações com o .exe e o .sha256

  Os programas já instalados avisam "Nova versão disponível" na próxima abertura.

.EXAMPLE
  .\publicar-versao.ps1 -Versao 1.2.0 -Notas "- Contas e saldos`n- Atualizador"

.EXAMPLE
  .\publicar-versao.ps1 -Versao 1.2.1 -NotasArquivo notas.txt

.EXAMPLE
  .\publicar-versao.ps1 -Versao 1.2.0 -Notas "teste" -SoGerar    # monta tudo, mas não envia nada

  Antes da primeira vez: instale o GitHub CLI (winget install GitHub.cli) e entre com `gh auth login`.
  O repositório de atualizações precisa ser PÚBLICO e conter só os instaladores (nunca o código-fonte).
#>
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [string]$Notas = "",
    [string]$NotasArquivo = "",
    [string]$Repositorio = "Lukkzz11/Nexus-LS---Update",
    [switch]$SemTestes,
    [switch]$SoGerar
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Falhar($mensagem) {
    Write-Host ""
    Write-Host "ERRO: $mensagem" -ForegroundColor Red
    exit 1
}

function Passo($texto) { Write-Host ""; Write-Host "==> $texto" -ForegroundColor Cyan }

# ---------- 1. conferências ----------
Passo "Conferindo a versão e o código"
$Versao = $Versao.Trim().TrimStart("v", "V")
if ($Versao -notmatch '^\d+\.\d+\.\d+$') { Falhar "Versão inválida: '$Versao'. Use o formato 1.2.3." }

if ($NotasArquivo) {
    if (-not (Test-Path $NotasArquivo)) { Falhar "Arquivo de notas não encontrado: $NotasArquivo" }
    $Notas = Get-Content $NotasArquivo -Raw -Encoding UTF8
}
if ([string]::IsNullOrWhiteSpace($Notas)) { Falhar "Informe o que mudou (-Notas ""..."" ou -NotasArquivo arquivo.txt): é o texto que o usuário lê antes de atualizar." }

$sujo = git status --porcelain
if ($sujo) { Falhar "Há alterações sem commit. Faça o commit antes de publicar, para a versão corresponder ao código:`n$sujo" }

$temGh = [bool](Get-Command gh -ErrorAction SilentlyContinue)
if (-not $SoGerar) {
    if (-not $temGh) { Falhar "O GitHub CLI (gh) não está instalado. Rode: winget install GitHub.cli   e depois: gh auth login" }
    gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { Falhar "Você não está logado no GitHub. Rode: gh auth login" }

    $ultima = gh release view --repo $Repositorio --json tagName --jq .tagName 2>$null
    if ($LASTEXITCODE -eq 0 -and $ultima) {
        $atual = [version]($ultima.TrimStart("v") )
        if ([version]$Versao -le $atual) { Falhar "A versão $Versao não é maior que a última publicada ($ultima). Escolha um número maior." }
        Write-Host "Última publicada: $ultima  ->  nova: v$Versao"
    } else {
        Write-Host "Nenhuma versão publicada ainda em $Repositorio. Esta será a primeira."
    }
}

# ---------- 2. testes ----------
if (-not $SemTestes) {
    Passo "Rodando os testes (se algum falhar, nada é enviado)"
    dotnet test --nologo -v q
    if ($LASTEXITCODE -ne 0) { Falhar "Testes falharam. Corrija antes de publicar." }
}

# ---------- 3. gerar o executável ----------
Passo "Gerando o pacote do editor do catálogo (JSX)"
& (Join-Path $PSScriptRoot "web\construir.ps1")
if ($LASTEXITCODE -ne 0) { Falhar "A geração do pacote do editor do catálogo falhou." }
Passo "Gerando o NexusLS.exe versão $Versao"
$saida = Join-Path $PSScriptRoot "publicar_release"
if (Test-Path $saida) { Remove-Item $saida -Recurse -Force }
dotnet publish src/NexusLS.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Versao -p:InformationalVersion=$Versao -o $saida -v q --nologo
if ($LASTEXITCODE -ne 0) { Falhar "A geração do executável falhou." }

$exe = Join-Path $saida "NexusLS.exe"
if (-not (Test-Path $exe)) { Falhar "NexusLS.exe não foi gerado." }
$info = (Get-Item $exe).VersionInfo
Write-Host ("Arquivo: {0:N1} MB  |  versão gravada no arquivo: {1}" -f ((Get-Item $exe).Length / 1MB), $info.ProductVersion)
if (-not $info.ProductVersion.StartsWith($Versao)) { Falhar "A versão gravada no arquivo ($($info.ProductVersion)) não é a pedida ($Versao)." }

# ---------- 4. assinatura de conferência ----------
Passo "Calculando o SHA-256"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$arquivoHash = Join-Path $saida "NexusLS.exe.sha256"
[IO.File]::WriteAllText($arquivoHash, "$hash  NexusLS.exe`n", (New-Object Text.UTF8Encoding($false)))
Write-Host $hash

$arquivoNotas = Join-Path $saida "notas.md"
[IO.File]::WriteAllText($arquivoNotas, $Notas.Trim() + "`n", (New-Object Text.UTF8Encoding($false)))

if ($SoGerar) {
    Passo "Pronto (modo -SoGerar): nada foi enviado"
    Write-Host "Arquivos em: $saida"
    exit 0
}

# ---------- 5. enviar ----------
Passo "Enviando para $Repositorio como v$Versao"
gh release create "v$Versao" $exe $arquivoHash --repo $Repositorio --title "Nexus LS $Versao" --notes-file $arquivoNotas
if ($LASTEXITCODE -ne 0) { Falhar "O envio ao GitHub falhou. Nada foi publicado." }

git tag -a "v$Versao" -m "Nexus LS $Versao" 2>$null

Passo "Versão $Versao publicada"
Write-Host "Os programas instalados vão avisar na próxima abertura (ou em Configurações > Verificar atualizações)."
Write-Host "Para instalar na própria máquina: feche o Nexus LS e copie $exe sobre publicar\NexusLS.exe."
