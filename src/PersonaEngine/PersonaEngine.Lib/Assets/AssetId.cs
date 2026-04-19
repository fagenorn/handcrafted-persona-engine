namespace PersonaEngine.Lib.Assets;

public readonly record struct AssetId(string Value)
{
    public override string ToString() => Value;
}
