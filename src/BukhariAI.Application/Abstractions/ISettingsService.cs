namespace BukhariAI.Application.Abstractions;

/// <summary>
/// Provides access to user-configurable settings persisted in the database.
/// </summary>
public interface ISettingsService
{
    /// <summary>Returns a dictionary of all stored settings keyed by setting name.</summary>
    Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the stored value for a key, or null if not set.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Persists a batch of key-value settings, upserting each entry.</summary>
    Task SaveAsync(Dictionary<string, string> settings, CancellationToken cancellationToken = default);
}
