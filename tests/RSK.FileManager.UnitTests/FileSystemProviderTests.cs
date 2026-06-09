using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RSK.FileManager.Abstractions;
using RSK.FileManager.FileSystem;
using Xunit;

namespace RSK.FileManager.UnitTests;

public sealed class FileSystemProviderTests : IDisposable
{
    private const string Secret = "this-is-a-strong-secret-key-32+chars";
    private readonly string _root;

    public FileSystemProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rskfm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private FileManagerOptions Options(bool removeEmpty = false, int expiryHours = 1, bool generateUrl = false) => new()
    {
        Provider = StorageProvider.FileSystem,
        DefaultSecureUrlExpiryHours = expiryHours,
        GenerateUrlOnUpload = generateUrl,
        FileSystem = new FileSystemOptions
        {
            RootPath = _root,
            ServeBaseUrl = "https://app.rsk.com/files",
            TokenSecret = Secret,
            RemoveEmptyFolders = removeEmpty
        }
    };

    private FileSystemProvider NewProvider(FileManagerOptions? options = null)
        => new(options ?? Options(), NullLogger<FileSystemProvider>.Instance);

    private static MemoryStream Text(string s) => new(Encoding.UTF8.GetBytes(s));

    private static FileUploadRequest Req(string path, bool overwrite = false)
        => new() { FilePath = path, Overwrite = overwrite };

    [Fact]
    public async Task Full_lifecycle_upload_exists_metadata_list_download_delete()
    {
        var p = NewProvider();

        var result = await p.UploadAsync(Req("docs/a.txt"), Text("hello"));
        result.StoredPath.Should().Be("docs/a.txt");
        result.FileSizeBytes.Should().Be(5);
        result.Provider.Should().Be("FileSystem");

        (await p.ExistsAsync("docs/a.txt")).Should().BeTrue();

        var meta = await p.GetMetadataAsync("docs/a.txt");
        meta.FileSizeBytes.Should().Be(5);
        meta.ContentType.Should().Be("text/plain");

        var list = await p.ListAsync("docs");
        list.Files.Should().ContainSingle(f => f.FilePath == "docs/a.txt");

        await using (var dl = await p.DownloadAsync("docs/a.txt"))
        {
            using var reader = new StreamReader(dl.Content);
            (await reader.ReadToEndAsync()).Should().Be("hello");
        }

        await p.DeleteAsync("docs/a.txt");
        (await p.ExistsAsync("docs/a.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_without_overwrite_throws_when_file_exists()
    {
        var p = NewProvider();
        await p.UploadAsync(Req("a.txt"), Text("one"));

        await FluentActions.Awaiting(() => p.UploadAsync(Req("a.txt"), Text("two")))
            .Should().ThrowAsync<FileManagerValidationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task Upload_with_overwrite_replaces_existing()
    {
        var p = NewProvider();
        await p.UploadAsync(Req("a.txt"), Text("one"));
        await p.UploadAsync(Req("a.txt", overwrite: true), Text("two-longer"));

        await using var dl = await p.DownloadAsync("a.txt");
        using var reader = new StreamReader(dl.Content);
        (await reader.ReadToEndAsync()).Should().Be("two-longer");
    }

    [Fact]
    public async Task Traversal_path_is_rejected_on_upload()
    {
        var p = NewProvider();
        await FluentActions.Awaiting(() => p.UploadAsync(Req("../evil.txt"), Text("x")))
            .Should().ThrowAsync<FileManagerValidationException>();
    }

    [Fact]
    public async Task Download_missing_file_throws_not_found()
    {
        var p = NewProvider();
        await FluentActions.Awaiting(() => p.DownloadAsync("nope.txt"))
            .Should().ThrowAsync<FileManagerNotFoundException>();
    }

    [Fact]
    public async Task Empty_folders_removed_only_when_enabled()
    {
        // Enabled
        var p1 = NewProvider(Options(removeEmpty: true));
        await p1.UploadAsync(Req("x/y/z.txt"), Text("hi"));
        await p1.DeleteAsync("x/y/z.txt");
        Directory.Exists(Path.Combine(_root, "x")).Should().BeFalse();

        // Disabled (default)
        var p2 = NewProvider(Options(removeEmpty: false));
        await p2.UploadAsync(Req("k/m/n.txt"), Text("hi"));
        await p2.DeleteAsync("k/m/n.txt");
        Directory.Exists(Path.Combine(_root, "k", "m")).Should().BeTrue();
    }

    [Fact]
    public async Task Secure_url_includes_expiry_when_bounded()
    {
        var p = NewProvider(Options(expiryHours: 1));
        var url = await p.GetSecureUrlAsync("docs/a.txt");

        url.ExpiresAt.Should().NotBeNull();
        url.Url.Should().Contain("expires=").And.Contain("token=");
    }

    [Fact]
    public async Task Secure_url_omits_expiry_when_never()
    {
        var p = NewProvider(Options(expiryHours: 0));   // 0 = never (allowed for FileSystem)
        var url = await p.GetSecureUrlAsync("docs/a.txt");

        url.ExpiresAt.Should().BeNull();
        url.Url.Should().Contain("token=");
        url.Url.Should().NotContain("expires=");
    }
}
