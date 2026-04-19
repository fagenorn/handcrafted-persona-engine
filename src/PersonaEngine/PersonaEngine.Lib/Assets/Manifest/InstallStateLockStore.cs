using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PersonaEngine.Lib.Assets.Manifest;

public sealed class InstallStateLockStore
{
    private readonly string _path;
    private readonly ILogger _logger;

    public InstallStateLockStore(string path, ILogger? logger = null)
    {
        _path = path;
        _logger = logger ?? NullLogger.Instance;
    }

    public InstallStateLock Read(string fallbackManifestVersion)
    {
        if (!File.Exists(_path))
            return InstallStateLock.Empty(fallbackManifestVersion);

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<InstallStateLock>(json, ManifestLoader.JsonOptions)
                ?? InstallStateLock.Empty(fallbackManifestVersion);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "install-state.lock.json is corrupt; treating as empty install state"
            );
            return InstallStateLock.Empty(fallbackManifestVersion);
        }
    }

    public void Write(InstallStateLock state)
    {
        var json = JsonSerializer.Serialize(state, ManifestLoader.JsonOptions);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        // Atomic rename — works across Windows + POSIX.
        File.Move(tmp, _path, overwrite: true);
    }
}
