# Gera o pacote do editor/catálogo em JSX e o coloca dentro do programa.
# Rode sempre que mexer em web\catalogo\src, antes de compilar o NexusLS.App:  .\web\construir.ps1
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$web = Join-Path $PSScriptRoot 'catalogo'
$zip = Join-Path $raiz 'src\NexusLS.App\Web\catalogo.zip'

Push-Location $web
try {
    if (-not (Test-Path 'node_modules')) { npm ci; if ($LASTEXITCODE -ne 0) { throw 'npm ci falhou' } }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'npm run build falhou' }
} finally { Pop-Location }

New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $web 'dist\*') -DestinationPath $zip
Write-Host "Pacote gerado: $zip"
