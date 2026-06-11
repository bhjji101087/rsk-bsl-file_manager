using RSK.FileManager.Abstractions;

namespace RSK.FileManager.Core;

/// <summary>
/// Validates uploads: size, extension whitelist, and (optionally) file content by
/// magic bytes so a renamed executable cannot bypass an extension check.
/// </summary>
public static class FileValidator
{
    private const int HeaderSize = 8;

    // Known signatures for the extensions we accept. The leading bytes of the file
    // must match one of these for the claimed extension.
    private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]  = new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } },                 // %PDF
        [".png"]  = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } },                 // .PNG
        [".jpg"]  = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },                       // JPEG
        [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".gif"]  = new[] { new byte[] { 0x47, 0x49, 0x46, 0x38 } },                 // GIF8
        // OOXML (docx/xlsx/pptx) and zip are all PK zip containers.
        [".zip"]  = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        [".docx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        [".xlsx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        [".pptx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
    };

    // Signatures that are blocked regardless of the file name.
    private static readonly byte[][] BlockedSignatures =
    {
        new byte[] { 0x4D, 0x5A },             // "MZ" — Windows PE executable / DLL
        new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, // 0x7F E L F — Linux ELF executable
    };

    /// <summary>Throws if the size exceeds the configured maximum.</summary>
    public static void ValidateSize(long sizeBytes, FileManagerOptions options)
    {
        if (sizeBytes < 0)
            throw new FileManagerValidationException("File size cannot be negative.");

        if (sizeBytes > options.MaxFileSizeBytes)
            throw new FileManagerValidationException(
                $"File size {sizeBytes} bytes exceeds the maximum of {options.MaxFileSizeBytes} bytes.");
    }

    /// <summary>
    /// Throws if the file extension is not in the configured whitelist.
    /// An empty whitelist allows any extension.
    /// </summary>
    public static void ValidateExtension(string path, FileManagerOptions options)
    {
        if (options.AllowedExtensions is null || options.AllowedExtensions.Length == 0)
            return;

        var ext = Path.GetExtension(path);
        foreach (var allowed in options.AllowedExtensions)
        {
            if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase))
                return;
        }

        throw new FileManagerValidationException(
            $"File extension '{ext}' is not allowed.");
    }

    /// <summary>
    /// Sniffs the leading bytes of the content. Blocks known-dangerous signatures
    /// (executables) regardless of name, and rejects content whose signature does not
    /// match its claimed extension. The stream position is restored afterward so the
    /// upload body is intact.
    /// </summary>
    /// <exception cref="FileManagerValidationException">
    /// Thrown when the stream is not seekable, the content is an executable, or the
    /// content does not match the claimed extension.
    /// </exception>
    public static async Task ValidateContentAsync(Stream content, string path, CancellationToken cancellationToken)
    {
        if (content is null)
            throw new FileManagerValidationException("Content stream is null.");

        if (!content.CanSeek)
            throw new FileManagerValidationException(
                "Content stream must be seekable for content sniffing. " +
                "Buffer the content (e.g. to a MemoryStream) before upload, or disable EnableContentSniffing.");

        var startPosition = content.Position;

        var header = new byte[HeaderSize];
        var read = await ReadAtLeastAsync(content, header, cancellationToken).ConfigureAwait(false);

        content.Seek(startPosition, SeekOrigin.Begin);

        foreach (var blocked in BlockedSignatures)
        {
            if (StartsWith(header, read, blocked))
                throw new FileManagerValidationException("Executable content is not permitted.");
        }

        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && Signatures.TryGetValue(ext, out var expected))
        {
            var matched = false;
            foreach (var sig in expected)
            {
                if (StartsWith(header, read, sig)) { matched = true; break; }
            }

            if (!matched)
                throw new FileManagerValidationException(
                    $"File content does not match its '{ext}' extension.");
        }
    }

    private static bool StartsWith(byte[] buffer, int length, byte[] signature)
    {
        if (length < signature.Length)
            return false;

        for (var i = 0; i < signature.Length; i++)
        {
            if (buffer[i] != signature[i])
                return false;
        }

        return true;
    }

    // A single ReadAsync may return fewer bytes than requested; loop until the buffer
    // is filled or the stream ends. Returns the number of bytes actually read.
    private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer, total, buffer.Length - total, cancellationToken).ConfigureAwait(false);
            if (n == 0)
                break;
            total += n;
        }

        return total;
    }
}
