using System.Text.Json;
using System.Text.Json.Serialization;
using KarmasisQueueExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// JSON-file-backed implementation of <see cref="IConnectionProfileStore"/>.
/// Profiles are stored at <c>%LOCALAPPDATA%\KarmasisQueueExplorer\profiles.json</c>.
/// </summary>
public sealed class ConnectionProfileStore : IConnectionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _profilesFilePath;
    private readonly ILogger<ConnectionProfileStore>? _logger;

    /// <param name="profilesFilePath">
    /// Override the storage file path. When <c>null</c> (default) the standard
    /// <c>%LOCALAPPDATA%\KarmasisQueueExplorer\profiles.json</c> path is used.
    /// Injecting a custom path is the primary seam for unit tests.
    /// </param>
    /// <param name="logger">Optional structured logger.</param>
    public ConnectionProfileStore(string? profilesFilePath = null, ILogger<ConnectionProfileStore>? logger = null)
    {
        _logger = logger;

        if (profilesFilePath is not null)
        {
            _profilesFilePath = profilesFilePath;
        }
        else
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KarmasisQueueExplorer");

            Directory.CreateDirectory(appDataDir);
            _profilesFilePath = Path.Combine(appDataDir, "profiles.json");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConnectionProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_profilesFilePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_profilesFilePath);
            var dtos = await JsonSerializer.DeserializeAsync<List<ConnectionProfileDto>>(stream, JsonOptions, cancellationToken);

            return dtos?
                .Where(dto => !string.IsNullOrWhiteSpace(dto.MachineName))
                .Select(dto => new ConnectionProfile(
                    string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.MachineName : dto.DisplayName,
                    dto.MachineName))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load connection profiles from {Path}. Starting with empty profile list.", _profilesFilePath);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task SaveProfilesAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default)
    {
        try
        {
            var dtos = profiles
                .Select(p => new ConnectionProfileDto(p.DisplayName, p.MachineName))
                .ToList();

            var dir = Path.GetDirectoryName(_profilesFilePath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            await using var stream = new FileStream(_profilesFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, dtos, JsonOptions, cancellationToken);

            _logger?.LogDebug("Saved {Count} connection profile(s) to {Path}", profiles.Count, _profilesFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save connection profiles to {Path}", _profilesFilePath);
        }
    }

    private sealed record ConnectionProfileDto(
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("machineName")] string MachineName);
}
