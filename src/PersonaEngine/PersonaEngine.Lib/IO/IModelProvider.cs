namespace PersonaEngine.Lib.IO;

/// <summary>
///     Resolves model resource paths from well-known <see cref="ModelId" /> identifiers.
/// </summary>
public interface IModelProvider
{
    string GetModelPath(ModelId modelId);
}
