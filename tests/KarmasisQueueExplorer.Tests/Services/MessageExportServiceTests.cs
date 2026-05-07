using KarmasisQueueExplorer.Core.Models;
using KarmasisQueueExplorer.Core.Services;

namespace KarmasisQueueExplorer.Tests.Services;

public sealed class MessageExportServiceTests
{
    private string _exportDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _exportDirectory = Path.Combine(Path.GetTempPath(), $"kqe-export-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_exportDirectory))
        {
            Directory.Delete(_exportDirectory, recursive: true);
        }
    }

    [Test]
    public async Task ExportMessageAsync_WritesReadableMessageFile()
    {
        var service = new MessageExportService(_exportDirectory);
        var message = new MessageInfo("id-1", "Order/Created", DateTime.Today, 20, "Normal", "Normal", "hello body", "hello body");

        var filePath = await service.ExportMessageAsync(message, @".\private$\orders");
        var content = await File.ReadAllTextAsync(filePath);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(filePath), Is.True);
            Assert.That(Path.GetFileName(filePath), Does.Contain("Order_Created"));
            Assert.That(content, Does.Contain(@".\private$\orders"));
            Assert.That(content, Does.Contain("Order/Created"));
            Assert.That(content, Does.Contain("hello body"));
        });
    }
}
