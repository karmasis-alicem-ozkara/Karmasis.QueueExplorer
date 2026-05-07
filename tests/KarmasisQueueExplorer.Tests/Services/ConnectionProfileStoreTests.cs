using KarmasisQueueExplorer.Core.Models;
using KarmasisQueueExplorer.Core.Services;

namespace KarmasisQueueExplorer.Tests.Services;

public sealed class ConnectionProfileStoreTests
{
    private string _profilesFilePath = string.Empty;
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"KQE_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _profilesFilePath = Path.Combine(_tempDir, "profiles.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── Load ────────────────────────────────────────────────────────────────

    [Test]
    public async Task LoadProfilesAsync_WhenFileDoesNotExist_ReturnsEmptyList()
    {
        var store = new ConnectionProfileStore(_profilesFilePath);

        var result = await store.LoadProfilesAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task LoadProfilesAsync_WhenFileIsEmpty_ReturnsEmptyList()
    {
        File.WriteAllText(_profilesFilePath, string.Empty);
        var store = new ConnectionProfileStore(_profilesFilePath);

        var result = await store.LoadProfilesAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task LoadProfilesAsync_WhenFileIsCorrupt_ReturnsEmptyListGracefully()
    {
        File.WriteAllText(_profilesFilePath, "{ not valid json !!!!");
        var store = new ConnectionProfileStore(_profilesFilePath);

        var result = await store.LoadProfilesAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task LoadProfilesAsync_WhenFileHasValidProfiles_ReturnsAllProfiles()
    {
        var store = new ConnectionProfileStore(_profilesFilePath);
        await store.SaveProfilesAsync(
        [
            new ConnectionProfile("Local Machine", "."),
            new ConnectionProfile("Build Server", "BUILD-SRV-01")
        ]);

        var result = await store.LoadProfilesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].DisplayName, Is.EqualTo("Local Machine"));
            Assert.That(result[0].MachineName, Is.EqualTo("."));
            Assert.That(result[1].DisplayName, Is.EqualTo("Build Server"));
            Assert.That(result[1].MachineName, Is.EqualTo("BUILD-SRV-01"));
        });
    }

    [Test]
    public async Task LoadProfilesAsync_WhenProfileHasNoDisplayName_UsesMachineNameAsDisplayName()
    {
        // Write a JSON file where displayName is empty to exercise the fallback path.
        File.WriteAllText(_profilesFilePath, """
            [{ "displayName": "", "machineName": "REMOTE-01" }]
            """);
        var store = new ConnectionProfileStore(_profilesFilePath);

        var result = await store.LoadProfilesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DisplayName, Is.EqualTo("REMOTE-01"));
        });
    }

    [Test]
    public async Task LoadProfilesAsync_SkipsEntriesWithBlankMachineName()
    {
        File.WriteAllText(_profilesFilePath, """
            [
              { "displayName": "Valid", "machineName": "VALID-SRV" },
              { "displayName": "Empty", "machineName": "" },
              { "displayName": "Whitespace", "machineName": "   " }
            ]
            """);
        var store = new ConnectionProfileStore(_profilesFilePath);

        var result = await store.LoadProfilesAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].MachineName, Is.EqualTo("VALID-SRV"));
    }

    // ── Save ────────────────────────────────────────────────────────────────

    [Test]
    public async Task SaveProfilesAsync_CreatesFileWithSerializedProfiles()
    {
        var store = new ConnectionProfileStore(_profilesFilePath);

        await store.SaveProfilesAsync([new ConnectionProfile("Local Machine", ".")]);

        Assert.That(File.Exists(_profilesFilePath), Is.True);
        var content = await File.ReadAllTextAsync(_profilesFilePath);
        Assert.That(content, Does.Contain("Local Machine"));
        Assert.That(content, Does.Contain("."));
    }

    [Test]
    public async Task SaveProfilesAsync_OverwritesPreviouslySavedProfiles()
    {
        var store = new ConnectionProfileStore(_profilesFilePath);
        await store.SaveProfilesAsync([new ConnectionProfile("Old Profile", "OLD-SRV")]);

        await store.SaveProfilesAsync([new ConnectionProfile("New Profile", "NEW-SRV")]);

        var result = await store.LoadProfilesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DisplayName, Is.EqualTo("New Profile"));
        });
    }

    [Test]
    public async Task SaveAndLoad_RoundTripsMultipleProfiles()
    {
        var store = new ConnectionProfileStore(_profilesFilePath);
        ConnectionProfile[] original =
        [
            new ConnectionProfile("Local Machine", "."),
            new ConnectionProfile("Test Server", "TEST-SRV"),
            new ConnectionProfile("Production", "PROD-SRV-01")
        ];

        await store.SaveProfilesAsync(original);
        var loaded = await store.LoadProfilesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Has.Count.EqualTo(3));
            for (var i = 0; i < original.Length; i++)
            {
                Assert.That(loaded[i].DisplayName, Is.EqualTo(original[i].DisplayName));
                Assert.That(loaded[i].MachineName, Is.EqualTo(original[i].MachineName));
            }
        });
    }

    [Test]
    public async Task SaveProfilesAsync_WithEmptyList_ProducesEmptyJsonArray()
    {
        var store = new ConnectionProfileStore(_profilesFilePath);

        await store.SaveProfilesAsync([]);

        var loaded = await store.LoadProfilesAsync();
        Assert.That(loaded, Is.Empty);
    }
}
