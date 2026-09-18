using PerfTest.Data;

namespace PerfTest.Seeders;

public sealed class DatabaseSeedingManager(IEnumerable<IDatabaseSeeder> seeders)
{
    private readonly IReadOnlyDictionary<string, IDatabaseSeeder> _seeders =
        seeders.ToDictionary(seeder => seeder.DatabaseName, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> DatabaseNames => _seeders.Keys.ToArray();

    public Task SeedAsync(
        string databaseName,
        SqliteDatasetReader dataset,
        IProgress<SeedProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_seeders.TryGetValue(databaseName, out var seeder))
            throw new ArgumentException(
                $"Unknown database '{databaseName}'. Available databases: {string.Join(", ", DatabaseNames)}",
                nameof(databaseName));

        return seeder.SeedAsync(dataset, progress, cancellationToken);
    }

    public async Task SeedAllAsync(
        SqliteDatasetReader dataset,
        IProgress<SeedProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Seed sequentially by default so five databases do not compete for the
        // SQLite reader, disk bandwidth, CPU, and memory at the same time.
        foreach (var seeder in _seeders.Values)
            await seeder.SeedAsync(dataset, progress, cancellationToken);
    }
}
