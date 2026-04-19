using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests;

public sealed class CommandLineArgsTests
{
    [Fact]
    public void Empty_args_yields_AutoIfMissing()
    {
        var parsed = CommandLineArgs.Parse(Array.Empty<string>());

        parsed.Bootstrap.Mode.Should().Be(BootstrapMode.AutoIfMissing);
        parsed.Bootstrap.PreselectedProfile.Should().BeNull();
        parsed.NonInteractive.Should().BeFalse();
        parsed.PassThrough.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--reinstall", BootstrapMode.Reinstall)]
    [InlineData("--repair", BootstrapMode.Repair)]
    [InlineData("--verify", BootstrapMode.Verify)]
    [InlineData("--offline", BootstrapMode.Offline)]
    public void Mode_flags_map_to_BootstrapMode(string flag, BootstrapMode expected)
    {
        var parsed = CommandLineArgs.Parse(new[] { flag });
        parsed.Bootstrap.Mode.Should().Be(expected);
    }

    [Theory]
    [InlineData("try", ProfileTier.TryItOut)]
    [InlineData("stream", ProfileTier.StreamWithIt)]
    [InlineData("build", ProfileTier.BuildWithIt)]
    public void Profile_flag_preselects_profile(string slug, ProfileTier expected)
    {
        var parsed = CommandLineArgs.Parse(new[] { $"--profile={slug}" });
        parsed.Bootstrap.PreselectedProfile.Should().Be(expected);
    }

    [Fact]
    public void NonInteractive_flag_is_recognized()
    {
        var parsed = CommandLineArgs.Parse(new[] { "--non-interactive" });
        parsed.NonInteractive.Should().BeTrue();
    }

    [Fact]
    public void Unknown_args_are_passed_through()
    {
        var parsed = CommandLineArgs.Parse(new[] { "--other", "value", "--reinstall" });
        parsed.Bootstrap.Mode.Should().Be(BootstrapMode.Reinstall);
        parsed.PassThrough.Should().Equal("--other", "value");
    }

    [Fact]
    public void Unknown_profile_slug_throws()
    {
        var act = () => CommandLineArgs.Parse(new[] { "--profile=ultimate" });
        act.Should().Throw<ArgumentException>().WithMessage("*ultimate*");
    }
}
