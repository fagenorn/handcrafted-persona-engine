using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace PersonaEngine.Lib.Bootstrapper.Sources;

public static class HttpClientFactoryExtensions
{
    public const string AssetDownloadHttpClientName = "AssetDownload";

    public static IServiceCollection AddAssetDownloadHttpClient(this IServiceCollection services)
    {
        services
            .AddHttpClient(
                AssetDownloadHttpClientName,
                c =>
                {
                    c.Timeout = TimeSpan.FromMinutes(30); // Large model files
                    c.DefaultRequestHeaders.UserAgent.ParseAdd("PersonaEngine-Bootstrapper/1.0");
                    // Defense in depth: prevent gzip from corrupting hash/size accounting
                    // when fetching binary blobs through HuggingFace Xet/LFS CDN redirects.
                    c.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");
                }
            )
            .AddPolicyHandler(
                (sp, _) =>
                {
                    var logger = sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("AssetDownloadRetry");
                    return HttpPolicyExtensions
                        .HandleTransientHttpError()
                        .Or<TaskCanceledException>(ex =>
                            !ex.CancellationToken.IsCancellationRequested
                        )
                        .WaitAndRetryAsync(
                            retryCount: 3,
                            sleepDurationProvider: attempt =>
                                TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                            onRetry: (outcome, delay, attempt, _) =>
                                logger.LogWarning(
                                    "Asset download attempt {Attempt} failed (status={Status}); retrying in {Delay}s",
                                    attempt,
                                    outcome.Result?.StatusCode,
                                    delay.TotalSeconds
                                )
                        );
                }
            );

        return services;
    }
}
