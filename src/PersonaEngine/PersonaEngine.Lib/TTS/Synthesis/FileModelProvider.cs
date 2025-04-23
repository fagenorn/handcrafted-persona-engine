using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Utils;

namespace PersonaEngine.Lib.TTS.Synthesis;

public class FileModelProvider : IModelProvider
{
    private readonly string _baseDirectory;

    private readonly ILogger<FileModelProvider> _logger;

    private readonly ConcurrentDictionary<ModelType, ModelResource> _modelCache = new();

    private bool _disposed;

    public FileModelProvider(string baseDirectory, ILogger<FileModelProvider> logger)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!Directory.Exists(_baseDirectory))
        {
            throw new DirectoryNotFoundException($"Model directory not found: {_baseDirectory}");
        }
    }

    public Task<ModelResource> GetModelAsync(
        ModelType modelType,
        CancellationToken cancellationToken = default
    )
    {
        if (_modelCache.TryGetValue(modelType, out var cachedModel))
        {
            return Task.FromResult(cachedModel);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var modelPath = GetModelPath(modelType);
            var model = new ModelResource(modelPath);

            _modelCache[modelType] = model;

            _logger.LogInformation("Loaded model {ModelType} from {Path}", modelType, modelPath);

            return Task.FromResult(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading model {ModelType}", modelType);

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var model in _modelCache.Values)
        {
            model.Dispose();
        }

        _modelCache.Clear();
        _disposed = true;

        await Task.CompletedTask;
    }

    public string GetModelPath(ModelType modelType)
    {
        var fullPath = Path.Combine(_baseDirectory, modelType.GetDescription());

        return fullPath;
    }
}
