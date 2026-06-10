using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using RSK.FileManager.Abstractions;
using RSK.FileManager.AspNetCore;
using RSK.FileManager.FileSystem;
using Xunit;

namespace RSK.FileManager.IntegrationTests;

public sealed class FileManagerHealthCheckTests
{
    private const string Secret = "this-is-a-strong-secret-key-32+chars";

    private static FileManagerHealthCheck Build(string rootPath)
    {
        var options = new FileManagerOptions
        {
            Provider = StorageProvider.FileSystem,
            FileSystem = new FileSystemOptions
            {
                RootPath = rootPath,
                ServeBaseUrl = "https://localhost/files",
                TokenSecret = Secret
            }
        };
        var provider = new FileSystemProvider(options, NullLogger<FileSystemProvider>.Instance);
        return new FileManagerHealthCheck(options, provider);
    }

    [Fact]
    public async Task Writable_root_is_healthy()
    {
        var root = Path.Combine(Path.GetTempPath(), "rskfm-hc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await Build(root).CheckHealthAsync(new HealthCheckContext());
            result.Status.Should().Be(HealthStatus.Healthy);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Unwritable_root_is_unhealthy()
    {
        // Use a file as if it were a directory: creating a subdirectory under it fails.
        var filePath = Path.Combine(Path.GetTempPath(), "rskfm-file-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(filePath, "x");
        try
        {
            var result = await Build(Path.Combine(filePath, "sub")).CheckHealthAsync(new HealthCheckContext());
            result.Status.Should().Be(HealthStatus.Unhealthy);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
