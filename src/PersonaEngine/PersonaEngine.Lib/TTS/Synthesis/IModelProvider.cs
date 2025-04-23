namespace PersonaEngine.Lib.TTS.Synthesis;

public interface IModelProvider : IAsyncDisposable
{
    string GetModelPath(ModelType modelType);

    Task<ModelResource> GetModelAsync(
        ModelType modelType,
        CancellationToken cancellationToken = default
    );
}
