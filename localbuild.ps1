Write-Host "=== LuminaVault Local Build ===" -ForegroundColor Cyan

Write-Host "`n[1/5] Stopping containers..." -ForegroundColor Yellow
docker compose down

Write-Host "`n[2/5] Pruning unused images..." -ForegroundColor Yellow
docker image prune -f

Write-Host "`n[3/5] Building images..." -ForegroundColor Yellow
docker compose build
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n[4/5] Starting containers..." -ForegroundColor Yellow
$env:HOST_MEDIA_PATH = "C:\Dev\Bilder"
Write-Host "HOST_MEDIA_PATH = $env:HOST_MEDIA_PATH" -ForegroundColor Gray
docker compose up -d

Write-Host "`n[5/5] Streaming logs (Ctrl+C to stop)..." -ForegroundColor Green
docker compose logs -f