<#
.SYNOPSIS
    Verwaltet den Nominatim-Container (Reverse-Geocoding mit OSM-Daten).

.DESCRIPTION
    Dieses Script steuert die Nominatim-Infrastruktur unabhaengig vom
    normalen App-Build (localbuild.ps1).

    Nominatim importiert die germany-latest.osm.pbf automatisch beim
    ersten Start (PBF_PATH in docker-compose.yml). Der initiale Import
    dauert je nach Hardware mehrere Stunden.

.PARAMETER Action
    start    - Startet den Nominatim-Container
    stop     - Stoppt den Nominatim-Container
    restart  - Stop + Start
    reset    - Loescht Volumes komplett und startet sauber neu (Reimport!)
    status   - Zeigt den Status des Containers
    logs     - Streamt die Nominatim-Logs
    test     - Testet Reverse-Geocoding mit Muenchen-Koordinaten

.EXAMPLE
    .\nominatim.ps1 start
    .\nominatim.ps1 status
    .\nominatim.ps1 test
    .\nominatim.ps1 logs
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet("start", "stop", "restart", "reset", "status", "logs", "test")]
    [string]$Action
)

function Start-Nominatim {
    Write-Host "`nNominatim-Container starten..." -ForegroundColor Cyan
    Write-Host "Beim ersten Start wird germany-latest.osm.pbf aus C:\Dev\OpenStreetMapData importiert." -ForegroundColor Gray
    Write-Host "Das kann mehrere Stunden dauern. Fortschritt mit: .\nominatim.ps1 logs`n" -ForegroundColor Gray
    docker compose up -d nominatim

    $timeout = 60
    $elapsed = 0
    while ($elapsed -lt $timeout) {
        $health = docker inspect --format '{{.State.Health.Status}}' luminavault-nominatim-1 2>$null
        if ($health -eq "healthy") {
            Write-Host "Nominatim ist bereit." -ForegroundColor Green
            return
        }
        Start-Sleep -Seconds 5
        $elapsed += 5
        Write-Host "  Warte... ($elapsed s) Status: $health" -ForegroundColor Gray
    }
    Write-Host "Nominatim laeuft, aber Healthcheck noch nicht healthy." -ForegroundColor Yellow
    Write-Host "Beim ersten Start ist das normal (Import laeuft)." -ForegroundColor Yellow
    Write-Host "Fortschritt pruefen: .\nominatim.ps1 logs" -ForegroundColor Gray
}

function Stop-Nominatim {
    Write-Host "`nNominatim-Container stoppen..." -ForegroundColor Cyan
    docker compose stop nominatim
    Write-Host "Gestoppt. Volumes bleiben erhalten (kein Reimport noetig)." -ForegroundColor Green
}

function Reset-Nominatim {
    Write-Host "`n=== Nominatim zuruecksetzen ===" -ForegroundColor Red
    Write-Host "ACHTUNG: Loescht alle Nominatim-Daten!" -ForegroundColor Red
    Write-Host "Die germany-latest.osm.pbf aus C:\Dev\OpenStreetMapData wird erneut importiert.`n" -ForegroundColor Yellow

    $confirm = Read-Host "Fortfahren? (j/n)"
    if ($confirm -ne "j") {
        Write-Host "Abgebrochen." -ForegroundColor Gray
        return
    }

    Write-Host "`nContainer stoppen und entfernen..." -ForegroundColor Cyan
    docker compose stop nominatim
    docker compose rm -f nominatim

    Write-Host "Nominatim-Volumes loeschen..." -ForegroundColor Cyan
    docker volume rm luminavault_nominatim-data 2>$null
    docker volume rm luminavault_nominatim-flatnode 2>$null
    docker volume rm luminavault_nominatim-progress 2>$null

    Write-Host "Sauber neu starten..." -ForegroundColor Cyan
    Start-Nominatim
}

function Test-Nominatim {
    Write-Host "`nTeste Reverse-Geocoding (Muenchen, 48.1351, 11.5820)..." -ForegroundColor Cyan
    $result = docker exec luminavault-nominatim-1 curl -sf "http://localhost:8080/reverse?lat=48.1351&lon=11.5820&format=jsonv2&accept-language=de" 2>$null
    if ($result) {
        Write-Host "Ergebnis:" -ForegroundColor Green
        Write-Host $result | ConvertFrom-Json | ConvertTo-Json -Depth 5
    } else {
        Write-Host "Keine Antwort. Nominatim ist noch nicht bereit oder nicht gestartet." -ForegroundColor Yellow
        Write-Host "Starten mit: .\nominatim.ps1 start" -ForegroundColor Gray
    }
}

function Show-Status {
    Write-Host "`nNominatim-Status:" -ForegroundColor Cyan
    docker compose ps nominatim 2>$null

    $health = docker inspect --format '{{.State.Health.Status}}' luminavault-nominatim-1 2>$null
    if ($health) {
        Write-Host "`nHealthcheck: $health" -ForegroundColor $(if ($health -eq "healthy") { "Green" } else { "Yellow" })
    } else {
        Write-Host "`nNominatim-Container nicht gefunden." -ForegroundColor Red
        Write-Host "Starten mit: .\nominatim.ps1 start" -ForegroundColor Gray
    }
}

switch ($Action) {
    "start"   { Start-Nominatim }
    "stop"    { Stop-Nominatim }
    "restart" { Stop-Nominatim; Start-Nominatim }
    "reset"   { Reset-Nominatim }
    "status"  { Show-Status }
    "logs"    { docker compose logs -f nominatim }
    "test"    { Test-Nominatim }
}
