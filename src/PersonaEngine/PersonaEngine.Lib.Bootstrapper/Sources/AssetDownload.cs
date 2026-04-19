using PersonaEngine.Lib.Bootstrapper.Manifest;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public sealed record AssetDownload(
    Uri Url,
    long ExpectedSize,
    string ExpectedSha256,
    Func<Stream, CancellationToken, Task>? PostProcess
);
