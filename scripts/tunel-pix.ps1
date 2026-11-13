<#
  Túnel da Cloudflare para o webhook do Pix (Mercado Pago) do Nexus.

  Uso rápido (URL temporária, sem conta):
      powershell -ExecutionPolicy Bypass -File scripts\tunel-pix.ps1
  Com o Jellyfin também (cria um 2º túnel temporário para a porta dele; qualquer pessoa com a URL o acessa):
      ... -Jellyfin
  Túnel fixo "NexusPagamento" com domínio seu (precisa de conta Cloudflare; o login abre o navegador e é você quem autoriza):
      ... -Fixo -Dominio pagamento.seudominio.com.br

  O Nexus escuta em http://127.0.0.1:5099 e só responde a /webhook/mercadopago; o resto devolve 404.
  Cadastre no Mercado Pago (Suas integrações > Webhooks) a URL:  https://<url-do-tunel>/webhook/mercadopago
#>
param(
    [switch]$Fixo,
    [string]$Dominio = "",
    [switch]$Jellyfin,
    [int]$Porta = 5099,
    [int]$PortaJellyfin = 8096
)
$ErrorActionPreference = "Stop"
$pasta = Join-Path $env:LOCALAPPDATA "NexusLS\cloudflared"
$exe = Join-Path $pasta "cloudflared.exe"

if (-not (Test-Path $exe)) {
    New-Item -ItemType Directory -Force $pasta | Out-Null
    Write-Host "Baixando o cloudflared (github.com/cloudflare/cloudflared)..."
    Invoke-WebRequest "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe" -OutFile $exe -UseBasicParsing
}
& $exe --version

if ($Jellyfin) {
    Write-Host "Túnel temporário do Jellyfin (porta $PortaJellyfin) em outra janela..."
    Start-Process $exe -ArgumentList "tunnel --url http://127.0.0.1:$PortaJellyfin"
}

if (-not $Fixo) {
    Write-Host "Túnel temporário do Pix. A URL https://....trycloudflare.com aparece abaixo; ela muda a cada execução."
    & $exe tunnel --url "http://127.0.0.1:$Porta"
    return
}

if (-not $Dominio) { throw "Informe -Dominio (ex.: pagamento.seudominio.com.br), um domínio já ativo na sua conta Cloudflare." }
if (-not (Test-Path (Join-Path $env:USERPROFILE ".cloudflared\cert.pem"))) { & $exe tunnel login }   # abre o navegador: autorize lá
$existe = (& $exe tunnel list 2>$null) -match "NexusPagamento"
if (-not $existe) { & $exe tunnel create NexusPagamento }
& $exe tunnel route dns NexusPagamento $Dominio
Write-Host "Webhook para cadastrar no Mercado Pago: https://$Dominio/webhook/mercadopago"
& $exe tunnel run --url "http://127.0.0.1:$Porta" NexusPagamento
