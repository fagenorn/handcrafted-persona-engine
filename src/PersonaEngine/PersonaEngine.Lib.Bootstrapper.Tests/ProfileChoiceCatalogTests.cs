using FluentAssertions;
using PersonaEngine.Lib.Assets.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

public sealed class ProfileChoiceCatalogTests
{
    private static readonly InstallManifest Manifest = ManifestLoader.LoadEmbedded();

    [Fact]
    public void BuildFrom_returns_three_profiles_in_intent_order()
    {
        var profiles = ProfileChoiceCatalog.BuildFrom(Manifest);

        profiles.Should().HaveCount(3);
        profiles[0].Profile.Should().Be(ProfileTier.TryItOut);
        profiles[1].Profile.Should().Be(ProfileTier.StreamWithIt);
        profiles[2].Profile.Should().Be(ProfileTier.BuildWithIt);
    }

    [Fact]
    public void Each_profile_has_title_size_tagline_and_at_least_one_bullet()
    {
        foreach (var choice in ProfileChoiceCatalog.BuildFrom(Manifest))
        {
            choice
                .Title.Should()
                .NotBeNullOrWhiteSpace(because: $"{choice.Profile} must have a title");
            choice.SizeLabel.Should().Contain("GB");
            choice
                .Tagline.Should()
                .NotBeNullOrWhiteSpace(because: $"{choice.Profile} must have a tagline");
            choice
                .Bullets.Should()
                .NotBeEmpty(because: $"{choice.Profile} must have bullet points");
        }
    }

    [Fact]
    public void Profile_sizes_are_monotonically_nondecreasing_by_tier()
    {
        // Higher tiers include every lower-tier asset, so their byte totals
        // must never shrink as the user moves from "Try it out" → "Build with it".
        var totals = ProfileChoiceCatalog.ComputeProfileDownloadBytes(Manifest);

        totals[ProfileTier.TryItOut].Should().BeLessThanOrEqualTo(totals[ProfileTier.StreamWithIt]);
        totals[ProfileTier.StreamWithIt]
            .Should()
            .BeLessThanOrEqualTo(totals[ProfileTier.BuildWithIt]);
    }

    [Fact]
    public void Profile_sizes_cover_nvidia_redists_with_fallback_values()
    {
        // The NVIDIA redist manifest entries carry sizeBytes: 0 because the real
        // size only resolves at runtime. The catalog must substitute its fallback
        // table so the user sees an honest estimate — otherwise Try-it-out would
        // report ~0 GB for the CUDA redists the bootstrapper will actually pull.
        var totals = ProfileChoiceCatalog.ComputeProfileDownloadBytes(Manifest);
        totals[ProfileTier.TryItOut].Should().BeGreaterThan(1_000_000_000);
    }
}
