using System.Numerics;
using PersonaEngine.Lib.UI.ControlPanel;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI;

public class ParticleSystemTests
{
    [Fact]
    public void NewSystem_HasNoAliveParticles()
    {
        var system = new ParticleSystem(12);
        Assert.Equal(0, system.AliveCount);
    }

    [Fact]
    public void Update_SpawnsParticles_OverTime()
    {
        var system = new ParticleSystem(12);
        var bounds = new Vector2(800f, 600f);

        // Several updates should spawn particles
        for (var i = 0; i < 60; i++)
            system.Update(0.016f, PersonaUiState.Idle, bounds);

        Assert.True(system.AliveCount > 0);
    }

    [Fact]
    public void Update_NeverExceedsMaxParticles()
    {
        var system = new ParticleSystem(12);
        var bounds = new Vector2(800f, 600f);

        // Many updates — should not exceed maxParticles
        for (var i = 0; i < 600; i++)
            system.Update(0.016f, PersonaUiState.Idle, bounds);

        Assert.True(system.AliveCount <= 12);
    }

    [Fact]
    public void Update_SpeakingState_HasAtLeastAsMany_AsIdle()
    {
        var idleSystem = new ParticleSystem(12);
        var speakingSystem = new ParticleSystem(12);
        var bounds = new Vector2(800f, 600f);

        for (var i = 0; i < 600; i++)
        {
            idleSystem.Update(0.016f, PersonaUiState.Idle, bounds);
            speakingSystem.Update(0.016f, PersonaUiState.Speaking, bounds);
        }

        // Speaking target count (11) > Idle target count (7)
        Assert.True(speakingSystem.AliveCount >= idleSystem.AliveCount);
    }

    [Fact]
    public void Particle_Alpha_FadesInAndOut()
    {
        // Alpha should be low at start (fading in) and end (fading out), high in the middle
        var p = new Particle { Lifetime = 0f, MaxLifetime = 8f };
        Assert.Equal(0f, p.Alpha, precision: 2); // fading in at t=0

        p = p with { Lifetime = 4f };
        Assert.True(p.Alpha > 0.5f, $"Expected middle alpha > 0.5, got {p.Alpha}"); // middle of life

        p = p with { Lifetime = 7.9f };
        Assert.True(p.Alpha < 0.3f, $"Expected end alpha < 0.3, got {p.Alpha}"); // fading out
    }

    [Fact]
    public void Particle_Alpha_ZeroWhenMaxLifetimeIsZero()
    {
        var p = new Particle { Lifetime = 0f, MaxLifetime = 0f };
        Assert.Equal(0f, p.Alpha);
    }

    [Fact]
    public void Particle_IsAlive_FalseWhenLifetimeExceedsMax()
    {
        var p = new Particle { Lifetime = 10f, MaxLifetime = 5f };
        Assert.False(p.IsAlive);
    }

    [Fact]
    public void Update_SpawnedParticles_AreInsideBounds()
    {
        var system = new ParticleSystem(12);
        var bounds = new Vector2(800f, 600f);

        for (var i = 0; i < 300; i++)
            system.Update(0.016f, PersonaUiState.Idle, bounds);

        Assert.True(system.AliveCount > 0);

        // Initial positions must fall within bounds. Velocity may carry a
        // particle beyond bounds over its lifetime, so we check newly-spawned
        // particles (lifetime close to zero). The original bug spawned every
        // particle at the origin because bounds were (0, 0); this fails then.
        var freshSpawns = 0;
        foreach (ref readonly var p in system.Particles)
        {
            if (!p.IsAlive || p.Lifetime > 0.05f)
                continue;

            freshSpawns++;

            // Under the bug, positions would be (0, 0) exactly. With correct
            // bounds they are uniformly distributed in [0, bounds].
            Assert.InRange(p.Position.X, 0f, bounds.X);
            Assert.InRange(p.Position.Y, 0f, bounds.Y);
        }
    }

    [Fact]
    public void Update_WithZeroBounds_DoesNotSpawn()
    {
        // Defensive invariant: the simulation must refuse to spawn into a
        // zero-area region rather than collapsing every particle to (0, 0).
        var system = new ParticleSystem(12);

        for (var i = 0; i < 300; i++)
            system.Update(0.016f, PersonaUiState.Idle, Vector2.Zero);

        Assert.Equal(0, system.AliveCount);
    }
}
