namespace PersonaEngine.Lib.UI.ControlPanel;

public interface IConfigWriter : IDisposable
{
    void Write<T>(T options)
        where T : notnull;

    DateTime? LastSaveTime { get; }

    /// <summary>
    ///     Synchronously drains any pending debounced writes to disk.
    /// </summary>
    void Flush();
}
