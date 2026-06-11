using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using RSK.FileManager.Abstractions;

namespace RSK.FileManager.AspNetCore;

/// <summary>
/// Health check for RSK.FileManager: verifies the FileSystem root is writable, or the
/// Azure container is reachable.
/// </summary>
public sealed class FileManagerHealthCheck : IHealthCheck
{
    private const string Sentinel = "__rskfm_healthcheck__";

    private readonly FileManagerOptions _options;
    private readonly IFileManagerService _service;

    /// <summary>Creates the health check.</summary>
    public FileManagerHealthCheck(FileManagerOptions options, IFileManagerService service)
    {
        _options = options;
        _service = service;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_options.Provider == StorageProvider.FileSystem)
            {
                var root = _options.FileSystem.RootPath;
                Directory.CreateDirectory(root);
                var probe = Path.Combine(root, Sentinel + "-" + Guid.NewGuid().ToString("N"));
                await File.WriteAllTextAsync(probe, "ok", cancellationToken).ConfigureAwait(false);
                File.Delete(probe);
                return HealthCheckResult.Healthy("FileSystem root is writable.");
            }

            // Azure: a lightweight existence check reaches the container; a missing blob is fine,
            // an unreachable/missing container surfaces as an exception.
            await _service.ExistsAsync(Sentinel, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Azure Blob container is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RSK.FileManager health check failed.", ex);
        }
    }
}

/// <summary>Registration helper for the RSK.FileManager health check.</summary>
public static class FileManagerHealthCheckExtensions
{
    /// <summary>Adds the RSK.FileManager health check to the health-checks pipeline.</summary>
    public static IHealthChecksBuilder AddFileManagerHealthCheck(
        this IHealthChecksBuilder builder, string name = "filemanager")
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        return builder.AddCheck<FileManagerHealthCheck>(name);
    }
}
