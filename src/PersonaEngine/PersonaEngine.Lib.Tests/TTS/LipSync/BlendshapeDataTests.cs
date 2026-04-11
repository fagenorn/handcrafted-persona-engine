using System.Globalization;
using PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.LipSync;

public class BlendshapeDataTests
{
    [Fact]
    public void ParseConfig_ReadsActiveIndicesAndRegularization()
    {
        // 52-element array: activate indices 0, 17, 23 (eyeBlinkLeft, jawOpen, mouthSmileLeft)
        var activePoses = new int[52];
        activePoses[0] = 1;
        activePoses[17] = 1;
        activePoses[23] = 1;

        var multipliers = new float[52];
        var offsets = new float[52];

        for (var i = 0; i < 52; i++)
        {
            multipliers[i] = 1.0f + i * 0.1f;
            offsets[i] = i * 0.01f;
        }

        var json = $$"""
            {
                "blendshape_params": {
                    "numPoses": 52,
                    "templateBBSize": 0.25,
                    "strengthL1regularization": 0.001,
                    "strengthL2regularization": 0.01,
                    "strengthTemporalSmoothing": 0.1,
                    "strengthSymmetry": 0.05,
                    "bsSolveActivePoses": [{{string.Join(",", activePoses)}}],
                    "bsWeightMultipliers": [{{string.Join(
                ",",
                multipliers.Select(f => f.ToString("F1", CultureInfo.InvariantCulture))
            )}}],
                    "bsWeightOffsets": [{{string.Join(
                ",",
                offsets.Select(f => f.ToString("F2", CultureInfo.InvariantCulture))
            )}}]
                }
            }
            """;

        var config = BlendshapeConfig.FromJson(json);

        Assert.Equal(52, config.NumPoses);
        Assert.Equal(0.25f, config.TemplateBBSize);
        Assert.Equal(0.001f, config.StrengthL1);
        Assert.Equal(0.01f, config.StrengthL2);
        Assert.Equal(0.1f, config.StrengthTemporal);
        Assert.Equal(0.05f, config.StrengthSymmetry);

        Assert.Equal([0, 17, 23], config.ActiveIndices);

        Assert.Equal(52, config.Multipliers.Length);
        Assert.Equal(1.0f, config.Multipliers[0]);
        Assert.Equal(1.0f + 17 * 0.1f, config.Multipliers[17], 0.001f);

        Assert.Equal(52, config.Offsets.Length);
        Assert.Equal(0.0f, config.Offsets[0]);
        Assert.Equal(17 * 0.01f, config.Offsets[17], 0.001f);
    }

    [Fact]
    public void ParseConfig_AllInactive_ReturnsEmptyActiveIndices()
    {
        var json = """
            {
                "blendshape_params": {
                    "numPoses": 52,
                    "templateBBSize": 0.3,
                    "strengthL1regularization": 0.0,
                    "strengthL2regularization": 0.0,
                    "strengthTemporalSmoothing": 0.0,
                    "strengthSymmetry": 0.0,
                    "bsSolveActivePoses": [0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
                    "bsWeightMultipliers": [1.0,1.0,1.0],
                    "bsWeightOffsets": [0.0,0.0,0.0]
                }
            }
            """;

        var config = BlendshapeConfig.FromJson(json);

        Assert.Empty(config.ActiveIndices);
        Assert.Equal(3, config.Multipliers.Length);
        Assert.Equal(3, config.Offsets.Length);
    }

    [Fact]
    public void ParseConfig_AllActive_ReturnsAllIndices()
    {
        var allOnes = string.Join(",", Enumerable.Repeat(1, 52));
        var allMultipliers = string.Join(",", Enumerable.Repeat("2.0", 52));
        var allOffsets = string.Join(",", Enumerable.Repeat("0.5", 52));

        var json = $$"""
            {
                "blendshape_params": {
                    "numPoses": 52,
                    "templateBBSize": 0.2,
                    "strengthL1regularization": 0.5,
                    "strengthL2regularization": 0.5,
                    "strengthTemporalSmoothing": 0.5,
                    "strengthSymmetry": 0.5,
                    "bsSolveActivePoses": [{{allOnes}}],
                    "bsWeightMultipliers": [{{allMultipliers}}],
                    "bsWeightOffsets": [{{allOffsets}}]
                }
            }
            """;

        var config = BlendshapeConfig.FromJson(json);

        Assert.Equal(52, config.ActiveIndices.Length);
        Assert.Equal(Enumerable.Range(0, 52).ToArray(), config.ActiveIndices);
        Assert.All(config.Multipliers, m => Assert.Equal(2.0f, m));
        Assert.All(config.Offsets, o => Assert.Equal(0.5f, o));
    }
}
