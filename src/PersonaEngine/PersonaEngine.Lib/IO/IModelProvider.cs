using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.IO;

public interface IModelProvider : IAsyncDisposable
{
    string GetModelPath(ModelType modelType);

    Task<ModelResource> GetModelAsync(
        ModelType modelType,
        CancellationToken cancellationToken = default
    );
}
