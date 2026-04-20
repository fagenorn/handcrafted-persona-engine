using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Builds the profile picker copy with real per-profile download sizes sourced
/// from the install manifest. Copy is written for the target archetype —
/// VTubers and streamers who want a talking avatar, not engineers shopping for
/// ML components — so we avoid jargon (no "ONNX", "CUDA", "CTC aligner").
/// </summary>
public static class ProfileChoiceCatalog
{
    // Approximate download sizes for NVIDIA redists (manifest declares
    // sizeBytes: 0 for those; their real size only resolves once the
    // NvidiaRedistClient fetches the platform manifest). These numbers are
    // within ~5% of the archive sizes reported by NVIDIA's redistrib_*.json
    // so the profile-size estimate stays honest even before first run.
    private static readonly IReadOnlyDictionary<string, long> NvidiaFallbackSizes = new Dictionary<
        string,
        long
    >(StringComparer.Ordinal)
    {
        // CUDA 12 redist family (ONNX Runtime GPU)
        ["cuda_cudart@12.4.1"] = 2_800_000,
        ["libcublas@12.4.1"] = 400_000_000,
        ["libcufft@12.4.1"] = 165_000_000,
        ["cudnn@9.1.1"] = 725_000_000,
        // CUDA 13 redist family (Whisper.net)
        ["cuda_cudart@13.0.3"] = 2_900_000,
        ["libcublas@13.0.3"] = 404_000_000,
    };

    public static IReadOnlyList<ProfileChoice> BuildFrom(InstallManifest manifest)
    {
        var sizes = ComputeProfileDownloadBytes(manifest);

        return new ProfileChoice[]
        {
            new()
            {
                Profile = ProfileTier.TryItOut,
                Title = "Try it out",
                SizeLabel = FormatSize(sizes[ProfileTier.TryItOut]),
                Tagline = "Just show me what the avatar can do.",
                Bullets = new[]
                {
                    "Talk to the avatar with your mic, hear it talk back",
                    "Default Aria Live2D character — lip-sync, blinking, idle motion",
                    "Live subtitles and an in-app control panel",
                    "Clean output to OBS (Spout) so you can record/stream it",
                    "Built-in profanity bleep so nothing embarrassing slips out",
                },
            },
            new()
            {
                Profile = ProfileTier.StreamWithIt,
                Title = "Stream with it",
                SizeLabel = FormatSize(sizes[ProfileTier.StreamWithIt]),
                Tagline = "Give my persona a voice of her own.",
                Bullets = new[]
                {
                    "Everything in Try it out, plus:",
                    "Clone a character voice with RVC voice conversion",
                    "Ships with a starter voice pack you can swap between on the fly",
                },
            },
            new()
            {
                Profile = ProfileTier.BuildWithIt,
                Title = "Build with it",
                SizeLabel = FormatSize(sizes[ProfileTier.BuildWithIt]),
                Tagline = "The full kit — a nicer voice and all the extras.",
                Bullets = new[]
                {
                    "Everything in Stream with it, plus:",
                    "Higher-quality Qwen3 voice — more natural, more expressive delivery",
                    "Vision: she can look at your screen and actually react to it",
                    "Sharper speech recognition (Whisper Turbo) — better accuracy with fast or accented speech",
                    "Audio2Face lip-sync for avatars that support detailed face rigs",
                    "Background-music / vocals separation so she hears you over your stream audio",
                },
            },
        };
    }

    internal static Dictionary<ProfileTier, long> ComputeProfileDownloadBytes(
        InstallManifest manifest
    )
    {
        var totals = new Dictionary<ProfileTier, long>
        {
            [ProfileTier.TryItOut] = 0,
            [ProfileTier.StreamWithIt] = 0,
            [ProfileTier.BuildWithIt] = 0,
        };

        foreach (var asset in manifest.Assets)
        {
            var bytes = EstimateBytes(asset);
            // A profile tier N includes every asset whose tier <= N — mirror
            // the planner's IsInProfile predicate so the "X GB" the user sees
            // matches what they'll actually download.
            foreach (var tier in totals.Keys.ToList())
            {
                if ((int)asset.ProfileTier <= (int)tier)
                    totals[tier] += bytes;
            }
        }

        return totals;
    }

    private static long EstimateBytes(AssetEntry asset)
    {
        if (asset.SizeBytes > 0)
            return asset.SizeBytes;
        if (asset.Source is NvidiaRedistSource nv)
        {
            var key = $"{nv.Package}@{nv.Version}";
            if (NvidiaFallbackSizes.TryGetValue(key, out var size))
                return size;
        }
        return 0;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
            return "size unknown";
        var mb = bytes / 1024d / 1024d;
        if (mb >= 1024)
            return $"~{mb / 1024d:0.#} GB";
        return $"~{mb:0} MB";
    }
}
