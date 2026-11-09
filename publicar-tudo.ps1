<#
.SYNOPSIS
  Compila TODO o Nexus (PDV, Atualizador, Etiquetas) + o instalador Nexus-Setup.exe e publica tudo num release.
  (O License Manager não entra: ele é só do dono.)

.EXAMPLE
  .\publicar-tudo.ps1 -Versao 1.2.4 -Notas "- Instalador Nexus-Setup.exe"
  .\publicar-tudo.ps1 -Versao 1.2.4 -Notas "teste" -SoGerar     # monta tudo, não envia nada
#>
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [Parameter(Mandatory = $true)][string]$Notas,
    [string]$Repositorio = "Lukkzz11/Nexus-LS---Update",
    [switch]$SoGerar
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$Versao = $Versao.Trim().TrimStart("v", "V")
if ($Versao -notmatch '^\d+\.\d+\.\d+$') { throw "Versão inválida: $Versao" }
if (git status --porcelain) { throw "Há alterações sem commit. Commite antes (a versão precisa corresponder ao código)." }

& (Join-Path $PSScriptRoot "web\construir.ps1")
if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o editor do catálogo." }

$saida = Join-Path $PSScriptRoot "publicar_release"
if (Test-Path $saida) { Remove-Item $saida -Recurse -Force }
New-Item -ItemType Directory $saida | Out-Null

# projeto, nome do exe final
$projetos = @(
    @{ P = "src/NexusLS.App";        Exe = "NexusLS.exe" },
    @{ P = "src/NexusLS.Atualizador"; Exe = "atualizador.exe" },
    @{ P = "src/NexusLS.BarTender";  Exe = "NexusBarTender.exe" },
    @{ P = "src/NexusLS.Setup";      Exe = "Nexus-Setup.exe" }
)
foreach ($p in $projetos) {
    Write-Host "==> $($p.Exe) $Versao" -ForegroundColor Cyan
    $tmp = Join-Path $saida ("_" + [IO.Path]::GetFileNameWithoutExtension($p.Exe))
    dotnet publish $p.P -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Versao -p:InformationalVersion=$Versao -o $tmp -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar $($p.Exe)" }
    $exe = Join-Path $tmp $p.Exe
    if (-not (Test-Path $exe)) { throw "$($p.Exe) não foi gerado." }
    Move-Item $exe (Join-Path $saida $p.Exe)
    Remove-Item $tmp -Recurse -Force
    $hash = (Get-FileHash (Join-Path $saida $p.Exe) -Algorithm SHA256).Hash.ToLower()
    [IO.File]::WriteAllText((Join-Path $saida ($p.Exe + ".sha256")), "$hash  $($p.Exe)`n", (New-Object Text.UTF8Encoding($false)))
}
$arquivoNotas = Join-Path $saida "notas.md"
[IO.File]::WriteAllText($arquivoNotas, $Notas.Trim() + "`n", (New-Object Text.UTF8Encoding($false)))
Get-ChildItem $saida | ForEach-Object { "{0,-34} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
if ($SoGerar) { Write-Host "Pronto (modo -SoGerar): nada foi enviado. Arquivos em $saida"; exit 0 }

$arquivos = $projetos | ForEach-Object { Join-Path $saida $_.Exe; Join-Path $saida ($_.Exe + ".sha256") }
gh release create "v$Versao" @arquivos --repo $Repositorio --title "Nexus $Versao" --notes-file $arquivoNotas
if ($LASTEXITCODE -ne 0) { throw "O envio ao GitHub falhou." }
git tag -a "v$Versao" -m "Nexus $Versao" 2>$null
Write-Host "Publicado: v$Versao (inclui Nexus-Setup.exe)" -ForegroundColor Green
