using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PersonaEngine.Lib.Bootstrapper.GpuPreflight;

/// <summary>
/// Spawns <c>nvidia-smi</c> with a short hard timeout and parses the first CSV
/// row. Callers treat <c>null</c> as "no answer" — they should not rely on the
/// reason (missing binary vs. timeout vs. non-zero exit) since users can't act
/// on the distinction.
/// </summary>
public sealed class NvidiaSmiRunner(ILogger<NvidiaSmiRunner>? log = null) : INvidiaSmiRunner
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(5);

    public async Task<NvidiaSmiResult?> QueryAsync(CancellationToken ct)
    {
        // Primary query includes compute_cap (supported on driver ≥ 470 per
        // NVIDIA's docs). If that fails we try the older 2-column variant so
        // we can still detect the GPU on ancient setups.
        var withCap = await RunAsync(
                "--query-gpu=driver_version,name,compute_cap --format=csv,noheader",
                ct
            )
            .ConfigureAwait(false);
        if (withCap is not null)
            return withCap;

        return await RunAsync("--query-gpu=driver_version,name --format=csv,noheader", ct)
            .ConfigureAwait(false);
    }

    private async Task<NvidiaSmiResult?> RunAsync(string args, CancellationToken ct)
    {
        Process? proc = null;
        try
        {
            proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            proc.Start();
        }
        catch (Exception ex)
        {
            // Most common path when the user has no NVIDIA driver: the binary
            // doesn't exist. Log at debug so this isn't noisy on developer
            // machines without CUDA — the caller will emit the user-facing
            // message.
            log?.LogDebug(ex, "nvidia-smi could not be started ({Args})", args);
            proc?.Dispose();
            return null;
        }

        using (proc)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(HardTimeout);

            string stdout;
            try
            {
                stdout = await proc
                    .StandardOutput.ReadToEndAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout path — user's CancellationToken wasn't tripped, so we
                // hit the hard cap. Kill the hung process so we don't leak a
                // child and report "no answer" to the caller.
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    /* best-effort */
                }
                log?.LogWarning("nvidia-smi timed out after {Timeout}", HardTimeout);
                return null;
            }

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return null;
            }

            var firstLine = stdout.Split('\n')[0].Trim();
            var cols = firstLine.Split(',');
            if (cols.Length < 2)
                return null;

            var driver = cols[0].Trim();
            var name = cols[1].Trim();
            var cc = cols.Length >= 3 ? cols[2].Trim() : null;
            if (string.IsNullOrWhiteSpace(driver) || string.IsNullOrWhiteSpace(name))
                return null;

            return new NvidiaSmiResult(driver, name, string.IsNullOrWhiteSpace(cc) ? null : cc);
        }
    }
}
