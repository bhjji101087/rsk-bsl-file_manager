using System.Text;
using Azure.Storage.Blobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RSK.FileManager.Abstractions;
using RSK.FileManager.AzureBlob;
using Xunit;

namespace RSK.FileManager.IntegrationTests;

// Exercises AzureBlobProvider against the local Azurite emulator (started in CI before
// this test step; for local runs, start Azurite with `npx azurite-blob` first).
public class AzureBlobProviderTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private const string Container = "rsk-files-it";

    private AzureBlobProvider _provider = null!;

    public async Task InitializeAsync()
    {
        var service = new BlobServiceClient(ConnectionString);
        await service.GetBlobContainerClient(Container).CreateIfNotExistsAsync();
        _provider = new AzureBlobProvider(NewOptions(), NullLogger<AzureBlobProvider>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static FileManagerOptions NewOptions(bool generateUrlOnUpload = false) => new()
    {
        Provider = StorageProvider.AzureBlob,
        RootContainer = Container,
        GenerateUrlOnUpload = generateUrlOnUpload,
        AzureBlob = new AzureBlobOptions
        {
            UseManagedIdentity = false,
            ConnectionString = ConnectionString
        }
    };

    [Fact]
    public async Task Upload_then_download_roundtrips_content()
    {
        var path = $"roundtrip/{Guid.NewGuid():N}.txt";
        var bytes = Encoding.UTF8.GetBytes("hello azurite");

        var uploaded = await _provider.UploadAsync(new FileUploadRequest { FilePath = path, Overwrite = true }, bytes);

        uploaded.StoredPath.Should().Be(path);
        uploaded.FileSizeBytes.Should().Be(bytes.Length);
        uploaded.ContentType.Should().Be("text/plain");
        uploaded.Provider.Should().Be("AzureBlob");

        using var download = await _provider.DownloadAsync(path);
        using var reader = new StreamReader(download.Content);
        (await reader.ReadToEndAsync()).Should().Be("hello azurite");
        download.ContentType.Should().Be("text/plain");
        download.FileSizeBytes.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task Upload_with_non_seekable_stream_buffers_content_before_upload()
    {
        var path = $"roundtrip/{Guid.NewGuid():N}.txt";
        var bytes = Encoding.UTF8.GetBytes("non-seekable upload");
        using var nonSeekable = new NonSeekableStream(new MemoryStream(bytes));

        var uploaded = await _provider.UploadAsync(new FileUploadRequest { FilePath = path, Overwrite = true }, nonSeekable);

        uploaded.FileSizeBytes.Should().Be(bytes.Length);

        using var download = await _provider.DownloadAsync(path);
        using var reader = new StreamReader(download.Content);
        (await reader.ReadToEndAsync()).Should().Be("non-seekable upload");
    }

    [Fact]
    public async Task Upload_without_overwrite_throws_when_file_already_exists()
    {
        var path = $"roundtrip/{Guid.NewGuid():N}.txt";
        var bytes = Encoding.UTF8.GetBytes("first");

        await _provider.UploadAsync(new FileUploadRequest { FilePath = path, Overwrite = true }, bytes);

        var act = () => _provider.UploadAsync(new FileUploadRequest { FilePath = path, Overwrite = false }, bytes);

        await act.Should().ThrowAsync<FileManagerValidationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task Upload_with_metadata_stores_metadata()
    {
        var path = $"roundtrip/{Guid.NewGuid():N}.txt";
        var bytes = Encoding.UTF8.GetBytes("metadata test");

        await _provider.UploadAsync(new FileUploadRequest
        {
            FilePath = path,
            Overwrite = true,
            Metadata = new Dictionary<string, string> { ["owner"] = "rsk" }
        }, bytes);

        var metadata = await _provider.GetMetadataAsync(path);

        metadata.Metadata.Should().ContainKey("owner").WhoseValue.Should().Be("rsk");
        metadata.FileSizeBytes.Should().Be(bytes.Length);
        metadata.FilePath.Should().Be(path);
    }

    [Fact]
    public async Task Upload_with_generate_url_on_upload_returns_secure_url()
    {
        var provider = new AzureBlobProvider(NewOptions(generateUrlOnUpload: true), NullLogger<AzureBlobProvider>.Instance);
        var path = $"roundtrip/{Guid.NewGuid():N}.txt";
        var bytes = Encoding.UTF8.GetBytes("secure url test");

        var uploaded = await provider.UploadAsync(new FileUploadRequest { FilePath = path, Overwrite = true }, bytes);

        uploaded.SecureUrl.Should().NotBeNullOrEmpty();
        uploaded.SecureUrl.Should().Contain("sig=");
    }

    [Fact]
    public async Task Download_missing_file_throws_not_found()
    {
        var path = $"missing/{Guid.NewGuid():N}.txt";

        var act = () => _provider.DownloadAsync(path);

        await act.Should().ThrowAsync<FileManagerNotFoundException>();
    }

    [Fact]
    public async Task Delete_removes_existing_file()
    {
        var path = $"roundtrip/{Guid.NewGuid():N}.txt";
        await _provider.UploadAsync(new FileUploadRequest { FilePath = path, Overwrite = true }, Encoding.UTF8.GetBytes("to delete"));

        await _provider.DeleteAsync(path);

        (await _provider.ExistsAsync(path)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_missing_file_throws_not_found()
    {
        var path = $"missing/{Guid.NewGuid():N}.txt";

        var act = () => _provider.DeleteAsync(path);

        await act.Should().ThrowAsync<FileManagerNotFoundException>();
    }

    [Fact]
    public async Task Exists_returns_false_for_missing_file_and_true_after_upload()
    {
        var path = $"roundtrip/{Guid.NewGuid():N}.txt";

        (await _provider.ExistsAsync(path)).Should().BeFalse();

        await _provider.UploadAsync(new FileUploadRequest { FilePath = path, Overwrite = true }, Encoding.UTF8.GetBytes("exists"));

        (await _provider.ExistsAsync(path)).Should().BeTrue();
    }

    [Fact]
    public async Task GetMetadata_missing_file_throws_not_found()
    {
        var path = $"missing/{Guid.NewGuid():N}.txt";

        var act = () => _provider.GetMetadataAsync(path);

        await act.Should().ThrowAsync<FileManagerNotFoundException>();
    }

    [Fact]
    public async Task List_returns_uploaded_files_under_prefix()
    {
        var folder = $"listing/{Guid.NewGuid():N}";
        await _provider.UploadAsync(new FileUploadRequest { FilePath = $"{folder}/a.txt", Overwrite = true }, Encoding.UTF8.GetBytes("a"));
        await _provider.UploadAsync(new FileUploadRequest { FilePath = $"{folder}/b.txt", Overwrite = true }, Encoding.UTF8.GetBytes("b"));

        var result = await _provider.ListAsync(folder);

        result.Files.Should().HaveCount(2);
        result.Files.Select(f => f.FilePath).Should().Contain(new[] { $"{folder}/a.txt", $"{folder}/b.txt" });
    }

    [Fact]
    public async Task Move_is_not_supported()
    {
        var act = () => _provider.MoveAsync("a.txt", "b.txt");
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Copy_is_not_supported()
    {
        var act = () => _provider.CopyAsync("a.txt", "b.txt");
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    // A wrapper that disables seeking, exercising AzureBlobProvider's buffering
    // path for streams that cannot report their length up front.
    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;
        public NonSeekableStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => _inner.Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
