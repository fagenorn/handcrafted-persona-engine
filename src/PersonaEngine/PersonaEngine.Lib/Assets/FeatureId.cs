namespace PersonaEngine.Lib.Assets;

public readonly record struct FeatureId(string Value)
{
    public override string ToString() => Value;
}
