using System.IO.Compression;
using System.Text.Json;
using PersonaEngine.Lib.Utils.IO;

namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Configuration parsed from a bs_skin_config JSON file,
///     containing regularization strengths and active blendshape indices.
/// </summary>
public sealed class BlendshapeConfig
{
    public int NumPoses { get; init; }

    public float TemplateBBSize { get; init; }

    public float StrengthL1 { get; init; }

    public float StrengthL2 { get; init; }

    public float StrengthTemporal { get; init; }

    public float StrengthSymmetry { get; init; }

    /// <summary>
    ///     Indices where bsSolveActivePoses[i] == 1.
    /// </summary>
    public int[] ActiveIndices { get; init; } = [];

    public float[] Multipliers { get; init; } = [];

    public float[] Offsets { get; init; } = [];

    /// <summary>
    ///     Parses a blendshape config from a bs_skin_config JSON string.
    /// </summary>
    public static BlendshapeConfig FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var bp = root.GetProperty("blendshape_params");

        var activePoses = bp.GetProperty("bsSolveActivePoses");
        var activeIndices = new List<int>();

        var index = 0;
        foreach (var element in activePoses.EnumerateArray())
        {
            if (element.GetInt32() == 1)
            {
                activeIndices.Add(index);
            }

            index++;
        }

        var multipliers = ReadFloatArray(bp.GetProperty("bsWeightMultipliers"));
        var offsets = ReadFloatArray(bp.GetProperty("bsWeightOffsets"));

        return new BlendshapeConfig
        {
            NumPoses = bp.GetProperty("numPoses").GetInt32(),
            TemplateBBSize = bp.GetProperty("templateBBSize").GetSingle(),
            StrengthL1 = bp.GetProperty("strengthL1regularization").GetSingle(),
            StrengthL2 = bp.GetProperty("strengthL2regularization").GetSingle(),
            StrengthTemporal = bp.GetProperty("strengthTemporalSmoothing").GetSingle(),
            StrengthSymmetry = bp.GetProperty("strengthSymmetry").GetSingle(),
            ActiveIndices = activeIndices.ToArray(),
            Multipliers = multipliers,
            Offsets = offsets,
        };
    }

    private static float[] ReadFloatArray(JsonElement element)
    {
        var result = new float[element.GetArrayLength()];
        var i = 0;

        foreach (var item in element.EnumerateArray())
        {
            result[i++] = item.GetSingle();
        }

        return result;
    }
}

/// <summary>
///     Loaded blendshape basis data from NPZ files, ready for the solve step.
/// </summary>
public sealed class BlendshapeData
{
    /// <summary>
    ///     The 52 ARKit blendshape names in standard order.
    /// </summary>
    private static readonly string[] ARKitBlendshapeNames =
    [
        "eyeBlinkLeft",
        "eyeLookDownLeft",
        "eyeLookInLeft",
        "eyeLookOutLeft",
        "eyeLookUpLeft",
        "eyeSquintLeft",
        "eyeWideLeft",
        "eyeBlinkRight",
        "eyeLookDownRight",
        "eyeLookInRight",
        "eyeLookOutRight",
        "eyeLookUpRight",
        "eyeSquintRight",
        "eyeWideRight",
        "jawForward",
        "jawLeft",
        "jawRight",
        "jawOpen",
        "mouthClose",
        "mouthFunnel",
        "mouthPucker",
        "mouthLeft",
        "mouthRight",
        "mouthSmileLeft",
        "mouthSmileRight",
        "mouthFrownLeft",
        "mouthFrownRight",
        "mouthDimpleLeft",
        "mouthDimpleRight",
        "mouthStretchLeft",
        "mouthStretchRight",
        "mouthRollLower",
        "mouthRollUpper",
        "mouthShrugLower",
        "mouthShrugUpper",
        "mouthPressLeft",
        "mouthPressRight",
        "mouthLowerDownLeft",
        "mouthLowerDownRight",
        "mouthUpperUpLeft",
        "mouthUpperUpRight",
        "browDownLeft",
        "browDownRight",
        "browInnerUp",
        "browOuterUpLeft",
        "browOuterUpRight",
        "cheekPuff",
        "cheekSquintLeft",
        "cheekSquintRight",
        "noseSneerLeft",
        "noseSneerRight",
        "tongueOut",
    ];

    /// <summary>
    ///     Masked neutral vertex positions, flattened to [M*3].
    /// </summary>
    public required float[] NeutralFlat { get; init; }

    /// <summary>
    ///     Delta matrix [M*3, K] in row-major order, where M is the number of masked
    ///     vertices and K is the number of active blendshapes.
    /// </summary>
    public required float[] DeltaMatrix { get; init; }

    /// <summary>
    ///     Number of masked vertex components (M * 3).
    /// </summary>
    public required int MaskedPositionCount { get; init; }

    /// <summary>
    ///     Number of active blendshapes (K).
    /// </summary>
    public required int ActiveCount { get; init; }

    /// <summary>
    ///     Vertex indices from the frontal mask [M].
    /// </summary>
    public required int[] FrontalMask { get; init; }

    /// <summary>
    ///     Full neutral skin vertex positions [V*3] from the model data NPZ.
    /// </summary>
    public required float[] NeutralSkinFlat { get; init; }

    /// <summary>
    ///     Eye close pose delta [24002*3] from model_data NPZ, used by AnimatorSkin compose.
    /// </summary>
    public required float[] EyeClosePoseDeltaFlat { get; init; }

    /// <summary>
    ///     Lip open pose delta [24002*3] from model_data NPZ, used by AnimatorSkin compose.
    /// </summary>
    public required float[] LipOpenPoseDeltaFlat { get; init; }

    /// <summary>
    ///     Pre-computed micro eye movement data at 30fps from model_data NPZ.
    ///     Shape: [N, 2] where each row is (X, Y) rotation offset.
    ///     Null if the model data does not contain saccade information.
    /// </summary>
    public float[,]? SaccadeRotMatrix { get; init; }

    /// <summary>
    ///     Loads blendshape basis data from NPZ files.
    /// </summary>
    /// <param name="skinNpzPath">Path to the bs_skin NPZ file.</param>
    /// <param name="modelDataNpzPath">Path to the model_data NPZ file.</param>
    /// <param name="config">Parsed blendshape configuration.</param>
    public static BlendshapeData Load(
        string skinNpzPath,
        string modelDataNpzPath,
        BlendshapeConfig config
    )
    {
        using var skinZip = ZipFile.OpenRead(skinNpzPath);

        // 1. Read neutral pose
        var neutral = ReadFloat32FromZip(skinZip, "neutral.npy");

        // 2. Read frontal mask (int32 vertex indices)
        var frontalMask = ReadInt32FromZip(skinZip, "frontalMask.npy");

        var maskedVertexCount = frontalMask.Length;
        var maskedPositionCount = maskedVertexCount * 3;
        var activeCount = config.ActiveIndices.Length;

        // 3. Build masked neutral
        var neutralFlat = new float[maskedPositionCount];

        for (var m = 0; m < maskedVertexCount; m++)
        {
            var vertexIdx = frontalMask[m];
            neutralFlat[m * 3] = neutral[vertexIdx * 3];
            neutralFlat[m * 3 + 1] = neutral[vertexIdx * 3 + 1];
            neutralFlat[m * 3 + 2] = neutral[vertexIdx * 3 + 2];
        }

        // 4. Load active blendshape deltas and build delta matrix
        var deltaMatrix = new float[maskedPositionCount * activeCount];

        for (var k = 0; k < activeCount; k++)
        {
            var poseIndex = config.ActiveIndices[k];
            var blendshapeName = ARKitBlendshapeNames[poseIndex];

            var blendshapeData = ReadFloat32FromZip(skinZip, $"{blendshapeName}.npy");

            // Apply frontal mask and store as column k in row-major layout
            for (var m = 0; m < maskedVertexCount; m++)
            {
                var vertexIdx = frontalMask[m];
                // Row = m*3+c, Col = k => index = (m*3+c) * activeCount + k
                deltaMatrix[(m * 3) * activeCount + k] = blendshapeData[vertexIdx * 3];
                deltaMatrix[(m * 3 + 1) * activeCount + k] = blendshapeData[vertexIdx * 3 + 1];
                deltaMatrix[(m * 3 + 2) * activeCount + k] = blendshapeData[vertexIdx * 3 + 2];
            }
        }

        // 5. Load neutral skin from model data NPZ
        using var modelZip = ZipFile.OpenRead(modelDataNpzPath);
        var neutralSkinFlat = ReadFloat32FromZip(modelZip, "neutral_skin.npy");
        var eyeCloseDelta = ReadFloat32FromZip(modelZip, "eye_close_pose_delta.npy");
        var lipOpenDelta = ReadFloat32FromZip(modelZip, "lip_open_pose_delta.npy");

        // 6. Load saccade data (optional — not all model data files include it)
        float[,]? saccadeRotMatrix = null;
        var saccadeEntry = modelZip.GetEntry("saccade_rot_matrix.npy");
        if (saccadeEntry != null)
        {
            using var saccadeStream = saccadeEntry.Open();
            using var saccadeMemory = new MemoryStream();
            saccadeStream.CopyTo(saccadeMemory);
            saccadeMemory.Position = 0;
            var (saccadeFlat, _) = NpyReader.ReadFloat32(saccadeMemory, "saccade_rot_matrix.npy");
            var saccadeLen = saccadeFlat.Length / 2;
            saccadeRotMatrix = new float[saccadeLen, 2];
            for (var i = 0; i < saccadeLen; i++)
            {
                saccadeRotMatrix[i, 0] = saccadeFlat[i * 2];
                saccadeRotMatrix[i, 1] = saccadeFlat[i * 2 + 1];
            }
        }

        return new BlendshapeData
        {
            NeutralFlat = neutralFlat,
            DeltaMatrix = deltaMatrix,
            MaskedPositionCount = maskedPositionCount,
            ActiveCount = activeCount,
            FrontalMask = frontalMask,
            NeutralSkinFlat = neutralSkinFlat,
            EyeClosePoseDeltaFlat = eyeCloseDelta,
            LipOpenPoseDeltaFlat = lipOpenDelta,
            SaccadeRotMatrix = saccadeRotMatrix,
        };
    }

    private static float[] ReadFloat32FromZip(ZipArchive zip, string entryName)
    {
        var entry =
            zip.GetEntry(entryName)
            ?? throw new FileNotFoundException(
                $"NPZ archive does not contain entry '{entryName}'."
            );

        using var entryStream = entry.Open();
        using var memoryStream = new MemoryStream();
        entryStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        var (data, _) = NpyReader.ReadFloat32(memoryStream, entryName);

        return data;
    }

    private static int[] ReadInt32FromZip(ZipArchive zip, string entryName)
    {
        var entry =
            zip.GetEntry(entryName)
            ?? throw new FileNotFoundException(
                $"NPZ archive does not contain entry '{entryName}'."
            );

        using var entryStream = entry.Open();
        using var memoryStream = new MemoryStream();
        entryStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        var (data, _) = NpyReader.ReadInt32(memoryStream, entryName);

        return data;
    }
}
