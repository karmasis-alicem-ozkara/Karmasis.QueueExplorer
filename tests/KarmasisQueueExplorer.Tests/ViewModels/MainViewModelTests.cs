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
            Assert.That(viewModel.StatusMessage, Is.EqualTo("Loaded 3 queue(s) from ."));
            Assert.That(viewModel.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task LoadLocalQueuesCommand_UsesConfiguredTargetMachine()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @"REMOTE-SRV\private$\orders", "REMOTE-SRV", QueueType.Private)
        ]);
        var viewModel = new MainViewModel(service)
        {
            TargetMachineName = "REMOTE-SRV"
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(service.LastMachineName, Is.EqualTo("REMOTE-SRV"));
            Assert.That(viewModel.Queues.Single().MachineName, Is.EqualTo("REMOTE-SRV"));
            Assert.That(viewModel.StatusMessage, Is.EqualTo("Loaded 1 queue(s) from REMOTE-SRV"));
        });
    }

    [Test]
    public async Task LoadLocalQueuesCommand_BuildsMachineRootedQueueTree()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("ela", @"192.168.1.54\private$\ela", "192.168.1.54", QueueType.Private),
            new QueueInfo("elaalerts", @"192.168.1.54\private$\elaalerts", "192.168.1.54", QueueType.Private),
            new QueueInfo("elacmd", @"192.168.1.54\private$\elacmd", "192.168.1.54", QueueType.Private)
        ]);
        var viewModel = new MainViewModel(service)
        {
            TargetMachineName = "192.168.1.54"
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);

        var machine = viewModel.QueueTreeGroups.Single();
        var privateQueues = (QueueGroupViewModel)machine.Children.Single();

        Assert.Multiple(() =>
        {
            Assert.That(machine.Name, Is.EqualTo("192.168.1.54"));
            Assert.That(privateQueues.Name, Is.EqualTo("Private Queues"));
            Assert.That(privateQueues.Children.Cast<QueueNodeViewModel>().Select(queue => queue.Name), Is.EqualTo(new[] { "ela", "elaalerts", "elacmd" }));
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
            Assert.That(viewModel.SelectedQueue?.MessageCount, Is.EqualTo(1));
            Assert.That(viewModel.SelectedQueue?.DisplayName, Is.EqualTo("orders (1)"));
            Assert.That(viewModel.SelectedBodyJson, Does.Contain(Environment.NewLine));
            Assert.That(viewModel.SelectedBodyHex, Does.Contain("orderId"));
            Assert.That(viewModel.LastMessageRefreshTime, Is.Not.Null);
        });
    }

    [Test]
    public async Task OperationCommands_UpdateCanExecuteWhenQueueAndMessageSelectionChanges()
    {
        var viewModel = new MainViewModel(new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "body", "body")
        ]))
        {
            IsAutoRefreshEnabled = false
        };

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.RefreshMessagesCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.SendMessageCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.ExportSelectedMessageCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.DeleteSelectedMessageCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.CopySelectedMessageCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.MoveSelectedMessageCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.PurgeSelectedQueueCommand.CanExecute(null), Is.False);
        });

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await WaitUntilAsync(() => viewModel.SelectedMessage is not null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.RefreshMessagesCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.SendMessageCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.ExportSelectedMessageCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.DeleteSelectedMessageCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.PurgeSelectedQueueCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.CopySelectedMessageCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.MoveSelectedMessageCommand.CanExecute(null), Is.False);
        });

        viewModel.TargetQueuePath = @".\private$\archive";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CopySelectedMessageCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.MoveSelectedMessageCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public void QueueNodeViewModel_WhenMessageCountChanges_UpdatesDisplayName()
    {
        var viewModel = new QueueNodeViewModel(new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private));

        viewModel.MessageCount = 3;

        Assert.That(viewModel.DisplayName, Is.EqualTo("orders (3)"));
    }

    [Test]
    public async Task MessageFilterText_FiltersByLabelAndBody()
    {
        var viewModel = new MainViewModel(new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "{ \"orderId\": 10 }", "{ \"orderId\": 10 }"),
            new MessageInfo("id-2", "PaymentReceived", DateTime.Today, 55, "Normal", "Normal", "{ \"paymentId\": 22 }", "{ \"paymentId\": 22 }")
        ]))
        {
            IsAutoRefreshEnabled = false
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await WaitUntilAsync(() => viewModel.Messages.Count == 2);

        viewModel.MessageFilterText = "paymentId";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.FilteredMessages, Has.Count.EqualTo(1));
            Assert.That(viewModel.FilteredMessages.Single().Label, Is.EqualTo("PaymentReceived"));
            Assert.That(viewModel.SelectedMessage?.Label, Is.EqualTo("PaymentReceived"));
        });
    }

    [Test]
    public void AutoRefreshStatus_ReflectsEnabledStateAndInterval()
    {
        var viewModel = new MainViewModel(new StubMsmqService([]));

        viewModel.AutoRefreshIntervalSeconds = 5;

        Assert.That(viewModel.AutoRefreshStatus, Is.EqualTo("Auto-refresh: On (5s)"));

        viewModel.IsAutoRefreshEnabled = false;

        Assert.That(viewModel.AutoRefreshStatus, Is.EqualTo("Auto-refresh: Paused"));
    }

    [Test]
    public async Task SendMessageCommand_WhenQueueSelected_SendsMessageAndRefreshesList()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ], []);
        var viewModel = new MainViewModel(service)
        {
            IsAutoRefreshEnabled = false,
            NewMessageLabel = "ManualTest",
            NewMessageBody = "hello queue"
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await viewModel.SendMessageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(service.LastSentQueuePath, Is.EqualTo(@".\private$\orders"));
            Assert.That(service.LastSentLabel, Is.EqualTo("ManualTest"));
            Assert.That(service.LastSentBody, Is.EqualTo("hello queue"));
            Assert.That(viewModel.StatusMessage, Does.Contain("Loaded"));
            Assert.That(viewModel.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task ExportSelectedMessageCommand_WhenMessageSelected_ExportsMessage()
    {
        var exportService = new StubMessageExportService();
        var viewModel = new MainViewModel(new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "body", "body")
        ]), messageExportService: exportService)
        {
            IsAutoRefreshEnabled = false
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await WaitUntilAsync(() => viewModel.SelectedMessage is not null);
        await viewModel.ExportSelectedMessageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(exportService.ExportedMessage?.Label, Is.EqualTo("OrderCreated"));
            Assert.That(exportService.QueuePath, Is.EqualTo(@".\private$\orders"));
            Assert.That(viewModel.StatusMessage, Does.StartWith("Exported selected message to"));
        });
    }

    [Test]
    public async Task DeleteSelectedMessageCommand_WhenNotConfirmed_DoesNotDelete()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "body", "body")
        ]);
        var viewModel = new MainViewModel(service, dialogService: new StubDialogService(false))
        {
            IsAutoRefreshEnabled = false
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await WaitUntilAsync(() => viewModel.SelectedMessage is not null);
        await viewModel.DeleteSelectedMessageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(service.LastDeletedMessageId, Is.Null);
            Assert.That(viewModel.StatusMessage, Is.EqualTo("Delete cancelled."));
        });
    }

    [Test]
    public async Task DeleteSelectedMessageCommand_WhenConfirmed_DeletesSelectedMessage()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "body", "body")
        ]);
        var viewModel = new MainViewModel(service, dialogService: new StubDialogService(true))
        {
            IsAutoRefreshEnabled = false
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await WaitUntilAsync(() => viewModel.SelectedMessage is not null);
        await viewModel.DeleteSelectedMessageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(service.LastDeletedQueuePath, Is.EqualTo(@".\private$\orders"));
            Assert.That(service.LastDeletedMessageId, Is.EqualTo("id-1"));
            Assert.That(viewModel.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task CopySelectedMessageCommand_WhenTargetQueueProvided_CopiesWithoutDeleting()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "body", "body")
        ]);
        var viewModel = new MainViewModel(service)
        {
            IsAutoRefreshEnabled = false,
            TargetQueuePath = @".\private$\archive"
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await WaitUntilAsync(() => viewModel.SelectedMessage is not null);
        await viewModel.CopySelectedMessageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(service.LastCopiedTargetQueuePath, Is.EqualTo(@".\private$\archive"));
            Assert.That(service.LastCopiedMessage?.Id, Is.EqualTo("id-1"));
            Assert.That(service.LastDeletedMessageId, Is.Null);
            Assert.That(viewModel.StatusMessage, Does.StartWith("Copied message"));
        });
    }

    [Test]
    public async Task MoveSelectedMessageCommand_WhenNotConfirmed_DoesNotCopyOrDelete()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "body", "body")
        ]);
        var viewModel = new MainViewModel(service, dialogService: new StubDialogService(false))
        {
            IsAutoRefreshEnabled = false,
            TargetQueuePath = @".\private$\archive"
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await WaitUntilAsync(() => viewModel.SelectedMessage is not null);
        await viewModel.MoveSelectedMessageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(service.LastCopiedMessage, Is.Null);
            Assert.That(service.LastDeletedMessageId, Is.Null);
            Assert.That(viewModel.StatusMessage, Is.EqualTo("Move cancelled."));
        });
    }

    [Test]
    public async Task MoveSelectedMessageCommand_WhenConfirmed_CopiesThenDeletesSource()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "body", "body")
        ]);
        var viewModel = new MainViewModel(service, dialogService: new StubDialogService(true))
        {
            IsAutoRefreshEnabled = false,
            TargetQueuePath = @".\private$\archive"
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await WaitUntilAsync(() => viewModel.SelectedMessage is not null);
        await viewModel.MoveSelectedMessageCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(service.LastCopiedTargetQueuePath, Is.EqualTo(@".\private$\archive"));
            Assert.That(service.LastCopiedMessage?.Id, Is.EqualTo("id-1"));
            Assert.That(service.LastDeletedQueuePath, Is.EqualTo(@".\private$\orders"));
            Assert.That(service.LastDeletedMessageId, Is.EqualTo("id-1"));
            Assert.That(viewModel.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task PurgeSelectedQueueCommand_WhenNotConfirmed_DoesNotPurge()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ],
        [
            new MessageInfo("id-1", "OrderCreated", DateTime.Today, 42, "Normal", "Normal", "body", "body")
        ]);
        var viewModel = new MainViewModel(service, dialogService: new StubDialogService(false))
        {
            IsAutoRefreshEnabled = false
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await viewModel.PurgeSelectedQueueCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(service.LastPurgedQueuePath, Is.Null);
            Assert.That(viewModel.StatusMessage, Is.EqualTo("Purge cancelled."));
        });
    }

    [Test]
    public async Task PurgeSelectedQueueCommand_WhenConfirmed_PurgesQueueAndRefreshesMessages()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ], []);
        var viewModel = new MainViewModel(service, dialogService: new StubDialogService(true))
        {
            IsAutoRefreshEnabled = false
        };

        await viewModel.LoadLocalQueuesCommand.ExecuteAsync(null);
        viewModel.SelectedTreeItem = viewModel.QueueGroups.Single().Children.Single();
        await viewModel.PurgeSelectedQueueCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(service.LastPurgedQueuePath, Is.EqualTo(@".\private$\orders"));
            Assert.That(viewModel.Messages, Is.Empty);
            Assert.That(viewModel.IsBusy, Is.False);
        });
    }

    // ── Connection profile tests ─────────────────────────────────────────────

    [Test]
    public async Task InitializeAsync_LoadsProfilesFromStoreIntoSavedProfiles()
    {
        ConnectionProfile[] stored =
        [
            new ConnectionProfile("Local Machine", "."),
            new ConnectionProfile("Test Server", "TEST-SRV")
        ];
        var store = new StubConnectionProfileStore(stored);
        var viewModel = new MainViewModel(new StubMsmqService([]), connectionProfileStore: store);

        await viewModel.InitializeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SavedProfiles, Has.Count.EqualTo(2));
            Assert.That(viewModel.SavedProfiles[0].DisplayName, Is.EqualTo("Local Machine"));
            Assert.That(viewModel.SavedProfiles[1].MachineName, Is.EqualTo("TEST-SRV"));
        });
    }

    [Test]
    public async Task InitializeAsync_PreSelectsProfileMatchingDefaultMachineName()
    {
        ConnectionProfile[] stored =
        [
            new ConnectionProfile("Local Machine", "."),
            new ConnectionProfile("Test Server", "TEST-SRV")
        ];
        var store = new StubConnectionProfileStore(stored);
        var viewModel = new MainViewModel(new StubMsmqService([]), connectionProfileStore: store)
        {
            TargetMachineName = "."
        };

        await viewModel.InitializeAsync();

        Assert.That(viewModel.SelectedSavedProfile?.MachineName, Is.EqualTo("."));
    }

    [Test]
    public async Task SelectedSavedProfile_WhenChanged_UpdatesTargetMachineName()
    {
        ConnectionProfile[] stored =
        [
            new ConnectionProfile("Local Machine", "."),
            new ConnectionProfile("Build Server", "BUILD-SRV")
        ];
        var store = new StubConnectionProfileStore(stored);
        var viewModel = new MainViewModel(new StubMsmqService([]), connectionProfileStore: store);
        await viewModel.InitializeAsync();

        viewModel.SelectedSavedProfile = viewModel.SavedProfiles.Single(p => p.MachineName == "BUILD-SRV");

        Assert.That(viewModel.TargetMachineName, Is.EqualTo("BUILD-SRV"));
    }

    [Test]
    public async Task SaveCurrentProfileCommand_AddsNewProfileAndPersists()
    {
        var store = new StubConnectionProfileStore([]);
        var viewModel = new MainViewModel(new StubMsmqService([]), connectionProfileStore: store)
        {
            TargetMachineName = "NEW-SRV",
            NewProfileDisplayName = "New Server"
        };

        await viewModel.SaveCurrentProfileCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SavedProfiles, Has.Count.EqualTo(1));
            Assert.That(viewModel.SavedProfiles[0].DisplayName, Is.EqualTo("New Server"));
            Assert.That(viewModel.SavedProfiles[0].MachineName, Is.EqualTo("NEW-SRV"));
            Assert.That(store.SavedProfiles, Has.Count.EqualTo(1));
            Assert.That(viewModel.NewProfileDisplayName, Is.EqualTo(string.Empty));
            Assert.That(viewModel.StatusMessage, Does.Contain("New Server"));
        });
    }

    [Test]
    public async Task SaveCurrentProfileCommand_WhenNoDisplayName_UsesMachineNameAsDisplayName()
    {
        var store = new StubConnectionProfileStore([]);
        var viewModel = new MainViewModel(new StubMsmqService([]), connectionProfileStore: store)
        {
            TargetMachineName = "IMPLICIT-SRV",
            NewProfileDisplayName = string.Empty
        };

        await viewModel.SaveCurrentProfileCommand.ExecuteAsync(null);

        Assert.That(viewModel.SavedProfiles.Single().DisplayName, Is.EqualTo("IMPLICIT-SRV"));
    }

    [Test]
    public async Task SaveCurrentProfileCommand_WhenMachineAlreadyExists_UpdatesDisplayNameInPlace()
    {
        ConnectionProfile[] stored = [new ConnectionProfile("Old Name", "SAME-SRV")];
        var store = new StubConnectionProfileStore(stored);
        var viewModel = new MainViewModel(new StubMsmqService([]), connectionProfileStore: store);
        await viewModel.InitializeAsync();

        viewModel.TargetMachineName = "SAME-SRV";
        viewModel.NewProfileDisplayName = "Updated Name";
        await viewModel.SaveCurrentProfileCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SavedProfiles, Has.Count.EqualTo(1), "Should not add a duplicate entry");
            Assert.That(viewModel.SavedProfiles[0].DisplayName, Is.EqualTo("Updated Name"));
        });
    }

    [Test]
    public async Task RemoveSelectedProfileCommand_RemovesProfileAndPersists()
    {
        ConnectionProfile[] stored =
        [
            new ConnectionProfile("Local Machine", "."),
            new ConnectionProfile("Test Server", "TEST-SRV")
        ];
        var store = new StubConnectionProfileStore(stored);
        var viewModel = new MainViewModel(new StubMsmqService([]), connectionProfileStore: store);
        await viewModel.InitializeAsync();

        viewModel.SelectedSavedProfile = viewModel.SavedProfiles.Single(p => p.MachineName == "TEST-SRV");
        await viewModel.RemoveSelectedProfileCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SavedProfiles, Has.Count.EqualTo(1));
            Assert.That(viewModel.SavedProfiles.Any(p => p.MachineName == "TEST-SRV"), Is.False);
            Assert.That(store.SavedProfiles, Has.Count.EqualTo(1));
            Assert.That(viewModel.StatusMessage, Does.Contain("Test Server"));
        });
    }

    [Test]
    public async Task RemoveSelectedProfileCommand_CanExecute_FalseWhenNoProfileSelected()
    {
        var store = new StubConnectionProfileStore([]);
        var viewModel = new MainViewModel(new StubMsmqService([]), connectionProfileStore: store);
        await viewModel.InitializeAsync();

        Assert.That(viewModel.RemoveSelectedProfileCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task RemoveSelectedProfileCommand_CanExecute_TrueWhenProfileSelected()
    {
        ConnectionProfile[] stored = [new ConnectionProfile("Local Machine", ".")];
        var store = new StubConnectionProfileStore(stored);
        var viewModel = new MainViewModel(new StubMsmqService([]), connectionProfileStore: store);
        await viewModel.InitializeAsync();

        // InitializeAsync pre-selects the first profile
        Assert.That(viewModel.RemoveSelectedProfileCommand.CanExecute(null), Is.True);
    }

    // ── Quick-connect tests ──────────────────────────────────────────────────

    [Test]
    public async Task SelectingProfile_AfterInit_AutoConnectsToSelectedMachine()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @"REMOTE-SRV\private$\orders", "REMOTE-SRV", QueueType.Private)
        ]);
        ConnectionProfile[] stored =
        [
            new ConnectionProfile("Local Machine", "."),
            new ConnectionProfile("Remote Server", "REMOTE-SRV")
        ];
        var store = new StubConnectionProfileStore(stored);
        var viewModel = new MainViewModel(service, connectionProfileStore: store);

        await viewModel.InitializeAsync();

        // Startup: local profile pre-selected, no GetQueuesAsync call yet
        Assert.That(service.LastMachineName, Is.Null, "InitializeAsync must NOT auto-connect");

        // Act: user picks a different profile from the dropdown
        viewModel.SelectedSavedProfile = viewModel.SavedProfiles.Single(p => p.MachineName == "REMOTE-SRV");

        // The auto-connect triggers LoadLocalQueuesCommand asynchronously — wait for it
        await WaitUntilAsync(() => service.LastMachineName is not null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.TargetMachineName, Is.EqualTo("REMOTE-SRV"));
            Assert.That(service.LastMachineName, Is.EqualTo("REMOTE-SRV"));
            Assert.That(viewModel.StatusMessage, Does.Contain("REMOTE-SRV"));
        });
    }

    [Test]
    public async Task InitializeAsync_DoesNotAutoConnect_DuringStartupProfilePreSelection()
    {
        var service = new StubMsmqService(
        [
            new QueueInfo("orders", @".\private$\orders", ".", QueueType.Private)
        ]);
        ConnectionProfile[] stored =
        [
            new ConnectionProfile("Local Machine", "."),
            new ConnectionProfile("Remote Server", "REMOTE-SRV")
        ];
        var store = new StubConnectionProfileStore(stored);
        var viewModel = new MainViewModel(service, connectionProfileStore: store);

        await viewModel.InitializeAsync();

        // Profile was pre-selected during init, but GetQueuesAsync must NOT have been called
        Assert.That(service.LastMachineName, Is.Null);
        Assert.That(viewModel.StatusMessage, Is.EqualTo("Ready"));
    }

    [Test]
    public void IsNotBusy_IsTrueByDefault_AndInvertsIsBusy()
    {
        var viewModel = new MainViewModel(new StubMsmqService([]));

        Assert.That(viewModel.IsNotBusy, Is.True);
        Assert.That(viewModel.IsBusy, Is.False);
    }

    [Test]
    public async Task LoadLocalQueuesCommand_WhenServiceFails_UpdatesStatusAndClearsBusyFlag()    {
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
        public string? LastMachineName { get; private set; }
        public string? LastSentQueuePath { get; private set; }
        public string? LastSentLabel { get; private set; }
        public string? LastSentBody { get; private set; }
        public string? LastDeletedQueuePath { get; private set; }
        public string? LastDeletedMessageId { get; private set; }
        public string? LastCopiedTargetQueuePath { get; private set; }
        public MessageInfo? LastCopiedMessage { get; private set; }
        public string? LastPurgedQueuePath { get; private set; }

        public Task<IReadOnlyList<QueueInfo>> GetQueuesAsync(string machineName, CancellationToken cancellationToken = default)
        {
            LastMachineName = machineName;
            return Task.FromResult(queues);
        }

        public Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(string queuePath, int maxCount = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(messages ?? []);
        }

        public Task SendMessageAsync(string queuePath, string label, string bodyText, CancellationToken cancellationToken = default)
        {
            LastSentQueuePath = queuePath;
            LastSentLabel = label;
            LastSentBody = bodyText;
            return Task.CompletedTask;
        }

        public Task DeleteMessageAsync(string queuePath, string messageId, CancellationToken cancellationToken = default)
        {
            LastDeletedQueuePath = queuePath;
            LastDeletedMessageId = messageId;
            return Task.CompletedTask;
        }

        public Task CopyMessageAsync(string targetQueuePath, MessageInfo message, CancellationToken cancellationToken = default)
        {
            LastCopiedTargetQueuePath = targetQueuePath;
            LastCopiedMessage = message;
            return Task.CompletedTask;
        }

        public Task PurgeQueueAsync(string queuePath, CancellationToken cancellationToken = default)
        {
            LastPurgedQueuePath = queuePath;
            return Task.CompletedTask;
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

        public Task SendMessageAsync(string queuePath, string label, string bodyText, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteMessageAsync(string queuePath, string messageId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task CopyMessageAsync(string targetQueuePath, MessageInfo message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PurgeQueueAsync(string queuePath, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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

        public Task SendMessageAsync(string queuePath, string label, string bodyText, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteMessageAsync(string queuePath, string messageId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task CopyMessageAsync(string targetQueuePath, MessageInfo message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PurgeQueueAsync(string queuePath, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubMessageExportService : IMessageExportService
    {
        public MessageInfo? ExportedMessage { get; private set; }

        public string? QueuePath { get; private set; }

        public Task<string> ExportMessageAsync(MessageInfo message, string? queuePath, CancellationToken cancellationToken = default)
        {
            ExportedMessage = message;
            QueuePath = queuePath;
            return Task.FromResult(@"C:\exports\message.txt");
        }
    }

    private sealed class StubDialogService(bool confirmResult) : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(confirmResult);
        }
    }

    private sealed class StubConnectionProfileStore(IReadOnlyList<ConnectionProfile> initialProfiles) : IConnectionProfileStore
    {
        private List<ConnectionProfile> _profiles = [.. initialProfiles];

        public IReadOnlyList<ConnectionProfile> SavedProfiles => _profiles;

        public Task<IReadOnlyList<ConnectionProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ConnectionProfile>>(_profiles);
        }

        public Task SaveProfilesAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default)
        {
            _profiles = [.. profiles];
            return Task.CompletedTask;
        }
    }
}
