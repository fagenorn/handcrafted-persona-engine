using PersonaEngine.Lib.Configuration;

using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.Spout;

public class SpoutRegistry : IDisposable
{
    private readonly SpoutConfiguration _configs;

    private readonly GL _gl;

    private readonly Dictionary<string, SpoutManager> _spoutManagers = new();

    public SpoutRegistry(GL gl, SpoutConfiguration configs)
    {
        _gl      = gl;
        _configs = configs;

        foreach ( var config in _configs.Outputs )
        {
            GetOrCreateManager(config);
        }
    }

    public virtual void Dispose()
    {
        foreach ( var manager in _spoutManagers.Values )
        {
            manager.Dispose();
        }

        _spoutManagers.Clear();
    }

    public virtual SpoutManager GetOrCreateManager(SpoutOutputConfigurations config)
    {
        if ( !_spoutManagers.TryGetValue(config.Name, out var manager) )
        {
            manager = new SpoutManager(_gl, config);
            _spoutManagers.Add(config.Name, manager);
        }

        return manager;
    }

    public virtual void BeginFrame(string spoutName)
    {
        if ( string.IsNullOrEmpty(spoutName) || !_spoutManagers.TryGetValue(spoutName, out var manager) )
        {
            return;
        }

        manager.BeginFrame();
    }

    public virtual void SendFrame(string spoutName)
    {
        if ( string.IsNullOrEmpty(spoutName) || !_spoutManagers.TryGetValue(spoutName, out var manager) )
        {
            return;
        }

        manager.SendFrame();
    }

    public virtual void ResizeAll(int width, int height)
    {
        foreach ( var manager in _spoutManagers.Values )
        {
            manager.ResizeFramebuffer(width, height);
        }
    }
}