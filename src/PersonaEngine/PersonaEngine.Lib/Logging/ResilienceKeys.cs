using Microsoft.Extensions.Logging;

using Polly;

namespace PersonaEngine.Lib.Logging;

public static class ResilienceKeys
{
    public static readonly ResiliencePropertyKey<Guid> SessionId = new("session-id");
    
    public static readonly ResiliencePropertyKey<ILogger> Logger = new("logger");
}