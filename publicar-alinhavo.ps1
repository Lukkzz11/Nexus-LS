<#
.SYNOPSIS
  Gera o pacote do Alinhavo (Moldes e costura 3D) para o instalador do Nexus Hub.
  NAO envia nada para clientes, GitHub ou nuvem: so compila e monta arquivos locais.

.DESCRIPTION
  1. Compila `cargo build -p atl-app --release` em C:\Alinhavo (o exe sai como alinhavo.exe).
  2. Monta publicar\Alinhavo\ com alinhavo.exe + assets\manequim\*.glb + versao.txt
     (a versao vem do Cargo.toml: crate atl-app ou [workspace.package]).
  3. Compacta em publicar\Alinhavo.zip e copia para src\NexusLS.Setup\Pacotes\Alinhavo.zip,
     de onde o Nexus-Setup.exe embute o pacote (publicar-tudo.ps1 chama este script antes de
     compilar o instalador). O outro PC nao precisa de Rust.

.EXAMPLE
  .\publicar-alinhavo.ps1
  .\publicar-alinhavo.ps1 -PularCompilacao      # reaproveita o alinhavo.exe ja compilado
#>
param(
    [string]$PastaAlinhavo = "C:\Alinhavo",
    [switch]$PularCompilacao
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$saida = Join-Path $root "publicar"
$pasta = Join-Path $saida "Alinhavo"
$zip = Join-Path $saida "Alinhavo.zip"
$pacotesSetup = Join-Path $root "src\NexusLS.Setup\Pacotes"

if (-not (Test-Path (Join-Path $PastaAlinhavo "Cargo.toml"))) { throw "Projeto Alinhavo nao encontrado em $PastaAlinhavo (Cargo.toml ausente)." }

# --- versao: crate atl-app; se herdar do workspace, usa [workspace.package] ---
function Ler-Versao([string]$arquivo, [string]$secao) {
    $dentro = $false
    foreach ($linha in Get-Content $arquivo) {
        if ($linha -match '^\s*\[(.+)\]\s*$') { $dentro = ($Matches[1] -eq $secao); continue }
        if ($dentro -and $linha -match '^\s*version\s*=\s*"([^"]+)"') { return $Matches[1] }
    }
    return $null
}
$versao = Ler-Versao (Join-Path $PastaAlinhavo "crates\atl-app\Cargo.toml") "package"
if (-not $versao) { $versao = Ler-Versao (Join-Path $PastaAlinhavo "Cargo.toml") "workspace.package" }
if (-not $versao) { throw "Nao consegui ler a versao do Cargo.toml do Alinhavo." }

# --- compilacao ---
Get-Process -Name "alinhavo" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$exeOrigem = Join-Path $PastaAlinhavo "target\release\alinhavo.exe"
if (-not $PularCompilacao) {
    Write-Host "==> Compilando Alinhavo $versao (cargo build -p atl-app --release)..." -ForegroundColor Cyan
    Push-Location $PastaAlinhavo
    try { cargo build -p atl-app --release; if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar o Alinhavo." } }
    finally { Pop-Location }
}
if (-not (Test-Path $exeOrigem)) { throw "alinhavo.exe nao encontrado em $exeOrigem" }

$glbs = Get-ChildItem (Join-Path $PastaAlinhavo "assets\manequim") -Filter *.glb -ErrorAction SilentlyContinue
if (-not $glbs) { throw "Nenhum manequim (.glb) em $PastaAlinhavo\assets\manequim" }

# --- montagem do pacote ---
Write-Host "==> Montando pacote em $pasta" -ForegroundColor Cyan
if (Test-Path $pasta) { Remove-Item $pasta -Recurse -Force }
New-Item -ItemType Directory (Join-Path $pasta "assets\manequim") -Force | Out-Null
Copy-Item $exeOrigem (Join-Path $pasta "alinhavo.exe")
foreach ($g in $glbs) { Copy-Item $g.FullName (Join-Path $pasta "assets\manequim\$($g.Name)") }
[IO.File]::WriteAllText((Join-Path $pasta "versao.txt"), $versao + "`n", (New-Object Text.UTF8Encoding($false)))

# --- zip (o instalador embute este arquivo) ---
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($pasta, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
New-Item -ItemType Directory $pacotesSetup -Force | Out-Null
Copy-Item $zip (Join-Path $pacotesSetup "Alinhavo.zip") -Force

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "==> Pronto: Alinhavo $versao ($mb MB)" -ForegroundColor Green
Write-Host "    Pasta : $pasta"
Write-Host "    Zip   : $zip"
Write-Host "    Setup : $(Join-Path $pacotesSetup 'Alinhavo.zip') (sera embutido no Nexus-Setup.exe)"
Write-Host "    Nada foi enviado para clientes ou para a nuvem." -ForegroundColor DarkGray
