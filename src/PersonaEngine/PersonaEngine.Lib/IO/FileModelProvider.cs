using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.IO;

/// <summary>
///     Resolves model paths by combining a base directory with the <see cref="ModelId.RelativePath" />.
/// </summary>
public sealed class FileModelProvider(string baseDirectory, ILogger<FileModelProvider> logger)
    : IModelProvider
{
    private readonly string _baseDirectory = Directory.Exists(baseDirectory)
        ? baseDirectory
        : throw new DirectoryNotFoundException($"Model directory not found: {baseDirectory}");

    public string GetModelPath(ModelId modelId) =>
        Path.Combine(_baseDirectory, modelId.RelativePath);
}
