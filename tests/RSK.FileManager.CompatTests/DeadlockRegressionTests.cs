using System.Text;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RSK.FileManager.Abstractions;
using RSK.FileManager.FileSystem;
using Xunit;

namespace RSK.FileManager.CompatTests;

// Guards the ConfigureAwait(false) contract: blocking synchronously on a library
// async method under a captured, single-threaded SynchronizationContext (the classic
// ASP.NET deadlock scenario) must complete, and no continuation may post back to the
// captured context.
public sealed class DeadlockRegressionTests
{
    private sealed class CapturingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);

        // Models a captured context whose only thread is blocked: posted continuations
        // would never run (a deadlock). If everything uses ConfigureAwait(false),
        // nothing is ever posted here.
        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _postCount);
    }

    [Fact]
    public void Upload_does_not_deadlock_under_single_threaded_context()
    {
        var root = Path.Combine(Path.GetTempPath(), "rskfm-deadlock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var context = new CapturingSynchronizationContext();

        try
        {
            var worker = Task.Run(() =>
            {
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    var options = new FileManagerOptions
                    {
                        Provider = StorageProvider.FileSystem,
                        GenerateUrlOnUpload = false,
                        FileSystem = new FileSystemOptions
                        {
                            RootPath = root,
                            ServeBaseUrl = "https://app.rsk.com/files",
                            TokenSecret = "this-is-a-strong-secret-key-32+chars"
                        }
                    };
                    var provider = new FileSystemProvider(options, NullLogger<FileSystemProvider>.Instance);

                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
                    // Block synchronously. Deadlocks if any await in the chain captured the context.
                    provider.UploadAsync(new FileUploadRequest { FilePath = "docs/a.txt", Overwrite = true }, ms)
                            .GetAwaiter().GetResult();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            });

            // xUnit1031: blocking here is intentional — this test verifies the library
            // does not deadlock when a caller blocks synchronously.
#pragma warning disable xUnit1031
            worker.Wait(TimeSpan.FromSeconds(15)).Should()
                .BeTrue("the library must not deadlock when blocked under a single-threaded SynchronizationContext");
#pragma warning restore xUnit1031

            context.PostCount.Should()
                .Be(0, "no continuation should post back to the captured context (ConfigureAwait(false) everywhere)");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
