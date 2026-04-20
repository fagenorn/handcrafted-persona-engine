using System.Text.Json;

namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

public sealed class AnimatorSkinConfig
{
    public float SkinStrength { get; init; }
    public float EyelidOpenOffset { get; init; }
    public float BlinkStrength { get; init; }
    public float LipOpenOffset { get; init; }
    public float UpperFaceSmoothing { get; init; }
    public float LowerFaceSmoothing { get; init; }
    public float FaceMaskLevel { get; init; }
    public float FaceMaskSoftness { get; init; }
    public float EyeballsStrength { get; init; }
    public float RightEyeRotXOffset { get; init; }
    public float RightEyeRotYOffset { get; init; }
    public float LeftEyeRotXOffset { get; init; }
    public float LeftEyeRotYOffset { get; init; }

    public float SaccadeStrength { get; init; }

    public static AnimatorSkinConfig FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var cfg = doc.RootElement.GetProperty("config");

        return new AnimatorSkinConfig
        {
            SkinStrength = cfg.GetProperty("skin_strength").GetSingle(),
            EyelidOpenOffset = cfg.GetProperty("eyelid_open_offset").GetSingle(),
            BlinkStrength = cfg.GetProperty("blink_strength").GetSingle(),
            LipOpenOffset = cfg.GetProperty("lip_open_offset").GetSingle(),
            UpperFaceSmoothing = cfg.GetProperty("upper_face_smoothing").GetSingle(),
            LowerFaceSmoothing = cfg.GetProperty("lower_face_smoothing").GetSingle(),
            FaceMaskLevel = cfg.GetProperty("face_mask_level").GetSingle(),
            FaceMaskSoftness = cfg.GetProperty("face_mask_softness").GetSingle(),
            EyeballsStrength = GetOptionalFloat(cfg, "eyeballs_strength", 1.0f),
            RightEyeRotXOffset = GetOptionalFloat(cfg, "right_eye_rot_x_offset", 0.0f),
            RightEyeRotYOffset = GetOptionalFloat(cfg, "right_eye_rot_y_offset", 0.0f),
            LeftEyeRotXOffset = GetOptionalFloat(cfg, "left_eye_rot_x_offset", 0.0f),
            LeftEyeRotYOffset = GetOptionalFloat(cfg, "left_eye_rot_y_offset", 0.0f),
            SaccadeStrength = GetOptionalFloat(cfg, "saccade_strength", 0.6f),
        };
    }

    private static float GetOptionalFloat(JsonElement cfg, string name, float defaultValue)
    {
        return cfg.TryGetProperty(name, out var prop) ? prop.GetSingle() : defaultValue;
    }
}
