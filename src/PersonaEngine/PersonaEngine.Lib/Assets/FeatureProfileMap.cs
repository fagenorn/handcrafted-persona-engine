using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Assets;

/// <summary>
///     Maps each <see cref="FeatureId" /> to the lowest <see cref="ProfileTier" /> that
///     installs the assets it needs. Used by locked-affordance UI to tell the user which
///     profile they need to re-run the installer with to unlock a feature.
///     <para>
///         Single source of truth: must mirror <c>install-manifest.json</c>'s per-asset
///         <c>profileTier</c>. Drift here = misleading tooltips, not crashes.
///     </para>
/// </summary>
public static class FeatureProfileMap
{
    private static readonly IReadOnlyDictionary<FeatureId, ProfileTier> Map = new Dictionary<
        FeatureId,
        ProfileTier
    >
    {
        // TryItOut tier — these are always available; included for completeness so
        // callers can ask MinimumTier(any) without a missing-key throw.
        { FeatureIds.LlmText, ProfileTier.TryItOut },
        { FeatureIds.AsrFastTier, ProfileTier.TryItOut },
        { FeatureIds.TtsKokoro, ProfileTier.TryItOut },
        { FeatureIds.ProfanityFilter, ProfileTier.TryItOut },
        { FeatureIds.Live2DAvatar, ProfileTier.TryItOut },
        // StreamWithIt tier
        { FeatureIds.VoiceCloning, ProfileTier.StreamWithIt },
        // BuildWithIt tier
        { FeatureIds.LlmVision, ProfileTier.BuildWithIt },
        { FeatureIds.AsrAccurate, ProfileTier.BuildWithIt },
        { FeatureIds.TtsQwen3, ProfileTier.BuildWithIt },
        { FeatureIds.Audio2Face, ProfileTier.BuildWithIt },
        { FeatureIds.VisionCapture, ProfileTier.BuildWithIt },
        { FeatureIds.MusicSourceSeparation, ProfileTier.BuildWithIt },
    };

    /// <summary>
    ///     Returns the lowest profile tier whose installer pulls down everything
    ///     <paramref name="feature" /> needs. Falls back to <see cref="ProfileTier.BuildWithIt" />
    ///     for any feature not explicitly mapped — the conservative default so a forgotten
    ///     entry still produces an "upgrade to the largest profile" message rather than
    ///     a misleadingly small one.
    /// </summary>
    public static ProfileTier MinimumTier(FeatureId feature) =>
        Map.TryGetValue(feature, out var tier) ? tier : ProfileTier.BuildWithIt;

    /// <summary>
    ///     Human-readable name of <see cref="MinimumTier" />, matching the installer's
    ///     profile picker copy verbatim ("Try it out", "Stream with it", "Build with it").
    /// </summary>
    public static string MinimumProfileLabel(FeatureId feature) =>
        ProfileLabel(MinimumTier(feature));

    public static string ProfileLabel(ProfileTier tier) =>
        tier switch
        {
            ProfileTier.TryItOut => "Try it out",
            ProfileTier.StreamWithIt => "Stream with it",
            ProfileTier.BuildWithIt => "Build with it",
            _ => tier.ToString(),
        };
}
