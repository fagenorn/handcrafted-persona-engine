namespace PersonaEngine.Lib.Configuration;

public record LlmOptions
{
    public string TextApiKey { get; set; } = string.Empty;

    public string VisionApiKey { get; set; } = string.Empty;

    public string TextModel { get; set; } = string.Empty;

    public string VisionModel { get; set; } = string.Empty;

    public string TextEndpoint { get; set; } = string.Empty;

    public string VisionEndpoint { get; set; } = string.Empty;
}
