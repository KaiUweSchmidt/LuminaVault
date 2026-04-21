using LuminaVault.WebUI.Settings;
using Microsoft.Extensions.Options;

namespace LuminaVault.WebUI.Services;

/// <summary>
/// Singleton service that holds the container-internal media path used by the
/// batch import feature. The path corresponds to a Docker bind-mount that maps
/// a directory on the host machine into the WebUI container.
///
/// The value is seeded from <see cref="ImportSettings.HostMediaPath"/> at startup
/// and can be changed at runtime via the Settings page; the change takes effect
/// immediately for the running instance (no container restart required).
///
/// To persist the path across container restarts, set the environment variable
/// <c>Import__HostMediaPath</c> in docker-compose.yml and add the corresponding
/// bind-mount to the <c>webui</c> service.
/// </summary>
public sealed class HostMediaPathService(IOptions<ImportSettings> options)
{
    private readonly object _lock = new();
    private string _hostMediaPath = options.Value.HostMediaPath;

    /// <summary>
    /// Current container-internal path for the host media mount.
    /// </summary>
    public string HostMediaPath
    {
        get { lock (_lock) { return _hostMediaPath; } }
        set
        {
            var newValue = string.IsNullOrWhiteSpace(value) ? options.Value.HostMediaPath : value.Trim();
            lock (_lock) { _hostMediaPath = newValue; }
        }
    }

    /// <summary>
    /// Returns true when the configured path exists inside the container,
    /// which indicates the Docker bind-mount has been set up correctly.
    /// </summary>
    public bool IsMounted => Directory.Exists(HostMediaPath);
}
