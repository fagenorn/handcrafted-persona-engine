using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

public sealed class CommandLineArgsTests
{
    [Fact]
    public void Empty_args_yields_AutoIfMissing()
    {
        var (opts, nonInteractive) = CommandLineArgs.Parse(Array.Empty<string>());

        opts.Mode.Should().Be(BootstrapMode.AutoIfMissing);
        opts.PreselectedProfile.Should().BeNull();
        nonInteractive.Should().BeFalse();
    }

    [Theory]
    [InlineData("--install", BootstrapMode.Reinstall)]
    [InlineData("--verify", BootstrapMode.Verify)]
    [InlineData("--repair", BootstrapMode.Repair)]
    [InlineData("--offline", BootstrapMode.Offline)]
    public void Mode_flags_map_to_BootstrapMode(string flag, BootstrapMode expected)
    {
        var (opts, _) = CommandLineArgs.Parse(new[] { flag });

        opts.Mode.Should().Be(expected);
    }

    [Theory]
    [InlineData("try-it-out", ProfileTier.TryItOut)]
    [InlineData("stream-with-it", ProfileTier.StreamWithIt)]
    [InlineData("build-with-it", ProfileTier.BuildWithIt)]
    public void Profile_flag_preselects_profile(string slug, ProfileTier expected)
    {
        var (opts, _) = CommandLineArgs.Parse(new[] { "--profile", slug });

        opts.PreselectedProfile.Should().Be(expected);
    }

    [Fact]
    public void NonInteractive_flag_is_recognized()
    {
        var (_, nonInteractive) = CommandLineArgs.Parse(new[] { "--non-interactive" });

        nonInteractive.Should().BeTrue();
    }

    [Fact]
    public void Unknown_args_are_passed_through()
    {
        var act = () => CommandLineArgs.Parse(new[] { "--some-future-flag", "value" });

        act.Should().NotThrow();
    }

    [Fact]
    public void Unknown_profile_slug_throws()
    {
        var act = () => CommandLineArgs.Parse(new[] { "--profile", "not-a-profile" });

        act.Should().Throw<ArgumentException>().WithMessage("*not-a-profile*");
    }
}
