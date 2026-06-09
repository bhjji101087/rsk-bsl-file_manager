using Azure;
using Polly;
using Polly.Retry;

namespace RSK.FileManager.AzureBlob;

/// <summary>
/// Builds the Polly resilience pipeline used by the Azure provider: exponential
/// backoff over transient <see cref="RequestFailedException"/> status codes.
/// </summary>
internal static class RetryPolicies
{
    /// <summary>Transient HTTP status codes that are safe to retry.</summary>
    internal static bool IsTransient(RequestFailedException ex)
        => ex.Status is 429 or 500 or 503;

    /// <summary>
    /// Creates a retry pipeline. <paramref name="maxAttempts"/> retries with an
    /// exponential delay starting at <paramref name="baseDelaySeconds"/> (2s, 4s, 8s...).
    /// </summary>
    public static ResiliencePipeline Build(int maxAttempts, int baseDelaySeconds)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<RequestFailedException>(IsTransient),
                MaxRetryAttempts = Math.Max(0, maxAttempts),
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(Math.Max(1, baseDelaySeconds)),
                UseJitter = true
            })
            .Build();
    }
}
