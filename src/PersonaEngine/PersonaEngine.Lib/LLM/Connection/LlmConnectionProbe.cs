using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.LLM.Connection;

public sealed class LlmConnectionProbe : ILlmConnectionProbe, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<LlmConnectionProbe> _log;
    private readonly IOptionsMonitor<LlmOptions> _monitor;
    private readonly IDisposable? _onChangeSub;
    private readonly SemaphoreSlim _textGate = new(1, 1);
    private readonly SemaphoreSlim _visionGate = new(1, 1);

    private LlmOptions _lastSnapshot;
    private LlmProbeResult _text = LlmProbeResult.Unknown;
    private LlmProbeResult _vision = LlmProbeResult.Unknown;

    public LlmConnectionProbe(
        IOptionsMonitor<LlmOptions> monitor,
        HttpClient http,
        ILogger<LlmConnectionProbe> log
    )
    {
        _monitor = monitor;
        _http = http;
        _log = log;
        _lastSnapshot = monitor.CurrentValue;
        _onChangeSub = monitor.OnChange(OnOptionsChanged);
    }

    public LlmProbeResult TextStatus => _text;

    public LlmProbeResult VisionStatus => _vision;

    public event Action<LlmChannel>? StatusChanged;

    public async ValueTask ProbeAsync(LlmChannel channel, CancellationToken ct = default)
    {
        var opts = _monitor.CurrentValue;

        if (channel == LlmChannel.Vision && !opts.VisionEnabled)
        {
            Store(
                channel,
                new LlmProbeResult(
                    LlmProbeStatus.Disabled,
                    null,
                    Array.Empty<string>(),
                    DateTimeOffset.UtcNow
                )
            );
            return;
        }

        var gate = channel == LlmChannel.Text ? _textGate : _visionGate;
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Store(
                channel,
                new LlmProbeResult(
                    LlmProbeStatus.Probing,
                    null,
                    Array.Empty<string>(),
                    DateTimeOffset.UtcNow
                )
            );

            var (endpoint, model, apiKey) = ExtractCreds(channel, opts);
            var result = await ProbeEndpointAsync(endpoint, model, apiKey, ct)
                .ConfigureAwait(false);
            Store(channel, result);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        _onChangeSub?.Dispose();
        _textGate.Dispose();
        _visionGate.Dispose();
    }

    private static (string Endpoint, string Model, string ApiKey) ExtractCreds(
        LlmChannel channel,
        LlmOptions opts
    ) =>
        channel == LlmChannel.Text
            ? (opts.TextEndpoint, opts.TextModel, opts.TextApiKey)
            : (opts.VisionEndpoint, opts.VisionModel, opts.VisionApiKey);

    private void Store(LlmChannel channel, LlmProbeResult result)
    {
        if (channel == LlmChannel.Text)
            _text = result;
        else
            _vision = result;
        StatusChanged?.Invoke(channel);
    }

    private async Task<LlmProbeResult> ProbeEndpointAsync(
        string endpoint,
        string model,
        string apiKey,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
        )
        {
            return new LlmProbeResult(
                LlmProbeStatus.InvalidUrl,
                "Not a valid http/https URL.",
                Array.Empty<string>(),
                DateTimeOffset.UtcNow
            );
        }

        var url = $"{endpoint.TrimEnd('/')}/models";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                apiKey
            );
        }

        try
        {
            using var resp = await _http
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new LlmProbeResult(
                    LlmProbeStatus.Unauthorized,
                    $"HTTP {(int)resp.StatusCode}",
                    Array.Empty<string>(),
                    DateTimeOffset.UtcNow
                );
            }

            if (!resp.IsSuccessStatusCode)
            {
                return new LlmProbeResult(
                    LlmProbeStatus.Unreachable,
                    $"HTTP {(int)resp.StatusCode}",
                    Array.Empty<string>(),
                    DateTimeOffset.UtcNow
                );
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var models = ParseModels(stream);
            var hit = models.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));
            return new LlmProbeResult(
                hit ? LlmProbeStatus.Reachable : LlmProbeStatus.ModelMissing,
                hit ? null : $"'{model}' not served by this endpoint.",
                models,
                DateTimeOffset.UtcNow
            );
        }
        catch (TaskCanceledException tce) when (!ct.IsCancellationRequested)
        {
            return new LlmProbeResult(
                LlmProbeStatus.Unreachable,
                $"timed out ({tce.Message})",
                Array.Empty<string>(),
                DateTimeOffset.UtcNow
            );
        }
        catch (HttpRequestException ex)
        {
            return new LlmProbeResult(
                LlmProbeStatus.Unreachable,
                ex.Message,
                Array.Empty<string>(),
                DateTimeOffset.UtcNow
            );
        }
    }

    private static IReadOnlyList<string> ParseModels(Stream json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (
                !doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array
            )
            {
                return Array.Empty<string>();
            }

            var models = new List<string>(data.GetArrayLength());
            foreach (var element in data.EnumerateArray())
            {
                if (
                    element.TryGetProperty("id", out var idProp)
                    && idProp.ValueKind == JsonValueKind.String
                )
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id))
                        models.Add(id);
                }
            }
            return models;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private void FireAndForget(LlmChannel channel)
    {
        var task = ProbeAsync(channel).AsTask();
        _ = task;
    }

    private void OnOptionsChanged(LlmOptions updated, string? _)
    {
        var prev = _lastSnapshot;
        _lastSnapshot = updated;

        if (
            !SameTuple(
                prev.TextEndpoint,
                prev.TextModel,
                prev.TextApiKey,
                updated.TextEndpoint,
                updated.TextModel,
                updated.TextApiKey
            )
        )
        {
            FireAndForget(LlmChannel.Text);
        }

        var prevVisionOn = prev.VisionEnabled;
        var nextVisionOn = updated.VisionEnabled;
        if (
            prevVisionOn != nextVisionOn
            || (
                nextVisionOn
                && !SameTuple(
                    prev.VisionEndpoint,
                    prev.VisionModel,
                    prev.VisionApiKey,
                    updated.VisionEndpoint,
                    updated.VisionModel,
                    updated.VisionApiKey
                )
            )
        )
        {
            FireAndForget(LlmChannel.Vision);
        }
    }

    private static bool SameTuple(
        string ae,
        string am,
        string ak,
        string be,
        string bm,
        string bk
    ) =>
        string.Equals(ae, be, StringComparison.Ordinal)
        && string.Equals(am, bm, StringComparison.Ordinal)
        && string.Equals(ak, bk, StringComparison.Ordinal);
}
