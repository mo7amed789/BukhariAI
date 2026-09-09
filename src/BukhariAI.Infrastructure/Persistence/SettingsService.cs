using BukhariAI.Application.Abstractions;
using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BukhariAI.Infrastructure.Persistence;

public sealed class SettingsService : ISettingsService
{
    private readonly BukhariDbContext _db;

    public SettingsService(BukhariDbContext db) => _db = db;

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.AppSettings.ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Key, r => r.Value);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        return row?.Value;
    }

    public async Task SaveAsync(Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        foreach (var (key, value) in settings)
        {
            var existing = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (existing is null)
            {
                _db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAtUtc = DateTime.UtcNow });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }
}
