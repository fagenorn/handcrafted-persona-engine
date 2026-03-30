namespace PersonaEngine.Lib.Live2D.Framework.Motion;

public record CubismMotionObj
{
    public MetaObj Meta { get; set; }

    public List<Curve> Curves { get; set; }

    public List<UserDataObj> UserData { get; set; }

    public record MetaObj
    {
        public float Duration { get; set; }

        public bool Loop { get; set; }

        public bool AreBeziersRestricted { get; set; }

        public int CurveCount { get; set; }

        public float Fps { get; set; }

        public int TotalSegmentCount { get; set; }

        public int TotalPointCount { get; set; }

        public float? FadeInTime { get; set; }

        public float? FadeOutTime { get; set; }

        public int UserDataCount { get; set; }

        public int TotalUserDataSize { get; set; }
    }

    public record Curve
    {
        public float? FadeInTime { get; set; }

        public float? FadeOutTime { get; set; }

        public List<float> Segments { get; set; }

        public string Target { get; set; }

        public string Id { get; set; }
    }

    public record UserDataObj
    {
        public float Time { get; set; }

        public string Value { get; set; }
    }
}
