using System.Numerics;

namespace PersonaEngine.Lib.UI.ControlPanel;

public record struct Particle
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Lifetime;
    public float MaxLifetime;
    public float Radius;
    public Vector4 Color;
    public float WobblePhase;

    public readonly bool IsAlive => Lifetime < MaxLifetime;

    public readonly float Alpha
    {
        get
        {
            if (MaxLifetime <= 0f)
                return 0f;
            var t = Lifetime / MaxLifetime;
            // Fade in over first 15%, fade out over last 20%
            var fadeIn = Math.Clamp(t / 0.15f, 0f, 1f);
            var fadeOut = Math.Clamp((1f - t) / 0.20f, 0f, 1f);
            return fadeIn * fadeOut;
        }
    }
}

public sealed class ParticleSystem
{
    private readonly Particle[] _particles;
    private readonly Random _rng = new();

    // Pre-prime so the first spawn fires within the first second of simulation
    private float _spawnAccumulator = 0.9f;

    public ParticleSystem(int maxParticles)
    {
        _particles = new Particle[maxParticles];
        // Mark all as dead initially
        for (var i = 0; i < _particles.Length; i++)
            _particles[i].MaxLifetime = 0f;
    }

    public int AliveCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].IsAlive)
                    count++;
            }

            return count;
        }
    }

    public ReadOnlySpan<Particle> Particles => _particles;

    public void Update(float dt, PersonaUiState state, Vector2 bounds)
    {
        var config = GetConfig(state);

        // Update existing particles
        for (var i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            if (!p.IsAlive)
                continue;

            p.Lifetime += dt;

            if (!p.IsAlive)
                continue;

            // Wobble on X axis
            var wobble = MathF.Sin(p.Lifetime * 0.8f + p.WobblePhase) * 8f;
            p.Position.X += (p.Velocity.X + wobble * dt) * dt;
            p.Position.Y += p.Velocity.Y * dt;
        }

        // Spawn new particles to reach target count
        var alive = AliveCount;
        if (alive < config.TargetCount)
        {
            _spawnAccumulator += dt;
            var spawnInterval = 1f / config.SpawnRate;

            while (_spawnAccumulator >= spawnInterval && alive < config.TargetCount)
            {
                _spawnAccumulator -= spawnInterval;
                SpawnParticle(bounds, config);
                alive++;
            }
        }
        else
        {
            _spawnAccumulator = 0f;
        }
    }

    private void SpawnParticle(Vector2 bounds, in ParticleConfig config)
    {
        // Find a dead slot
        for (var i = 0; i < _particles.Length; i++)
        {
            if (_particles[i].IsAlive)
                continue;

            var speed =
                config.MinSpeed + (float)_rng.NextDouble() * (config.MaxSpeed - config.MinSpeed);
            var angle = (float)(_rng.NextDouble() * MathF.Tau);
            // Bias upward for speaking state
            var vy =
                -MathF.Abs(MathF.Sin(angle)) * speed * config.UpwardBias
                + MathF.Sin(angle) * speed * (1f - config.UpwardBias);

            _particles[i] = new Particle
            {
                Position = new Vector2(
                    (float)_rng.NextDouble() * bounds.X,
                    (float)_rng.NextDouble() * bounds.Y
                ),
                Velocity = new Vector2(MathF.Cos(angle) * speed, vy),
                Lifetime = 0f,
                MaxLifetime =
                    config.MinLifetime
                    + (float)_rng.NextDouble() * (config.MaxLifetime - config.MinLifetime),
                Radius = 2f + (float)_rng.NextDouble() * 2f,
                Color = config.BaseColor with { W = 1f },
                WobblePhase = (float)(_rng.NextDouble() * MathF.Tau),
            };

            return;
        }
    }

    private static ParticleConfig GetConfig(PersonaUiState state)
    {
        return state switch
        {
            PersonaUiState.Speaking => new ParticleConfig
            {
                TargetCount = 11,
                SpawnRate = 1.5f,
                MinSpeed = 25f,
                MaxSpeed = 40f,
                MinLifetime = 6f,
                MaxLifetime = 9f,
                UpwardBias = 0.6f,
                BaseColor = Theme.AccentPrimary,
            },
            PersonaUiState.Thinking => new ParticleConfig
            {
                TargetCount = 9,
                SpawnRate = 1.2f,
                MinSpeed = 20f,
                MaxSpeed = 35f,
                MinLifetime = 6f,
                MaxLifetime = 9f,
                UpwardBias = 0.3f,
                BaseColor = Theme.AccentSecondary,
            },
            _ => new ParticleConfig
            {
                TargetCount = 7,
                SpawnRate = 1f,
                MinSpeed = 20f,
                MaxSpeed = 35f,
                MinLifetime = 7f,
                MaxLifetime = 10f,
                UpwardBias = 0.2f,
                BaseColor = Theme.AccentSecondary,
            },
        };
    }

    private struct ParticleConfig
    {
        public int TargetCount;
        public float SpawnRate;
        public float MinSpeed;
        public float MaxSpeed;
        public float MinLifetime;
        public float MaxLifetime;
        public float UpwardBias;
        public Vector4 BaseColor;
    }
}
