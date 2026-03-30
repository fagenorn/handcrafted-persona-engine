using System.Numerics;

namespace PersonaEngine.Lib.Live2D.Framework.Physics;

public record CubismPhysicsObj
{
    public MetaObj Meta { get; set; }

    public List<PhysicsSetting> PhysicsSettings { get; set; }

    public record MetaObj
    {
        public EffectiveForce EffectiveForces { get; set; }

        public float Fps { get; set; }

        public int PhysicsSettingCount { get; set; }

        public int TotalInputCount { get; set; }

        public int TotalOutputCount { get; set; }

        public int VertexCount { get; set; }

        public record EffectiveForce
        {
            public Vector2 Gravity { get; set; }

            public Vector2 Wind { get; set; }
        }
    }

    public record PhysicsSetting
    {
        public NormalizationObj Normalization { get; set; }

        public List<InputObj> Input { get; set; }

        public List<OutputObj> Output { get; set; }

        public List<Vertice> Vertices { get; set; }

        public record NormalizationObj
        {
            public PositionObj Position { get; set; }

            public PositionObj Angle { get; set; }

            public record PositionObj
            {
                public float Minimum { get; set; }

                public float Maximum { get; set; }

                public float Default { get; set; }
            }
        }

        public record InputObj
        {
            public float Weight { get; set; }

            public bool Reflect { get; set; }

            public string Type { get; set; }

            public SourceObj Source { get; set; }

            public record SourceObj
            {
                public string Id { get; set; }
            }
        }

        public record OutputObj
        {
            public int VertexIndex { get; set; }

            public float Scale { get; set; }

            public float Weight { get; set; }

            public DestinationObj Destination { get; set; }

            public string Type { get; set; }

            public bool Reflect { get; set; }

            public record DestinationObj
            {
                public string Id { get; set; }
            }
        }

        public record Vertice
        {
            public float Mobility { get; set; }

            public float Delay { get; set; }

            public float Acceleration { get; set; }

            public float Radius { get; set; }

            public Vector2 Position { get; set; }
        }
    }
}
