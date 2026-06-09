using FluentAssertions;
using RSK.FileManager.Abstractions;
using RSK.FileManager.Core;
using Xunit;

namespace RSK.FileManager.UnitTests;

public class PathSanitizerTests
{
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config")]
    [InlineData("foo/../../bar")]
    [InlineData("a/../b")]
    public void Traversal_is_rejected(string path)
    {
        FluentActions.Invoking(() => PathSanitizer.Validate(path))
            .Should().Throw<FileManagerValidationException>()
            .WithMessage("*..*");
    }

    [Fact]
    public void Null_byte_is_rejected()
    {
        FluentActions.Invoking(() => PathSanitizer.Validate("a\0b.txt"))
            .Should().Throw<FileManagerValidationException>();
    }

    [Fact]
    public void Drive_letter_is_rejected()
    {
        FluentActions.Invoking(() => PathSanitizer.Validate("C:/Windows/evil.dll"))
            .Should().Throw<FileManagerValidationException>()
            .WithMessage("*:*");
    }

    [Fact]
    public void Overlong_path_is_rejected()
    {
        var path = new string('a', PathSanitizer.MaxPathLength + 1);
        FluentActions.Invoking(() => PathSanitizer.Validate(path))
            .Should().Throw<FileManagerValidationException>()
            .WithMessage("*maximum length*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_empty_is_rejected(string? path)
    {
        FluentActions.Invoking(() => PathSanitizer.Validate(path!))
            .Should().Throw<FileManagerValidationException>();
    }

    [Theory]
    [InlineData("/invoices/2026/inv001.pdf", "invoices/2026/inv001.pdf")]
    [InlineData("\\invoices\\2026\\inv001.pdf", "invoices/2026/inv001.pdf")]
    [InlineData("invoices//2026///inv001.pdf", "invoices/2026/inv001.pdf")]
    [InlineData("  reports/q1.xlsx  ", "reports/q1.xlsx")]
    public void Valid_paths_are_normalized(string input, string expected)
    {
        PathSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Fact]
    public void Dots_inside_a_filename_are_allowed()
    {
        PathSanitizer.Sanitize("docs/my..weird.name.pdf")
            .Should().Be("docs/my..weird.name.pdf");
    }
}
