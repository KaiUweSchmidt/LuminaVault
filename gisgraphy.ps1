<#
.SYNOPSIS
    Verwaltet die Gisgraphy-Container (DB + Server + OSM-Import).

.DESCRIPTION
    Dieses Script steuert die Gisgraphy-Infrastruktur unabhaengig vom
    normalen App-Build (localbuild.ps1).

.PARAMETER Action
    start    - Startet gisgraphy-db und gisgraphy
    stop     - Stoppt gisgraphy und gisgraphy-db
    restart  - Stop + Start
    import   - Startet den OSM-Datenimport (Deutschland, ~4 GB Download)
    status   - Zeigt den Status der Gisgraphy-Container
    logs     - Streamt die Gisgraphy-Logs

.EXAMPLE
    .\gisgraphy.ps1 start
    .\gisgraphy.ps1 import
    .\gisgraphy.ps1 status
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet("start", "stop", "restart", "import", "status", "logs")]
    [string]$Action
)

$gisgraphyServices = @("gisgraphy-db", "gisgraphy")

function Start-Gisgraphy {
    Write-Host "`nGisgraphy-Container starten..." -ForegroundColor Cyan
    docker compose up -d @gisgraphyServices
    Write-Host "Warte auf Healthcheck (kann bis zu 2 Minuten dauern)..." -ForegroundColor Gray

    $timeout = 180
    $elapsed = 0
    while ($elapsed -lt $timeout) {
        $health = docker inspect --format '{{.State.Health.Status}}' luminavault-gisgraphy-1 2>$null
        if ($health -eq "healthy") {
            Write-Host "Gisgraphy ist bereit." -ForegroundColor Green
            return
        }
        Start-Sleep -Seconds 5
        $elapsed += 5
        Write-Host "  Warte... ($elapsed s / $timeout s) Status: $health" -ForegroundColor Gray
    }
    Write-Host "Timeout: Gisgraphy ist nach $timeout s nicht healthy geworden." -ForegroundColor Yellow
    Write-Host "Pruefen mit: docker compose logs gisgraphy" -ForegroundColor Yellow
}

function Stop-Gisgraphy {
    Write-Host "`nGisgraphy-Container stoppen..." -ForegroundColor Cyan
    docker compose stop @gisgraphyServices
    Write-Host "Gestoppt. Volumes bleiben erhalten." -ForegroundColor Green
}

function Import-GisgraphyData {
    Write-Host "`n=== Gisgraphy OSM-Import ===" -ForegroundColor Cyan
    Write-Host "Dies laedt die Deutschland-OSM-Daten (~4 GB) und startet den Import." -ForegroundColor Gray
    Write-Host "Der Import selbst kann mehrere Stunden dauern.`n" -ForegroundColor Gray

    # Sicherstellen, dass Gisgraphy laeuft
    $health = docker inspect --format '{{.State.Health.Status}}' luminavault-gisgraphy-1 2>$null
    if ($health -ne "healthy") {
        Write-Host "Gisgraphy laeuft nicht oder ist nicht healthy. Starte zuerst..." -ForegroundColor Yellow
        Start-Gisgraphy
    }

    Write-Host "`nImporter starten..." -ForegroundColor Cyan
    docker compose --profile import up --no-deps gisgraphy-importer

    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nImport wurde angestossen." -ForegroundColor Green
        Write-Host "Fortschritt pruefen: docker compose logs -f gisgraphy" -ForegroundColor Gray
    } else {
        Write-Host "`nImporter fehlgeschlagen! (Exit Code: $LASTEXITCODE)" -ForegroundColor Red
    }
}

function Show-Status {
    Write-Host "`nGisgraphy-Status:" -ForegroundColor Cyan
    docker compose ps gisgraphy-db gisgraphy 2>$null

    $health = docker inspect --format '{{.State.Health.Status}}' luminavault-gisgraphy-1 2>$null
    if ($health) {
        Write-Host "`nGisgraphy Healthcheck: $health" -ForegroundColor $(if ($health -eq "healthy") { "Green" } else { "Yellow" })
    } else {
        Write-Host "`nGisgraphy-Container nicht gefunden." -ForegroundColor Red
    }
}

switch ($Action) {
    "start"   { Start-Gisgraphy }
    "stop"    { Stop-Gisgraphy }
    "restart" { Stop-Gisgraphy; Start-Gisgraphy }
    "import"  { Import-GisgraphyData }
    "status"  { Show-Status }
    "logs"    { docker compose logs -f @gisgraphyServices }
}
