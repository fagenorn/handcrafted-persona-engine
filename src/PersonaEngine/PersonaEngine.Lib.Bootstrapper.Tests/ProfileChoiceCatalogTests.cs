using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

public sealed class ProfileChoiceCatalogTests
{
    [Fact]
    public void All_returns_three_profiles_in_intent_order()
    {
        var profiles = ProfileChoiceCatalog.All;

        profiles.Should().HaveCount(3);
        profiles[0].Profile.Should().Be(ProfileTier.TryItOut);
        profiles[1].Profile.Should().Be(ProfileTier.StreamWithIt);
        profiles[2].Profile.Should().Be(ProfileTier.BuildWithIt);
    }

    [Fact]
    public void Each_profile_has_title_size_tagline_and_at_least_one_bullet()
    {
        foreach (var choice in ProfileChoiceCatalog.All)
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
}
