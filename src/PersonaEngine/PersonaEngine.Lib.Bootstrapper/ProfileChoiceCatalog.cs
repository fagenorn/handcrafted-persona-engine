using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>Static profile picker copy. Mirrors the design spec verbatim — change here = change in spec.</summary>
public static class ProfileChoiceCatalog
{
    public static IReadOnlyList<ProfileChoice> All { get; } =
        new ProfileChoice[]
        {
            new()
            {
                Profile = ProfileTier.TryItOut,
                Title = "Try it out",
                SizeLabel = "~3.1 GB",
                Tagline = "I just want to see this thing talk back to me.",
                Bullets = new[]
                {
                    "Voice-in / voice-out conversation with the default persona",
                    "Default Live2D avatar with lip-sync and idle animation",
                    "Subtitle + ImGui control panel",
                },
            },
            new()
            {
                Profile = ProfileTier.StreamWithIt,
                Title = "Stream with it",
                SizeLabel = "~8.0 GB",
                Tagline = "I want my Live2D persona on stream tonight.",
                Bullets = new[]
                {
                    "Everything in Try it out, plus:",
                    "RVC voice cloning for custom voice timbres",
                    "Spout output to OBS",
                    "Profanity beep filter",
                },
            },
            new()
            {
                Profile = ProfileTier.BuildWithIt,
                Title = "Build with it",
                SizeLabel = "~15.8 GB",
                Tagline = "I'm wiring this into my own pipeline.",
                Bullets = new[]
                {
                    "Everything in Stream with it, plus:",
                    "Vision LLM for screen-aware responses",
                    "Audio2Face streaming lip-sync solver",
                    "High-accuracy Whisper Turbo v3 transcription",
                },
            },
        };
}
