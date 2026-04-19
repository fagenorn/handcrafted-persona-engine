using FluentAssertions;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Bootstrapper.Manifest;
using Xunit;

namespace PersonaEngine.Lib.Tests.Assets;

public class FeatureIdsCoverageTests
{
    [Fact]
    public void Every_gate_string_in_the_manifest_has_a_FeatureIds_constant()
    {
        var manifest = ManifestLoader.LoadEmbedded();
        var known = FeatureIds.All.Select(f => f.Value).ToHashSet(StringComparer.Ordinal);

        var unknown = manifest
            .Assets.SelectMany(a => a.Gates)
            .Distinct(StringComparer.Ordinal)
            .Where(g => !known.Contains(g))
            .ToList();

        unknown
            .Should()
            .BeEmpty(
                "every gate string in install-manifest.json must have a corresponding FeatureIds constant"
            );
    }
}
