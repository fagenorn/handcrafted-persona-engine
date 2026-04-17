using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.LLM.Connection;
using Xunit;

namespace PersonaEngine.Lib.Tests.LLM.Connection;

public class LlmConnectionProbeTests
{
    private const string ModelsJson =
        "{\"data\":[{\"id\":\"llama-3.1-8b-instruct\"},{\"id\":\"mistral-7b\"}]}";

    private static IOptionsMonitor<LlmOptions> Monitor(LlmOptions seed)
    {
        var monitor = Substitute.For<IOptionsMonitor<LlmOptions>>();
        monitor.CurrentValue.Returns(_ => seed);
        monitor.OnChange(Arg.Any<Action<LlmOptions, string?>>()).Returns((IDisposable?)null);
        return monitor;
    }

    private static LlmConnectionProbe Make(StubHandler handler, LlmOptions seed)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return new LlmConnectionProbe(Monitor(seed), http, NullLogger<LlmConnectionProbe>.Instance);
    }

    [Fact]
    public async Task Probe_Reachable_WhenModelsListed()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ModelsJson, Encoding.UTF8, "application/json"),
        });
        var opts = new LlmOptions
        {
            TextEndpoint = "http://localhost:1234/v1",
            TextModel = "llama-3.1-8b-instruct",
        };
        var probe = Make(handler, opts);

        await probe.ProbeAsync(LlmChannel.Text);

        Assert.Equal(LlmProbeStatus.Reachable, probe.TextStatus.Status);
        Assert.Contains("llama-3.1-8b-instruct", probe.TextStatus.AvailableModels);
    }

    [Fact]
    public async Task Probe_ModelMissing_WhenConfiguredModelNotInList()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ModelsJson, Encoding.UTF8, "application/json"),
        });
        var opts = new LlmOptions
        {
            TextEndpoint = "http://localhost:1234/v1",
            TextModel = "gpt-does-not-exist",
        };
        var probe = Make(handler, opts);

        await probe.ProbeAsync(LlmChannel.Text);

        Assert.Equal(LlmProbeStatus.ModelMissing, probe.TextStatus.Status);
    }

    [Fact]
    public async Task Probe_Unauthorized_On401()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var opts = new LlmOptions
        {
            TextEndpoint = "https://api.openai.com/v1",
            TextModel = "gpt-4o-mini",
        };
        var probe = Make(handler, opts);

        await probe.ProbeAsync(LlmChannel.Text);

        Assert.Equal(LlmProbeStatus.Unauthorized, probe.TextStatus.Status);
    }

    [Fact]
    public async Task Probe_Unreachable_OnConnectException()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connect refused"));
        var opts = new LlmOptions { TextEndpoint = "http://localhost:9/v1", TextModel = "x" };
        var probe = Make(handler, opts);

        await probe.ProbeAsync(LlmChannel.Text);

        Assert.Equal(LlmProbeStatus.Unreachable, probe.TextStatus.Status);
    }

    [Fact]
    public async Task Probe_Unreachable_OnTimeout()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));
        var opts = new LlmOptions { TextEndpoint = "http://localhost:9/v1", TextModel = "x" };
        var probe = Make(handler, opts);

        await probe.ProbeAsync(LlmChannel.Text);

        Assert.Equal(LlmProbeStatus.Unreachable, probe.TextStatus.Status);
        Assert.Contains(
            "time",
            probe.TextStatus.DetailMessage ?? "",
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task Probe_InvalidUrl_RejectsGarbage()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var opts = new LlmOptions { TextEndpoint = "not-a-url", TextModel = "x" };
        var probe = Make(handler, opts);

        await probe.ProbeAsync(LlmChannel.Text);

        Assert.Equal(LlmProbeStatus.InvalidUrl, probe.TextStatus.Status);
    }

    [Fact]
    public async Task Probe_VisionDisabled_ShortCircuits_NoHttpTraffic()
    {
        var callCount = 0;
        var handler = new StubHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var opts = new LlmOptions
        {
            TextEndpoint = "http://localhost:1234/v1",
            TextModel = "x",
            VisionEnabled = false,
            VisionEndpoint = "http://localhost:1234/v1",
            VisionModel = "x",
        };
        var probe = Make(handler, opts);

        await probe.ProbeAsync(LlmChannel.Vision);

        Assert.Equal(LlmProbeStatus.Disabled, probe.VisionStatus.Status);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task StatusChanged_FiresTwice_EntryAndTerminal()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ModelsJson, Encoding.UTF8, "application/json"),
        });
        var opts = new LlmOptions
        {
            TextEndpoint = "http://localhost:1234/v1",
            TextModel = "llama-3.1-8b-instruct",
        };
        var probe = Make(handler, opts);
        var fired = new List<LlmProbeStatus>();
        probe.StatusChanged += ch =>
        {
            if (ch == LlmChannel.Text)
                fired.Add(probe.TextStatus.Status);
        };

        await probe.ProbeAsync(LlmChannel.Text);

        Assert.Equal(2, fired.Count);
        Assert.Equal(LlmProbeStatus.Probing, fired[0]);
        Assert.Equal(LlmProbeStatus.Reachable, fired[1]);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) =>
            _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(_factory(request));
    }
}
