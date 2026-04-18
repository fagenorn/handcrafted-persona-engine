using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.LLM.Connection;

/// <summary>
///     Default <see cref="ILlmConnectionProbe" /> implementation. Uses
///     <see cref="IHttpClientFactory" /> to hit <c>/models</c> on each configured
///     endpoint, and auto-reprobes when <see cref="IOptionsMonitor{T}.OnChange" />
///     fires for the relevant <see cref="LlmOptions" /> fields.
/// </summary>
public sealed class LlmConnectionProbe : ILlmConnectionProbe, IDisposable
{
    /// <summary>
    ///     Name under which the probe's <see cref="HttpClient" /> is registered with
    ///     <see cref="IHttpClientFactory" />. DI wires the timeout on this name.
    /// </summary>
    public const string HttpClientName = "llm-probe";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<LlmConnectionProbe> _log;
    private readonly IOptionsMonitor<LlmOptions> _monitor;
    private readonly IDisposable? _onChangeSub;
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _textGate = new(1, 1);
    private readonly SemaphoreSlim _visionGate = new(1, 1);

    private LlmOptions _lastSnapshot;
    private LlmProbeResult _text = LlmProbeResult.Unknown;
    private LlmProbeResult _vision = LlmProbeResult.Unknown;

    public LlmConnectionProbe(
        IOptionsMonitor<LlmOptions> monitor,
        IHttpClientFactory httpFactory,
        ILogger<LlmConnectionProbe> log
    )
    {
        _monitor = monitor;
        _httpFactory = httpFactory;
        _log = log;
        _lastSnapshot = monitor.CurrentValue;
        _onChangeSub = monitor.OnChange(OnOptionsChanged);
    }

    public LlmProbeResult TextStatus
    {
        get
        {
            lock (_syncRoot)
            {
                return _text;
            }
        }
    }

    public LlmProbeResult VisionStatus
    {
        get
        {
            lock (_syncRoot)
            {
                return _vision;
            }
        }
    }

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
        // Caller-cancelled mid-probe: leave the prior status in place rather than
        // flapping the UI to grey "Not tested". The dedup in Store() means a
        // re-probe that lands back on the same status won't raise StatusChanged.
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
        bool changed;
        lock (_syncRoot)
        {
            if (channel == LlmChannel.Text)
            {
                changed = !SameSurface(_text, result);
                _text = result;
            }
            else
            {
                changed = !SameSurface(_vision, result);
                _vision = result;
            }
        }

        if (changed)
        {
            StatusChanged?.Invoke(channel);
        }
    }

    // Dedup transitions on the fields a consumer actually reacts to. Detail
    // text and ProbedAt change on every probe and would otherwise storm
    // StatusChanged even when the surface is unchanged. AvailableModels
    // changes rarely, but we still gate on it so a freshly-served model id
    // wakes the UI.
    private static bool SameSurface(LlmProbeResult a, LlmProbeResult b)
    {
        if (a.Status != b.Status)
        {
            return false;
        }

        var am = a.AvailableModels;
        var bm = b.AvailableModels;
        if (ReferenceEquals(am, bm))
        {
            return true;
        }

        if (am.Count != bm.Count)
        {
            return false;
        }

        for (var i = 0; i < am.Count; i++)
        {
            if (!string.Equals(am[i], bm[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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
            var http = _httpFactory.CreateClient(HttpClientName);
            using var resp = await http
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

    private void OnOptionsChanged(LlmOptions updated, string? name)
    {
        // Hold _syncRoot across read+swap so concurrent OnChange callbacks
        // can't both observe the same prev and race past a mid-edit.
        LlmOptions prev;
        lock (_syncRoot)
        {
            prev = _lastSnapshot;
            _lastSnapshot = updated;
        }

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
            FireBackgroundProbe(LlmChannel.Text);
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
            FireBackgroundProbe(LlmChannel.Vision);
        }
    }

    /// <summary>
    ///     Fire-and-forget a re-probe on an <see cref="IOptionsMonitor{T}.OnChange" /> callback.
    ///     Unhandled exceptions (e.g. <see cref="ObjectDisposedException" /> from a disposal race)
    ///     are logged rather than left unobserved.
    /// </summary>
    private void FireBackgroundProbe(LlmChannel channel)
    {
        _ = ProbeAsync(channel)
            .AsTask()
            .ContinueWith(
                t =>
                    _log.LogError(
                        t.Exception,
                        "Unhandled exception in background probe for {Channel}",
                        channel
                    ),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
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
