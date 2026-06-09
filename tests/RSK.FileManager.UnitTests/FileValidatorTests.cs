using System.IO;
using FluentAssertions;
using RSK.FileManager.Abstractions;
using RSK.FileManager.Core;
using Xunit;

namespace RSK.FileManager.UnitTests;

public class FileValidatorTests
{
    private static readonly byte[] PdfHeader = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 }; // %PDF-1.7
    private static readonly byte[] ExeHeader = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 }; // MZ...

    // ── Size ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Oversize_is_rejected()
    {
        var options = new FileManagerOptions { MaxFileSizeBytes = 100 };
        FluentActions.Invoking(() => FileValidator.ValidateSize(101, options))
            .Should().Throw<FileManagerValidationException>();
    }

    [Fact]
    public void Within_size_passes()
    {
        var options = new FileManagerOptions { MaxFileSizeBytes = 100 };
        FluentActions.Invoking(() => FileValidator.ValidateSize(100, options)).Should().NotThrow();
    }

    // ── Extension whitelist ──────────────────────────────────────────────────

    [Fact]
    public void Disallowed_extension_is_rejected()
    {
        var options = new FileManagerOptions { AllowedExtensions = new[] { ".pdf", ".png" } };
        FluentActions.Invoking(() => FileValidator.ValidateExtension("a/b/data.exe", options))
            .Should().Throw<FileManagerValidationException>();
    }

    [Fact]
    public void Allowed_extension_passes_case_insensitively()
    {
        var options = new FileManagerOptions { AllowedExtensions = new[] { ".pdf" } };
        FluentActions.Invoking(() => FileValidator.ValidateExtension("a/b/REPORT.PDF", options))
            .Should().NotThrow();
    }

    [Fact]
    public void Empty_whitelist_allows_any_extension()
    {
        var options = new FileManagerOptions { AllowedExtensions = Array.Empty<string>() };
        FluentActions.Invoking(() => FileValidator.ValidateExtension("a/b/anything.xyz", options))
            .Should().NotThrow();
    }

    // ── Content sniffing ─────────────────────────────────────────────────────

    [Fact]
    public async Task Renamed_executable_is_rejected()
    {
        using var stream = new MemoryStream(ExeHeader);
        await FluentActions.Awaiting(() => FileValidator.ValidateContentAsync(stream, "report.pdf", default))
            .Should().ThrowAsync<FileManagerValidationException>()
            .WithMessage("*xecutable*");
    }

    [Fact]
    public async Task Matching_pdf_passes()
    {
        using var stream = new MemoryStream(PdfHeader);
        await FluentActions.Awaiting(() => FileValidator.ValidateContentAsync(stream, "report.pdf", default))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Content_not_matching_extension_is_rejected()
    {
        // PNG bytes presented as a .pdf
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        using var stream = new MemoryStream(png);
        await FluentActions.Awaiting(() => FileValidator.ValidateContentAsync(stream, "report.pdf", default))
            .Should().ThrowAsync<FileManagerValidationException>()
            .WithMessage("*does not match*");
    }

    [Fact]
    public async Task Stream_position_is_restored_after_sniffing()
    {
        using var stream = new MemoryStream(PdfHeader) { Position = 0 };
        await FileValidator.ValidateContentAsync(stream, "report.pdf", default);
        stream.Position.Should().Be(0);
    }

    [Fact]
    public async Task Nonseekable_stream_is_rejected()
    {
        using var inner = new MemoryStream(PdfHeader);
        using var nonSeekable = new NonSeekableStream(inner);
        await FluentActions.Awaiting(() => FileValidator.ValidateContentAsync(nonSeekable, "report.pdf", default))
            .Should().ThrowAsync<FileManagerValidationException>()
            .WithMessage("*seekable*");
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;
        public NonSeekableStream(Stream inner) => _inner = inner;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
