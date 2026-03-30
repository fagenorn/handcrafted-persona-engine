namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Model resource wrapper
/// </summary>
public class ModelResource : IDisposable
{
    private byte[]? _modelData;

    public ModelResource(string path)
    {
        Path = path;
    }

    /// <summary>
    ///     Path to the model
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Whether the model is loaded
    /// </summary>
    public bool IsLoaded => _modelData != null;

    public void Dispose()
    {
        _modelData = null;
    }

    /// <summary>
    ///     Gets the model data, loading it if necessary
    /// </summary>
    public async Task<byte[]> GetDataAsync()
    {
        if (_modelData == null)
        {
            _modelData = await File.ReadAllBytesAsync(Path);
        }

        return _modelData;
    }
}
