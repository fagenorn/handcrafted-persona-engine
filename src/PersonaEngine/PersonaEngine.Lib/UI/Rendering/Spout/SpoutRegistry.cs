using PersonaEngine.Lib.Configuration;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Rendering.Spout;

public class SpoutRegistry : IDisposable
{
    private readonly SpoutConfiguration[] _configs;

    private readonly GL _gl;

    private readonly Dictionary<string, SpoutManager> _spoutManagers = new();

    public SpoutRegistry(GL gl, SpoutConfiguration[] configs)
    {
        _gl = gl;
        _configs = configs;

        foreach (var config in _configs)
        {
            GetOrCreateManager(config);
        }
    }

    public void Dispose()
    {
        foreach (var manager in _spoutManagers.Values)
        {
            manager.Dispose();
        }

        _spoutManagers.Clear();
    }

    public SpoutManager GetOrCreateManager(SpoutConfiguration config)
    {
        if (!_spoutManagers.TryGetValue(config.OutputName, out var manager))
        {
            manager = new SpoutManager(_gl, config);
            _spoutManagers.Add(config.OutputName, manager);
        }

        return manager;
    }

    /// <summary>
    ///     Toggles the external Spout sender for the named target. Turning it
    ///     off stops publishing to OBS and other external receivers (including
    ///     the overlay window, which receives via the same Spout sender path).
    /// </summary>
    public void SetSenderEnabled(string spoutName, bool enabled)
    {
        if (
            !string.IsNullOrEmpty(spoutName)
            && _spoutManagers.TryGetValue(spoutName, out var manager)
        )
        {
            manager.SetSenderEnabled(enabled);
        }
    }

    public void BeginFrame(string spoutName)
    {
        if (
            string.IsNullOrEmpty(spoutName)
            || !_spoutManagers.TryGetValue(spoutName, out var manager)
        )
        {
            return;
        }

        manager.BeginFrame();
    }

    public void SendFrame(string spoutName)
    {
        if (
            string.IsNullOrEmpty(spoutName)
            || !_spoutManagers.TryGetValue(spoutName, out var manager)
        )
        {
            return;
        }

        manager.SendFrame();
    }

    public void ResizeAll(int width, int height)
    {
        foreach (var manager in _spoutManagers.Values)
        {
            manager.ResizeFramebuffer(width, height);
        }
    }
}
