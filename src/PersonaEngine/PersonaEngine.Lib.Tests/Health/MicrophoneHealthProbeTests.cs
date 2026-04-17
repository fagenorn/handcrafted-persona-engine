using Microsoft.Extensions.Options;
using NSubstitute;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Health;
using PersonaEngine.Lib.Health.Probes;
using Xunit;

namespace PersonaEngine.Lib.Tests.Health;

public class MicrophoneHealthProbeTests
{
    private static IOptionsMonitor<MicrophoneConfiguration> Monitor(string? deviceName)
    {
        var monitor = Substitute.For<IOptionsMonitor<MicrophoneConfiguration>>();
        monitor.CurrentValue.Returns(new MicrophoneConfiguration { DeviceName = deviceName });
        monitor
            .OnChange(Arg.Any<Action<MicrophoneConfiguration, string?>>())
            .Returns((IDisposable?)null);
        return monitor;
    }

    private static IMicrophone Mic(string? currentDevice, params string[] available)
    {
        var mic = Substitute.For<IMicrophone>();
        mic.CurrentDeviceName.Returns(currentDevice);
        mic.AvailableDevices.Returns(available);
        return mic;
    }

    [Fact]
    public void Healthy_WhenConfiguredDeviceIsOpen()
    {
        var mic = Mic("My USB Mic", "My USB Mic", "Default");
        var monitor = Monitor("My USB Mic");

        using var probe = new MicrophoneHealthProbe(mic, monitor);

        Assert.Equal(SubsystemHealth.Healthy, probe.Current.Health);
        Assert.Contains("My USB Mic", probe.Current.Label);
    }

    [Fact]
    public void Degraded_WhenConfiguredDeviceMissing_FellBackToDefault()
    {
        var mic = Mic("Default", "Other");
        var monitor = Monitor("Mic X");

        using var probe = new MicrophoneHealthProbe(mic, monitor);

        Assert.Equal(SubsystemHealth.Degraded, probe.Current.Health);
        Assert.Equal("Fell back to default", probe.Current.Label);
    }

    [Fact]
    public void Failed_WhenNoInputDevices()
    {
        var mic = Mic(null);
        var monitor = Monitor("");

        using var probe = new MicrophoneHealthProbe(mic, monitor);

        Assert.Equal(SubsystemHealth.Failed, probe.Current.Health);
        Assert.Equal("No input devices available", probe.Current.Label);
    }

    [Fact]
    public void StatusChanged_Fires_OnlyOnTransition()
    {
        // Start: device exists and is open → Healthy
        var mic = Substitute.For<IMicrophone>();
        mic.AvailableDevices.Returns(new[] { "My Mic" });
        mic.CurrentDeviceName.Returns("My Mic");

        var monitorSub = Substitute.For<IOptionsMonitor<MicrophoneConfiguration>>();
        monitorSub.CurrentValue.Returns(new MicrophoneConfiguration { DeviceName = "My Mic" });
        Action<MicrophoneConfiguration, string?>? capturedCallback = null;
        monitorSub
            .OnChange(Arg.Any<Action<MicrophoneConfiguration, string?>>())
            .Returns(ci =>
            {
                capturedCallback = ci.Arg<Action<MicrophoneConfiguration, string?>>();
                return Substitute.For<IDisposable>();
            });

        using var probe = new MicrophoneHealthProbe(mic, monitorSub);
        var fired = 0;
        probe.StatusChanged += _ => fired++;

        // Trigger refresh with no change — should not fire
        capturedCallback?.Invoke(new MicrophoneConfiguration { DeviceName = "My Mic" }, null);
        Assert.Equal(0, fired);

        // Now the device disappears
        mic.AvailableDevices.Returns(Array.Empty<string>());
        capturedCallback?.Invoke(new MicrophoneConfiguration { DeviceName = "My Mic" }, null);
        Assert.Equal(1, fired);
    }
}
