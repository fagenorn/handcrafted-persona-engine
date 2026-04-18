namespace PersonaEngine.Lib.LLM.Connection;

/// <summary>
///     Which LLM endpoint a probe / provider operation targets. The engine runs
///     two independent channels: text-only chat and vision-capable chat.
/// </summary>
public enum LlmChannel
{
    Text,
    Vision,
}
