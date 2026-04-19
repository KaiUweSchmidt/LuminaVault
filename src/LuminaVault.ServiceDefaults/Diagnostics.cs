using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LuminaVault.ServiceDefaults;

/// <summary>
/// Zentrale OpenTelemetry-Diagnostik-Konfiguration für alle LuminaVault-Services.
/// Stellt gemeinsame ActivitySources und Meters bereit.
/// </summary>
public static class Diagnostics
{
    public const string ServiceName = "LuminaVault";

    // ActivitySource für benutzerdefinierte Traces über alle Services hinweg
    public static readonly ActivitySource ActivitySource = new(ServiceName);

    // Meter für benutzerdefinierte Metriken
    public static readonly Meter Meter = new(ServiceName);

    // Startup-spezifische Instrumentierung
    public static readonly ActivitySource StartupActivitySource = new($"{ServiceName}.Startup");

    // HTTP-Client-spezifische Instrumentierung
    public static readonly ActivitySource HttpClientActivitySource = new($"{ServiceName}.HttpClient");

    // Histogramm für Startup-Dauer
    private static readonly Histogram<double> s_startupDuration = Meter.CreateHistogram<double>(
        "luminavault.startup.duration",
        unit: "ms",
        description: "Dauer der Startup-Phasen in Millisekunden");

    // Counter für Startup-Fehler
    private static readonly Counter<long> s_startupErrors = Meter.CreateCounter<long>(
        "luminavault.startup.errors",
        description: "Anzahl der Fehler während des Startups");

    // Counter für HTTP-Anfragen
    private static readonly Counter<long> s_httpRequests = Meter.CreateCounter<long>(
        "luminavault.http.requests",
        description: "Anzahl der ausgehenden HTTP-Anfragen");

    /// <summary>
    /// Startet eine Activity für eine Startup-Phase und gibt sie zurück.
    /// </summary>
    public static Activity? StartStartupActivity(string phaseName, ActivityKind kind = ActivityKind.Internal)
    {
        var activity = StartupActivitySource.StartActivity(phaseName, kind);
        activity?.SetTag("service.phase", phaseName);
        return activity;
    }

    /// <summary>
    /// Zeichnet die Dauer einer Startup-Phase auf.
    /// </summary>
    public static void RecordStartupDuration(string phase, double durationMs)
    {
        s_startupDuration.Record(durationMs, new KeyValuePair<string, object?>("phase", phase));
    }

    /// <summary>
    /// Zeichnet einen Startup-Fehler auf.
    /// </summary>
    public static void RecordStartupError(string phase, Exception? exception = null)
    {
        s_startupErrors.Add(1,
            new KeyValuePair<string, object?>("phase", phase),
            new KeyValuePair<string, object?>("error.type", exception?.GetType().Name ?? "unknown"));
    }

    /// <summary>
    /// Zeichnet eine HTTP-Anfrage auf.
    /// </summary>
    public static void RecordHttpRequest(string method, string url, int statusCode)
    {
        s_httpRequests.Add(1,
            new KeyValuePair<string, object?>("http.method", method),
            new KeyValuePair<string, object?>("http.url", url),
            new KeyValuePair<string, object?>("http.status_code", statusCode));
    }
}
