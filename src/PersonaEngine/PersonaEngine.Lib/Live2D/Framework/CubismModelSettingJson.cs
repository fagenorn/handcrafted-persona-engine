namespace PersonaEngine.Lib.Live2D.Framework;

public static class CubismModelSettingJson
{
    // JSON keys
    public const string Version = "Version";

    public const string FileReferences = "FileReferences";

    public const string Groups = "Groups";

    public const string Layout = "Layout";

    public const string HitAreas = "HitAreas";

    public const string Moc = "Moc";

    public const string Textures = "Textures";

    public const string Physics = "Physics";

    public const string DisplayInfo = "DisplayInfo";

    public const string Pose = "Pose";

    public const string Expressions = "Expressions";

    public const string Motions = "Motions";

    public const string UserData = "UserData";

    public const string Name = "Name";

    public const string FilePath = "File";

    public const string Id = "Id";

    public const string Ids = "Ids";

    public const string Target = "Target";

    // Motions
    public const string Idle = "Idle";

    public const string TapBody = "TapBody";

    public const string PinchIn = "PinchIn";

    public const string PinchOut = "PinchOut";

    public const string Shake = "Shake";

    public const string FlickHead = "FlickHead";

    public const string Parameter = "Parameter";

    public const string SoundPath = "Sound";

    public const string FadeInTime = "FadeInTime";

    public const string FadeOutTime = "FadeOutTime";

    // Layout
    public const string CenterX = "CenterX";

    public const string CenterY = "CenterY";

    public const string X = "X";

    public const string Y = "Y";

    public const string Width = "Width";

    public const string Height = "Height";

    public const string LipSync = "LipSync";

    public const string EyeBlink = "EyeBlink";

    public const string InitParameter = "init_param";

    public const string InitPartsVisible = "init_parts_visible";

    public const string Val = "val";

    public static bool GetLayoutMap(
        this ModelSettingObj obj,
        Dictionary<string, float> outLayoutMap
    )
    {
        var node = obj.Layout;
        if (node == null)
        {
            return false;
        }

        var ret = false;
        foreach (var item in node)
        {
            if (outLayoutMap.ContainsKey(item.Key))
            {
                outLayoutMap[item.Key] = item.Value;
            }
            else
            {
                outLayoutMap.Add(item.Key, item.Value);
            }

            ret = true;
        }

        return ret;
    }

    public static bool IsExistEyeBlinkParameters(this ModelSettingObj obj)
    {
        var node = obj.Groups;
        if (node == null)
        {
            return false;
        }

        foreach (var item in node)
        {
            if (item.Name == EyeBlink)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsExistLipSyncParameters(this ModelSettingObj obj)
    {
        var node = obj.Groups;
        if (node == null)
        {
            return false;
        }

        foreach (var item in node)
        {
            if (item.Name == LipSync)
            {
                return true;
            }
        }

        return false;
    }
}
