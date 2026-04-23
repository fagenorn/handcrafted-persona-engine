using FluentAssertions;
using PersonaEngine.Lib.UI.Rendering.Shaders;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.Rendering.Shaders;

public class ShaderRegistryTests
{
    [Fact]
    public void GetSource_LoadsPackagedFixture()
    {
        var source = ShaderRegistry.GetSource("test/ascii_ok.glsl");

        source.Should().Contain("#version 330");
        source.Should().Contain("gl_FragColor");
    }

    [Fact]
    public void GetSource_SecondCallReturnsSameReference()
    {
        var first = ShaderRegistry.GetSource("test/ascii_ok.glsl");
        var second = ShaderRegistry.GetSource("test/ascii_ok.glsl");

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void GetSource_MissingFile_ThrowsWithAttemptedPath()
    {
        var act = () => ShaderRegistry.GetSource("test/does_not_exist.glsl");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*test/does_not_exist.glsl*")
            .WithMessage("*Resources*Shaders*");
    }
}
