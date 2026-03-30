namespace PersonaEngine.Lib.Live2D.Framework.Model;

public record CubismModelUserDataObj
{
    public MetaObj Meta { get; set; }

    public List<UserDataObj> UserData { get; set; }

    public record MetaObj
    {
        public int UserDataCount { get; set; }

        public int TotalUserDataSize { get; set; }
    }

    public record UserDataObj
    {
        public string Target { get; set; }

        public string Id { get; set; }

        public string Value { get; set; }
    }
}
