namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Interface for model loading and management
/// </summary>
public interface IModelProvider : IAsyncDisposable
{
    string GetModelPath(ModelType modelType);
    
    Task<ModelResource> GetModelAsync(
        ModelType         modelType,
        CancellationToken cancellationToken = default);
}