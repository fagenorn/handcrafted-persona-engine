using PersonaEngine.Lib.Bootstrapper.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>UI copy for a single profile tier shown in the installer picker.</summary>
public sealed record ProfileChoiceEntry(
    ProfileTier Tier,
    string Title,
    string SizeLabel,
    string Tagline,
    IReadOnlyList<string> Bullets
);

/// <summary>
/// Static catalog of profile picker copy. Source of truth for titles, size labels,
/// taglines, and feature bullet points shown to the user during first-run setup.
/// </summary>
public static class ProfileChoiceCatalog
{
    public static IReadOnlyList<ProfileChoiceEntry> All { get; } =
        new ProfileChoiceEntry[]
        {
            new(
                Tier: ProfileTier.TryItOut,
                Title: "Try It Out",
                SizeLabel: "~2 GB",
                Tagline: "Spin up a talking avatar in minutes — no GPU required for the basics.",
                Bullets: new[]
                {
                    "Core TTS voice (Kokoro)",
                    "Default Live2D avatar",
                    "Text chat via any OpenAI-compatible endpoint",
                    "Subtitle rendering",
                }
            ),
            new(
                Tier: ProfileTier.StreamWithIt,
                Title: "Stream With It",
                SizeLabel: "~6 GB",
                Tagline: "Everything in Try It Out plus real-time voice cloning and OBS output.",
                Bullets: new[]
                {
                    "Everything in Try It Out",
                    "RVC voice cloning (HuBERT + CREPE)",
                    "Spout2 → OBS integration",
                    "Microphone input with VAD + Whisper ASR",
                    "Emotion-driven avatar animation",
                }
            ),
            new(
                Tier: ProfileTier.BuildWithIt,
                Title: "Build With It",
                SizeLabel: "~12 GB",
                Tagline: "Full developer setup with all models, music separation, and vision support.",
                Bullets: new[]
                {
                    "Everything in Stream With It",
                    "Music source separation (MDX + MelBandRoformer)",
                    "Screen-capture vision LLM captions",
                    "Qwen3-TTS alternative voice engine",
                    "All optional ONNX models",
                }
            ),
        };
}
