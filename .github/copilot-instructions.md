# LuminaVault – Copilot Instructions

## Projektübersicht

LuminaVault ist eine Microservice-basierte Foto-/Medienverwaltung, gebaut mit **.NET 10** und **Blazor Server-Side**.

### Architektur

| Projekt | Rolle |
|---|---|
| `LuminaVault.WebUI` | Blazor-Frontend (SSR) |
| `LuminaVault.ApiGateway` | YARP Reverse Proxy |
| `LuminaVault.MediaImport` | Medien-Import-Service |
| `LuminaVault.ObjectRecognition` | Bilderkennung |
| `LuminaVault.FaceRecognition` | Gesichtserkennung |
| `LuminaVault.ThumbnailGeneration` | Thumbnail-Erzeugung |
| `LuminaVault.MetadataStorage` | Metadaten-Persistenz |
| `LuminaVault.VectorSearch` | Vektorbasierte Suche |
| `LuminaVault.GeocodingService` | Geocoding / Reverse-Geocoding |
| `LuminaVault.ServiceDefaults` | Shared Defaults (Logging, Telemetry etc.) |

### Messaging

- **NATS** wird für Service-Kommunikation verwendet (`NATS.Net`).

### Projektstruktur

```
src/          – Produktivcode
tests/        – Testprojekte (Spiegel der src-Struktur)
```

## Technologie-Stack

- **.NET 10** / C# (latest)
- **Blazor Server-Side** (kein Razor Pages / MVC)
- **YARP** als API-Gateway
- **NATS** für Messaging
- **SixLabors.ImageSharp** für Bildverarbeitung
- **Entity Framework Core** (InMemory für Tests)
- **xUnit v3** + **NSubstitute** für Tests
- Nullable Reference Types: **aktiviert**
- Implicit Usings: **aktiviert**

## Coding-Konventionen

- Sprache im Code: **Englisch** (Variablen, Klassen, Methoden)
- Kommentare & Commit-Messages: **Deutsch** ist akzeptiert
- File-scoped namespaces verwenden
- Records für DTOs bevorzugen
- Async/Await durchgängig; Methoden mit `Async`-Suffix benennen
- `ArgumentNullException.ThrowIfNull()` für Null-Checks
- Keine stillen Exception-Catches
- Minimale Sichtbarkeit: `private` > `internal` > `public`

## Blazor-spezifisch

- **Blazor Server-Side** bevorzugen – keine Razor Pages oder MVC-Patterns vorschlagen
- Komponenten in `.razor`-Dateien, Code-Behind in `.razor.cs`
- `@inject` für Dependency Injection in Komponenten
- `NavigationManager` für Navigation
- `IJSRuntime` nur wenn nötig

## Testing

- **xUnit v3** mit `[Fact]` und `[Theory]` / `[InlineData]`
- **NSubstitute** für Mocking externer Abhängigkeiten
- **EF Core InMemory** für Datenbank-Tests
- Testprojekte liegen unter `tests/` und heißen `[Projektname].Tests`
- Testklassen spiegeln die Produktivklassen
- Testnamen beschreiben Verhalten: `WhenConditionThenExpectedResult`
- AAA-Pattern (Arrange-Act-Assert)
- Ein Verhalten pro Test

## Build & Run

```bash
dotnet build
dotnet test
```
