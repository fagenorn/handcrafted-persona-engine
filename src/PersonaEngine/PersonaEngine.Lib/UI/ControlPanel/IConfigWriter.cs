namespace PersonaEngine.Lib.UI.ControlPanel;

public interface IConfigWriter : IDisposable
{
    void Write<T>(T options)
        where T : notnull;

    DateTime? LastSaveTime { get; }
}
