using FluentAssertions;
using RSK.FileManager.Abstractions;
using Xunit;

namespace RSK.FileManager.UnitTests;

public class ExceptionHierarchyTests
{
    [Fact]
    public void All_specific_exceptions_derive_from_base()
    {
        new FileManagerNotFoundException().Should().BeAssignableTo<FileManagerException>();
        new FileManagerValidationException().Should().BeAssignableTo<FileManagerException>();
        new FileManagerProviderException().Should().BeAssignableTo<FileManagerException>();
        new FileManagerConfigException().Should().BeAssignableTo<FileManagerException>();
    }

    [Fact]
    public void Transient_is_a_provider_exception()
    {
        new FileManagerTransientException()
            .Should().BeAssignableTo<FileManagerProviderException>()
            .And.BeAssignableTo<FileManagerException>();
    }

    [Fact]
    public void Message_and_inner_are_preserved()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new FileManagerProviderException("storage failed", inner);

        ex.Message.Should().Be("storage failed");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ForPath_factory_includes_the_path()
    {
        FileManagerNotFoundException.ForPath("a/b/c.pdf")
            .Message.Should().Contain("a/b/c.pdf");
    }
}
