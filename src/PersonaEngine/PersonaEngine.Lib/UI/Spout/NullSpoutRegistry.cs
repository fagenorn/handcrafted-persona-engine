using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.Spout;

public class NullSpoutRegistry : SpoutRegistry
{
    public NullSpoutRegistry() : base(null, new SpoutConfiguration { Enabled = false }) { }

    public override void BeginFrame(string spoutName) { }
    public override void SendFrame(string spoutName) { }
    public override void ResizeAll(int width, int height) { }
    public override void Dispose() { }
    public override SpoutManager GetOrCreateManager(SpoutOutputConfigurations config) => null;
}