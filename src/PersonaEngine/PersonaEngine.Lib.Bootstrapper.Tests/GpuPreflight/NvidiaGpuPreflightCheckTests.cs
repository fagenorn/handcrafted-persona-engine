using FluentAssertions;
using NSubstitute;
using PersonaEngine.Lib.Bootstrapper.GpuPreflight;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.GpuPreflight;

/// <summary>
/// Covers the decision matrix of <see cref="NvidiaGpuPreflightCheck" /> against
/// fake nvidia-smi output + fake nvcuda probe. The intent is to lock in the
/// version floor (580.65 / compute 6.0) so a future driver bump doesn't silently
/// reclassify a previously-supported rig as failing.
/// </summary>
public sealed class NvidiaGpuPreflightCheckTests
{
    [Theory]
    [InlineData("580.65", 580, 65)]
    [InlineData("580.65.01", 580, 65)]
    [InlineData("  580.65 ", 580, 65)]
    [InlineData("999", 999, 0)]
    [InlineData("12.4", 12, 4)]
    public void TryParseVersion_parses_common_shapes(string input, int major, int minor)
    {
        var parsed = NvidiaGpuPreflightCheck.TryParseVersion(input);
        parsed.Should().Be((major, minor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("abc.def")]
    public void TryParseVersion_rejects_junk(string? input)
    {
        NvidiaGpuPreflightCheck.TryParseVersion(input).Should().BeNull();
    }

    [Fact]
    public async Task Pass_when_modern_driver_and_compute()
    {
        var check = Build(
            smi: Smi("580.65.01", "NVIDIA GeForce RTX 4080", "8.9"),
            nvcudaLoads: false
        );

        var status = await check.InspectAsync(CancellationToken.None);

        status.Ok.Should().BeTrue();
        status.Kind.Should().Be(GpuFailureKind.None);
        status.DriverVersion.Should().Be("580.65.01");
        status.GpuName.Should().Be("NVIDIA GeForce RTX 4080");
        status.ComputeCapability.Should().Be((8, 9));
    }

    [Fact]
    public async Task Pass_when_compute_cap_absent()
    {
        // Old drivers omit compute_cap. We must still pass if driver is recent.
        var check = Build(
            smi: Smi("580.70", "NVIDIA Pascal Card", computeCap: null),
            nvcudaLoads: false
        );

        var status = await check.InspectAsync(CancellationToken.None);

        status.Ok.Should().BeTrue();
        status.ComputeCapability.Should().BeNull();
    }

    [Fact]
    public async Task Fail_DriverTooOld_when_below_minimum()
    {
        var check = Build(smi: Smi("560.35.02", "NVIDIA RTX 3090", "8.6"), nvcudaLoads: false);

        var status = await check.InspectAsync(CancellationToken.None);

        status.Ok.Should().BeFalse();
        status.Kind.Should().Be(GpuFailureKind.DriverTooOld);
        status.DriverVersion.Should().Be("560.35.02");
        status.Detail.Should().Contain("580");
    }

    [Fact]
    public async Task Fail_DriverTooOld_just_below_threshold()
    {
        // Boundary check: 580.64 must fail, 580.65 must pass.
        var below = Build(smi: Smi("580.64", "GPU", "7.5"), nvcudaLoads: false);
        var at = Build(smi: Smi("580.65", "GPU", "7.5"), nvcudaLoads: false);

        (await below.InspectAsync(CancellationToken.None))
            .Kind.Should()
            .Be(GpuFailureKind.DriverTooOld);
        (await at.InspectAsync(CancellationToken.None)).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Fail_ComputeTooLow_on_Maxwell()
    {
        var check = Build(smi: Smi("580.65.01", "NVIDIA Quadro M2000", "5.2"), nvcudaLoads: false);

        var status = await check.InspectAsync(CancellationToken.None);

        status.Ok.Should().BeFalse();
        status.Kind.Should().Be(GpuFailureKind.ComputeTooLow);
        status.ComputeCapability.Should().Be((5, 2));
    }

    [Fact]
    public async Task Pass_compute_exactly_at_Pascal_floor()
    {
        // 6.0 is the documented floor — must pass.
        var check = Build(smi: Smi("580.65.01", "NVIDIA GTX 1080", "6.0"), nvcudaLoads: false);

        var status = await check.InspectAsync(CancellationToken.None);

        status.Ok.Should().BeTrue();
        status.ComputeCapability.Should().Be((6, 0));
    }

    [Fact]
    public async Task Fail_NoDriver_when_smi_missing_and_nvcuda_not_loadable()
    {
        var check = Build(smi: null, nvcudaLoads: false);

        var status = await check.InspectAsync(CancellationToken.None);

        status.Ok.Should().BeFalse();
        status.Kind.Should().Be(GpuFailureKind.NoDriver);
        status.Detail.Should().Contain("NVIDIA");
    }

    [Fact]
    public async Task Fail_ToolMissing_when_smi_missing_but_nvcuda_loads()
    {
        // Edge case: driver installed but nvidia-smi not on PATH. We can't
        // verify version, so we warn but flag it differently from NoDriver.
        var check = Build(smi: null, nvcudaLoads: true);

        var status = await check.InspectAsync(CancellationToken.None);

        status.Ok.Should().BeFalse();
        status.Kind.Should().Be(GpuFailureKind.ToolMissing);
    }

    [Fact]
    public async Task Fail_UnknownGpu_when_smi_reports_unparseable_driver()
    {
        var check = Build(smi: Smi("garbage", "Some GPU", null), nvcudaLoads: true);

        var status = await check.InspectAsync(CancellationToken.None);

        status.Ok.Should().BeFalse();
        status.Kind.Should().Be(GpuFailureKind.UnknownGpu);
        status.DriverVersion.Should().Be("garbage");
        status.GpuName.Should().Be("Some GPU");
    }

    // ---- helpers ----

    private static NvidiaGpuPreflightCheck Build(NvidiaSmiResult? smi, bool nvcudaLoads)
    {
        var smiRunner = Substitute.For<INvidiaSmiRunner>();
        smiRunner.QueryAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(smi));

        var probe = Substitute.For<INvcudaProbe>();
        probe.TryLoadNvcuda().Returns(nvcudaLoads);

        return new NvidiaGpuPreflightCheck(smiRunner, probe);
    }

    private static NvidiaSmiResult Smi(string driver, string name, string? computeCap) =>
        new(driver, name, computeCap);
}
