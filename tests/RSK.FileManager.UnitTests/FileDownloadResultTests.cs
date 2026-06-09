using System.IO;
using FluentAssertions;
using RSK.FileManager.Abstractions;
using Xunit;

namespace RSK.FileManager.UnitTests;

public class FileDownloadResultTests
{
    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void Dispose_disposes_the_underlying_stream()
    {
        var stream = new TrackingStream();
        var result = new FileDownloadResult
        {
            Content = stream,
            ContentType = "application/pdf",
            FileSizeBytes = 0
        };

        result.Dispose();

        stream.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_disposes_the_underlying_stream()
    {
        var stream = new TrackingStream();
        var result = new FileDownloadResult
        {
            Content = stream,
            ContentType = "application/pdf",
            FileSizeBytes = 0
        };

        await result.DisposeAsync();

        stream.Disposed.Should().BeTrue();
    }
}
