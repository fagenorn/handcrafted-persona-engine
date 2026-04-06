namespace PersonaEngine.Lib.IO;

/// <summary>
///     Identifies a model resource by its path relative to the model base directory.
/// </summary>
public readonly record struct ModelId(string RelativePath)
{
    public override string ToString() => RelativePath;
}
