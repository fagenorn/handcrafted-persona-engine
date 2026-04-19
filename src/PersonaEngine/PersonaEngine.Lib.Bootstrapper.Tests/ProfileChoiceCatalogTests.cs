using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

public sealed class ProfileChoiceCatalogTests
{
    [Fact]
    public void All_returns_three_profiles_in_tier_order()
    {
        var profiles = ProfileChoiceCatalog.All;

        profiles.Should().HaveCount(3);
        profiles[0].Tier.Should().Be(ProfileTier.TryItOut);
        profiles[1].Tier.Should().Be(ProfileTier.StreamWithIt);
        profiles[2].Tier.Should().Be(ProfileTier.BuildWithIt);
    }

    [Fact]
    public void Each_profile_has_non_empty_title_tagline_and_bullets()
    {
        foreach (var p in ProfileChoiceCatalog.All)
        {
            p.Title.Should().NotBeNullOrWhiteSpace(because: $"{p.Tier} must have a title");
            p.SizeLabel.Should().NotBeNullOrWhiteSpace(because: $"{p.Tier} must have a size label");
            p.Tagline.Should().NotBeNullOrWhiteSpace(because: $"{p.Tier} must have a tagline");
            p.Bullets.Should().NotBeEmpty(because: $"{p.Tier} must have bullet points");
            foreach (var bullet in p.Bullets)
                bullet
                    .Should()
                    .NotBeNullOrWhiteSpace(because: $"{p.Tier} bullet must not be blank");
        }
    }
}
