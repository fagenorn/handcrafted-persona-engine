using FluentAssertions;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Assets.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Tests.Assets;

/// <summary>
///     <see cref="FeatureProfileMap" /> is a hand-maintained mirror of install-manifest.json.
///     The docstring warns "Drift here = misleading tooltips." These tests catch drift at
///     build time so a forgotten entry or wrong tier never ships.
/// </summary>
public class FeatureProfileMapDriftTests
{
    [Fact]
    public void Every_FeatureIds_constant_is_mapped()
    {
        var mapped = FeatureProfileMap.EnumerateMappings().Select(kvp => kvp.Key).ToHashSet();

        var missing = FeatureIds.All.Where(id => !mapped.Contains(id)).ToList();

        missing
            .Should()
            .BeEmpty(
                "every FeatureId must have a FeatureProfileMap entry so locked-affordance UI never falls back to the conservative BuildWithIt default"
            );
    }

    [Fact]
    public void Mapped_tier_matches_the_lowest_profile_that_enables_the_feature_in_the_manifest()
    {
        var manifest = ManifestLoader.LoadEmbedded();

        foreach (var (feature, mappedTier) in FeatureProfileMap.EnumerateMappings())
        {
            // The gate can be enabled as soon as every asset gated on it is included
            // in the profile. An asset is included when its ProfileTier <= selected.
            // So the minimum profile that unlocks the feature is the MAX ProfileTier
            // across all assets that list this feature as a gate.
            var gatingAssets = manifest
                .Assets.Where(a => a.Gates.Contains(feature.Value, StringComparer.Ordinal))
                .ToList();

            if (gatingAssets.Count == 0)
            {
                // Feature exists in the map but no manifest asset gates on it. That's
                // only legal for the always-on TryItOut entries, which exist purely so
                // MinimumTier(any) doesn't throw.
                mappedTier
                    .Should()
                    .Be(
                        ProfileTier.TryItOut,
                        $"feature '{feature.Value}' has no gating assets in the manifest; only the always-on TryItOut default is legal"
                    );
                continue;
            }

            var expectedTier = gatingAssets.Max(a => a.ProfileTier);

            mappedTier
                .Should()
                .Be(
                    expectedTier,
                    $"feature '{feature.Value}' is gated by assets whose highest ProfileTier is {expectedTier}; FeatureProfileMap must match"
                );
        }
    }
}
