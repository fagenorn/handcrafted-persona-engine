namespace PersonaEngine.Lib.Configuration;

public record SpoutConfiguration
{
    public bool Enabled { get; set; } = false;
    
    public SpoutOutputConfigurations[] Outputs { get; set; } = [];
}