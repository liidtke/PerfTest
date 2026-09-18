using PerfTest.Data;

namespace PerfTest.Seeders;

public interface IDatabaseSeeder
{
    string DatabaseName { get; }

    Task SeedAsync(
        SqliteDatasetReader dataset,
        IProgress<SeedProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record SeedProgress(
    string Database,
    string Entity,
    long Inserted,
    long Total);
