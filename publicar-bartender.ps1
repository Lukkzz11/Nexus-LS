<#
.SYNOPSIS
  Publica o Nexus BarTender diretamente para a pasta C:\NexusLS\publicar, que alimenta o atalho da Área de Trabalho.
#>
param()

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$projeto = Join-Path $root "src\NexusLS.BarTender\NexusLS.BarTender.csproj"
$saida = Join-Path $root "publicar"

Write-Host "==> Publicando Nexus BarTender (win-x64, single-file)..." -ForegroundColor Cyan

# Fechar processos ativos se houver para não travar o arquivo
Get-Process -Name "NexusBarTender" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

dotnet publish $projeto -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $saida -v q --nologo

if ($LASTEXITCODE -eq 0) {
    $exe = Join-Path $saida "NexusBarTender.exe"
    $item = Get-Item $exe
    Write-Host "==> Sucesso! BarTender atualizado na Área de Trabalho: $exe ($([math]::Round($item.Length / 1MB, 1)) MB)" -ForegroundColor Green
} else {
    Write-Host "==> Falha ao publicar Nexus BarTender." -ForegroundColor Red
    exit 1
}
