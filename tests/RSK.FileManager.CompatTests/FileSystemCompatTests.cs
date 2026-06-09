using System.Collections.Specialized;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RSK.FileManager;
using RSK.FileManager.Abstractions;
using RSK.FileManager.FileSystem;
using Xunit;

namespace RSK.FileManager.CompatTests;

// Proves the public API compiles and runs on classic .NET Framework 4.6.2.
public sealed class FileSystemCompatTests : IDisposable
{
    private const string Secret = "this-is-a-strong-secret-key-32+chars";
    private readonly string _root;

    public FileSystemCompatTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rskfm-compat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    private FileManagerOptions Options() => new()
    {
        Provider = StorageProvider.FileSystem,
        GenerateUrlOnUpload = false,
        FileSystem = new FileSystemOptions
        {
            RootPath = _root,
            ServeBaseUrl = "https://app.rsk.com/files",
            TokenSecret = Secret
        }
    };

    [Fact]
    public async Task Provider_round_trips_on_net462()
    {
        var provider = new FileSystemProvider(Options(), NullLogger<FileSystemProvider>.Instance);

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("hello")))
        {
            await provider.UploadAsync(new FileUploadRequest { FilePath = "docs/a.txt" }, ms);
        }

        (await provider.ExistsAsync("docs/a.txt")).Should().BeTrue();

        using var dl = await provider.DownloadAsync("docs/a.txt");
        using var reader = new StreamReader(dl.Content);
        (await reader.ReadToEndAsync()).Should().Be("hello");
    }

    [Fact]
    public async Task Factory_initializes_and_serves_on_net462()
    {
        var settings = new NameValueCollection
        {
            ["FileManager:Provider"] = "FileSystem",
            ["FileManager:GenerateUrlOnUpload"] = "false",
            ["FileManager:FileSystem:RootPath"] = _root,
            ["FileManager:FileSystem:ServeBaseUrl"] = "https://app.rsk.com/files",
            ["FileManager:FileSystem:TokenSecret"] = Secret
        };

        FileManagerFactory.Initialize(settings);
        var service = FileManagerFactory.GetService();

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("via-factory")))
        {
            await service.UploadAsync(new FileUploadRequest { FilePath = "f/b.txt" }, ms);
        }

        (await service.ExistsAsync("f/b.txt")).Should().BeTrue();
    }
}
