using KarmasisQueueExplorer.App.ViewModels;
using KarmasisQueueExplorer.Core.Models;
using KarmasisQueueExplorer.Core.Services;

namespace KarmasisQueueExplorer.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Test]
    public async Task LoadLocalQueuesCommand_GroupsQueuesByType()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private),
            new QueueInfo("alerts", @".\private$\alerts", ".", QueueType.Private),
            new QueueInfo("deadletter", @".\system$\deadletter", ".", QueueType.DeadLetter)
        ]);
        var viewModel = new MainViewModel(service);

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Queues, Has.Count.EqualTo(3));
            Assert.That(viewModel.QueueGroups, Has.Count.EqualTo(2));
            Assert.That(viewModel.QueueGroups.Single(group => group.Name == "Private Queues").Count, Is.EqualTo(2));
            Assert.That(viewModel.QueueGroups.Single(group => group.Name == "System Queues").Count, Is.EqualTo(1));
            Assert.That(viewModel.QueueGroups.Any(group => group.Name == "Public Queues"), Is.False);
            Assert.That(viewModel.StatusMessage, Is.EqualTo("Loaded 3 local queue(s)."));
            Assert.That(viewModel.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task SelectedTreeItem_WhenQueueNodeSelected_UpdatesSelectedQueueAndStatus()
    {
        var viewModel = new MainViewModel(new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ], []))
        {
            IsAutoRefreshEnabled = false
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        var queue = viewModel.QueueGroups.Single().Children.Single();

        viewModel.SelectedTreeItem = queue;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedQueue, Is.SameAs(queue));
            Assert.That(viewModel.StatusMessage, Does.Contain("orders"));
        });
    }

    [Test]
    public async Task SelectedTreeItem_WhenQueueNodeSelected_LoadsReadableMessages()
    {
        var viewModel = new MainViewModel(new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "{ \"orderId\": 10 }", "{ \"orderId\": 10 }")
        ]))
        {
            IsAutoRefreshEnabled = false
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();

        await WaitUntilAsync(() => viewModel.Messages.Count == 1);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Messages.Single().Label, Is.EqualTo("OrderCreated"));
            Assert.That(viewModel.Messages.Single().BodyText, Does.Contain("orderId"));
            Assert.That(viewModel.SelectedMessage, Is.Not.Null);
            Assert.That(viewModel.SelectedBodyJson, Does.Contain(Environment.NewLine));
            Assert.That(viewModel.SelectedBodyHex, Does.Contain("orderId"));
            Assert.That(viewModel.LastMessageRefreshTime, Is.Not.Null);
        });
    }

    [Test]
    public async Task LoadLocalQueuesCommand_WhenServiceFails_UpdatesStatusAndClearsBusyFlag()
    {
        var viewModel = new MainViewModel(new ThrowingMsmqService());

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.StatusMessage, Does.StartWith("Failed to load queues:"));
            Assert.That(viewModel.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task LoadLocalQueuesCommand_WhenMsmqUnavailable_ShowsActionableWarning()
    {
        var viewModel = new MainViewModel(new UnavailableMsmqService());

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Queues, Is.Empty);
            Assert.That(viewModel.QueueGroups, Is.Empty);
            Assert.That(viewModel.HasWarning, Is.True);
            Assert.That(viewModel.WarningMessage, Does.Contain("Enable"));
            Assert.That(viewModel.StatusMessage, Is.EqualTo("MSMQ is unavailable."));
            Assert.That(viewModel.IsBusy, Is.False);
        });
    }

    private sealed class StubMsmqService(IReadOnlyList<QueueInfo> queues, IReadOnlyList<MessageInfo>? messages = null) : IMsmqService
    {
        public Task<IReadOnlyList<QueueInfo>> GetQueuesAsync(string machineName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(queues);
        }

        public Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(string queuePath, int maxCount = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(messages ?? []);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, cts.Token);
        }
    }

    private sealed class ThrowingMsmqService : IMsmqService
    {
        public Task<IReadOnlyList<QueueInfo>> GetQueuesAsync(string machineName, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("MSMQ unavailable");
        }

        public Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(string queuePath, int maxCount = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MessageInfo>>([]);
        }
    }

    private sealed class UnavailableMsmqService : IMsmqService
    {
        public Task<IReadOnlyList<QueueInfo>> GetQueuesAsync(string machineName, CancellationToken cancellationToken = default)
        {
            throw new MsmqUnavailableException("Enable MSMQ and refresh.");
        }

        public Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(string queuePath, int maxCount = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MessageInfo>>([]);
        }
    }
}
