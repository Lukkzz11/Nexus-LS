<#
.SYNOPSIS
  Gera e envia uma nova versão do Nexus LS para o repositório de atualizações (GitHub Releases).

.DESCRIPTION
  1. confere que o código está commitado e que a versão é maior que a última publicada
  2. roda todos os testes (se algum falhar, para tudo: nada é enviado)
  3. gera o NexusLS.exe com o número da versão gravado dentro dele
  4. assina o executável com Authenticode, se um certificado for informado (opcional)
  5. calcula o SHA-256 DEPOIS da assinatura (o programa instalado confere o arquivo baixado contra ele)
  6. cria o release no repositório de atualizações com o .exe e o .sha256

  Os programas já instalados avisam "Nova versão disponível" na próxima abertura.

  ASSINATURA DIGITAL (OPCIONAL)
  Sem -Certificado (e sem a variável NEXUS_CERT_THUMBPRINT), o fluxo é o de sempre: o .exe é
  publicado sem assinatura e o script apenas avisa, em destaque. Com um certificado, o script
  localiza o signtool.exe do Windows SDK e assina com SHA-256 e carimbo de tempo (timestamp)
  da DigiCert. Se a assinatura não ficar com Status "Valid" no Get-AuthenticodeSignature,
  nada é enviado.

  Como obter um certificado:
  - Para distribuir de verdade, use um certificado de assinatura de código (Code Signing) de uma
    autoridade certificadora (ICP-Brasil ou internacional). Certificado autoassinado serve só
    para teste: em outra máquina o Windows continuará mostrando "editor desconhecido".
  - Instale o .pfx no repositório do Windows (CurrentUser\My ou LocalMachine\My) e informe o
    thumbprint em -Certificado, ou guarde o .pfx e informe o caminho + -SenhaCertificado.
  - Para assinar com thumbprint sem digitar toda vez, defina a variável de ambiente
    NEXUS_CERT_THUMBPRINT com o thumbprint do certificado.
  - O carimbo de tempo exige internet. Ele mantém a assinatura válida depois que o certificado
    vence.

.EXAMPLE
  .\publicar-versao.ps1 -Versao 1.2.0 -Notas "- Contas e saldos`n- Atualizador"

.EXAMPLE
  .\publicar-versao.ps1 -Versao 1.2.1 -NotasArquivo notas.txt

.EXAMPLE
  .\publicar-versao.ps1 -Versao 1.2.0 -Notas "teste" -SoGerar    # monta tudo, mas não envia nada

.EXAMPLE
  # Assina com um certificado já instalado no Windows (thumbprint).
  .\publicar-versao.ps1 -Versao 1.2.2 -NotasArquivo notas.txt -Certificado "A1B2C3D4E5F60718293A4B5C6D7E8F9012345678"

.EXAMPLE
  # Assina com um arquivo .pfx (a senha não é gravada pelo script; evite deixá-la no histórico do PowerShell).
  .\publicar-versao.ps1 -Versao 1.2.2 -NotasArquivo notas.txt -Certificado "C:\certificados\nexus.pfx" -SenhaCertificado "senha-do-pfx"

  Antes da primeira vez: instale o GitHub CLI (winget install GitHub.cli) e entre com `gh auth login`.
  O repositório de atualizações precisa ser PÚBLICO e conter só os instaladores (nunca o código-fonte).
#>
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [string]$Notas = "",
    [string]$NotasArquivo = "",
    [string]$Repositorio = "Lukkzz11/Nexus-LS---Update",
    [string]$Certificado = "",
    [string]$SenhaCertificado = "",
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

# Procura o signtool.exe do Windows SDK (PATH ou pastas padrão do SDK).
function Localizar-SignTool {
    $noCaminho = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($noCaminho) { return $noCaminho.Source }

    $raizes = @()
    if (${env:ProgramFiles(x86)}) { $raizes += (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin") }
    if ($env:ProgramFiles) { $raizes += (Join-Path $env:ProgramFiles "Windows Kits\10\bin") }

    foreach ($raiz in $raizes) {
        if (-not (Test-Path -LiteralPath $raiz)) { continue }
        $achado = Get-ChildItem -Path (Join-Path $raiz "*\x64\signtool.exe") -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($achado) { return $achado.FullName }
    }
    return $null
}

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

# ---------- 4. assinatura digital (opcional) ----------
# O certificado pode vir do parâmetro ou da variável NEXUS_CERT_THUMBPRINT.
$certificadoInformado = $Certificado
if ([string]::IsNullOrWhiteSpace($certificadoInformado) -and $env:NEXUS_CERT_THUMBPRINT) {
    $certificadoInformado = $env:NEXUS_CERT_THUMBPRINT
}
$certificadoInformado = ($certificadoInformado + "").Trim()

if ($certificadoInformado) {
    Passo "Assinando o executável (Authenticode)"

    $signtool = Localizar-SignTool
    if (-not $signtool) {
        Falhar "signtool.exe não encontrado. Instale o Windows SDK e o componente 'Windows SDK Signing Tools for Desktop Apps' (ou coloque o signtool.exe no PATH)."
    }
    Write-Host "signtool: $signtool"

    # /fd SHA256 = resumo do arquivo; /tr + /td = carimbo de tempo SHA-256 da DigiCert.
    # O carimbo mantém a assinatura válida mesmo depois de o certificado vencer.
    $argumentos = @("sign", "/fd", "SHA256", "/tr", "http://timestamp.digicert.com", "/td", "SHA256")

    $ehPfx = $certificadoInformado -match '\.pfx$'
    if ($ehPfx -or (Test-Path -LiteralPath $certificadoInformado -PathType Leaf)) {
        if (-not (Test-Path -LiteralPath $certificadoInformado -PathType Leaf)) {
            Falhar "Arquivo .pfx não encontrado: $certificadoInformado"
        }
        $pfx = (Resolve-Path -LiteralPath $certificadoInformado).Path
        if ([string]::IsNullOrEmpty($SenhaCertificado)) {
            Falhar "Para assinar com um .pfx, informe -SenhaCertificado. Nada foi assinado nem publicado."
        }
        $argumentos += @("/f", $pfx, "/p", $SenhaCertificado)
        Write-Host "Certificado: $pfx (arquivo .pfx)"
    } else {
        # Thumbprint: o certificado precisa estar instalado no repositório do Windows.
        $thumbprint = ($certificadoInformado -replace '\s', '').ToUpperInvariant()
        if ($thumbprint -notmatch '^[0-9A-F]{40}$') {
            Falhar "Certificado inválido: informe o thumbprint (40 caracteres hexadecimais) ou o caminho de um arquivo .pfx."
        }
        $achado = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $thumbprint } |
            Select-Object -First 1
        if (-not $achado) {
            Falhar "Certificado com thumbprint $thumbprint não encontrado em CurrentUser\My nem em LocalMachine\My."
        }
        $argumentos += @("/sha1", $thumbprint)
        Write-Host "Certificado: $($achado.Subject) (thumbprint $thumbprint)"
    }

    $argumentos += $exe
    & $signtool @argumentos
    if ($LASTEXITCODE -ne 0) {
        Falhar "A assinatura falhou (signtool código $LASTEXITCODE). O executável NÃO foi publicado."
    }

    $assinatura = Get-AuthenticodeSignature -FilePath $exe
    if ($assinatura.Status -ne 'Valid') {
        Falhar "A assinatura não ficou válida (Status: $($assinatura.Status); $($assinatura.StatusMessage)). Nada foi publicado. Confira se a cadeia do certificado está confiável e se o carimbo de tempo funcionou."
    }
    Write-Host "Assinatura válida: $($assinatura.SignerCertificate.Subject)" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "AVISO: O executável saiu SEM assinatura digital." -ForegroundColor Black -BackgroundColor Yellow
    Write-Host "AVISO: O Windows pode mostrar 'editor desconhecido' ao instalar. Para assinar, informe -Certificado (ou defina NEXUS_CERT_THUMBPRINT)." -ForegroundColor Yellow
    Write-Host ""
}

# ---------- 5. SHA-256 (SEMPRE depois da assinatura) ----------
# Assinar altera os bytes do arquivo; por isso o hash é calculado só agora, para
# corresponder exatamente ao .exe que será publicado.
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

# ---------- 6. enviar ----------
Passo "Enviando para $Repositorio como v$Versao"
gh release create "v$Versao" $exe $arquivoHash --repo $Repositorio --title "Nexus LS $Versao" --notes-file $arquivoNotas
if ($LASTEXITCODE -ne 0) { Falhar "O envio ao GitHub falhou. Nada foi publicado." }

git tag -a "v$Versao" -m "Nexus LS $Versao" 2>$null

Passo "Versão $Versao publicada"
Write-Host "Os programas instalados vão avisar na próxima abertura (ou em Configurações > Verificar atualizações)."
Write-Host "Para instalar na própria máquina: feche o Nexus LS e copie $exe sobre publicar\NexusLS.exe."
