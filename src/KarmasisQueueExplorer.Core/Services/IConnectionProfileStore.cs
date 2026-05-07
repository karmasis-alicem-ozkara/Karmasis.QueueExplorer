using KarmasisQueueExplorer.Core.Models;

namespace KarmasisQueueExplorer.Core.Services;

/// <summary>
/// Persists and retrieves named MSMQ connection profiles across sessions.
/// </summary>
public interface IConnectionProfileStore
{
    /// <summary>
    /// Loads all saved connection profiles from persistent storage.
    /// Returns an empty list when no profiles have been saved yet or the store file is missing.
    /// </summary>
    Task<IReadOnlyList<ConnectionProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the given collection of connection profiles to storage,
    /// replacing any previously saved state.
    /// </summary>
    Task SaveProfilesAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default);
}
