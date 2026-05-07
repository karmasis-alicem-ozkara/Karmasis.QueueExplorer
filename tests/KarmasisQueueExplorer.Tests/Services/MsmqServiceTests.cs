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
    public async Task GetQueuesAsync_WhenRemoteMachineProvided_BuildsRemotePrivateQueuePaths()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "orders"), "QueueName=.\\private$\\orders");
        var service = new MsmqService(NullLogger<MsmqService>.Instance, machineName => machineName == "REMOTE-SRV" ? _tempDirectory : "missing");

        var queues = await service.GetQueuesAsync("REMOTE-SRV");

        Assert.Multiple(() =>
        {
            Assert.That(queues, Has.Count.EqualTo(1));
            Assert.That(queues.Single().MachineName, Is.EqualTo("REMOTE-SRV"));
            Assert.That(queues.Single().Path, Is.EqualTo(@"REMOTE-SRV\private$\orders"));
        });
    }

    [Test]
    public async Task GetQueuesAsync_ReadsAlternateQueueMetadataFields()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "admin"), "FormatName=DIRECT=OS:REMOTE-SRV\\private$\\admin");
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "elax"), "BaseQueueName=elax");
        var service = new MsmqService(NullLogger<MsmqService>.Instance, machineName => machineName == "10.0.0.5" ? _tempDirectory : "missing");

        var queues = await service.GetQueuesAsync("10.0.0.5");

        Assert.Multiple(() =>
        {
            Assert.That(queues.Select(queue => queue.Name), Is.EqualTo(new[] { "admin", "elax" }));
            Assert.That(queues.Single(queue => queue.Name == "admin").Path, Is.EqualTo(@"10.0.0.5\private$\admin"));
            Assert.That(queues.Single(queue => queue.Name == "elax").Path, Is.EqualTo(@"10.0.0.5\private$\elax"));
        });
    }

    [Test]
    public async Task GetQueuesAsync_WhenMetadataHasNoName_UsesLqsFileName()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "elax"), "OwnerSid=S-1-5-21");
        var service = new MsmqService(NullLogger<MsmqService>.Instance, _tempDirectory);

        var queues = await service.GetQueuesAsync(".");

        Assert.Multiple(() =>
        {
            Assert.That(queues, Has.Count.EqualTo(1));
            Assert.That(queues.Single().Name, Is.EqualTo("elax"));
            Assert.That(queues.Single().Path, Is.EqualTo(@".\private$\elax"));
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
