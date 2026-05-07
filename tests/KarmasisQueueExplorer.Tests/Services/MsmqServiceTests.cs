using KarmasisQueueExplorer.Core.Models;
using KarmasisQueueExplorer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KarmasisQueueExplorer.Tests.Services;

public sealed class MsmqServiceTests
{
    private string _tempDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"kqe-lqs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Test]
    public async Task GetQueuesAsync_ReadsQueueNamesFromLqsMetadata()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "orders"), "QueueName=orders");
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "alerts"), "QueueName=.\\private$\\alerts");
        var service = new MsmqService(NullLogger<MsmqService>.Instance, _tempDirectory);

        var queues = await service.GetQueuesAsync(".");

        Assert.Multiple(() =>
        {
            Assert.That(queues.Select(queue => queue.Name), Is.EqualTo(new[] { "alerts", "orders" }));
            Assert.That(queues.All(queue => queue.Type == QueueType.Private), Is.True);
            Assert.That(queues.Single(queue => queue.Name == "orders").Path, Is.EqualTo(@".\private$\orders"));
        });
    }

    [Test]
    public void GetQueuesAsync_WhenLqsDirectoryMissing_ThrowsMsmqUnavailableException()
    {
        var missingPath = Path.Combine(_tempDirectory, "missing");
        var service = new MsmqService(NullLogger<MsmqService>.Instance, missingPath);

        var exception = Assert.ThrowsAsync<MsmqUnavailableException>(async () => await service.GetQueuesAsync("."));

        Assert.That(exception!.Message, Does.Contain("MSMQ appears to be disabled"));
    }
}
