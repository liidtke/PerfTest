using Microsoft.Data.Sqlite;

namespace PerfTest.Benchmarking;

public sealed record StoredBenchmarkResult(
    long Id,
    DateTimeOffset CompletedAt,
    BenchmarkResult Result);

public sealed class BenchmarkResultStore
{
    public const string DefaultPath = "data/benchmark-results.sqlite";

    private readonly string _connectionString;

    public BenchmarkResultStore(string? path = null)
    {
        path ??= Environment.GetEnvironmentVariable("BenchmarkResultsPath") ?? DefaultPath;
        var fullPath = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(
            System.IO.Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The benchmark results path has no parent directory."));
        Path = fullPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath
        }.ToString();
    }

    public string Path { get; }

    public async Task SaveAsync(
        BenchmarkResult result,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO benchmark_runs (
                completed_at,
                test_type,
                database_name,
                strength,
                max_threads,
                requested_duration_ticks,
                elapsed_ticks,
                total_pass_duration_ticks,
                pass_count,
                successful_pass_count,
                failed_pass_count,
                average_pass_duration_ticks,
                p95_pass_duration_ticks,
                all_passes_succeeded,
                average_container_memory_mb
            )
            VALUES (
                $completedAt,
                $testType,
                $database,
                $strength,
                $maxThreads,
                $requestedDuration,
                $elapsed,
                $totalPassDuration,
                $passCount,
                $successfulPassCount,
                $failedPassCount,
                $averagePassDuration,
                $p95PassDuration,
                $allPassesSucceeded,
                $averageContainerMemoryMb
            );
            """;
        command.Parameters.AddWithValue("$completedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$testType", result.TestType.ToString());
        command.Parameters.AddWithValue("$database", result.Database.ToString());
        command.Parameters.AddWithValue("$strength", result.Strength);
        command.Parameters.AddWithValue("$maxThreads", result.MaxThreads);
        command.Parameters.AddWithValue("$requestedDuration", result.RequestedDuration.Ticks);
        command.Parameters.AddWithValue("$elapsed", result.TimeToCompleteAllPasses.Ticks);
        command.Parameters.AddWithValue("$totalPassDuration", result.TotalPassDuration.Ticks);
        command.Parameters.AddWithValue("$passCount", result.PassCount);
        command.Parameters.AddWithValue("$successfulPassCount", result.SuccessfulPassCount);
        command.Parameters.AddWithValue("$failedPassCount", result.FailedPassCount);
        command.Parameters.AddWithValue("$averagePassDuration", result.AveragePassDuration.Ticks);
        command.Parameters.AddWithValue("$p95PassDuration", result.P95PassDuration.Ticks);
        command.Parameters.AddWithValue("$allPassesSucceeded", result.AllPassesSucceeded ? 1 : 0);
        command.Parameters.AddWithValue(
            "$averageContainerMemoryMb",
            result.AverageContainerMemoryMb);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredBenchmarkResult?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        var results = await QueryAsync(null, null, 1, cancellationToken);
        return results.SingleOrDefault();
    }

    public Task<IReadOnlyList<StoredBenchmarkResult>> GetByDatabaseAsync(
        Database database,
        CancellationToken cancellationToken = default) =>
        QueryAsync("database_name", database.ToString(), null, cancellationToken);

    public Task<IReadOnlyList<StoredBenchmarkResult>> GetByTestTypeAsync(
        TestType testType,
        CancellationToken cancellationToken = default) =>
        QueryAsync("test_type", testType.ToString(), null, cancellationToken);

    private async Task<IReadOnlyList<StoredBenchmarkResult>> QueryAsync(
        string? filterColumn,
        string? filterValue,
        int? limit,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT
                id,
                completed_at,
                test_type,
                database_name,
                strength,
                max_threads,
                requested_duration_ticks,
                elapsed_ticks,
                total_pass_duration_ticks,
                pass_count,
                successful_pass_count,
                failed_pass_count,
                average_pass_duration_ticks,
                p95_pass_duration_ticks,
                all_passes_succeeded,
                average_container_memory_mb
            FROM benchmark_runs
            {(filterColumn is null ? string.Empty : $"WHERE {filterColumn} = $filterValue")}
            ORDER BY id DESC
            {(limit is null ? string.Empty : "LIMIT $limit")};
            """;
        if (filterColumn is not null)
            command.Parameters.AddWithValue("$filterValue", filterValue!);
        if (limit is not null)
            command.Parameters.AddWithValue("$limit", limit.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<StoredBenchmarkResult>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var result = new BenchmarkResult(
                Enum.Parse<TestType>(reader.GetString(2)),
                Enum.Parse<Database>(reader.GetString(3)),
                reader.GetInt32(4),
                reader.GetInt32(5),
                TimeSpan.FromTicks(reader.GetInt64(6)),
                TimeSpan.FromTicks(reader.GetInt64(7)),
                TimeSpan.FromTicks(reader.GetInt64(8)),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.GetInt64(11),
                TimeSpan.FromTicks(reader.GetInt64(12)),
                TimeSpan.FromTicks(reader.GetInt64(13)),
                reader.GetInt64(14) != 0)
            {
                AverageContainerMemoryMb = reader.GetDouble(15)
            };

            results.Add(new StoredBenchmarkResult(
                reader.GetInt64(0),
                DateTimeOffset.Parse(
                    reader.GetString(1),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                result));
        }

        return results;
    }

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS benchmark_runs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                completed_at TEXT NOT NULL,
                test_type TEXT NOT NULL,
                database_name TEXT NOT NULL,
                strength INTEGER NOT NULL,
                max_threads INTEGER NOT NULL,
                requested_duration_ticks INTEGER NOT NULL,
                elapsed_ticks INTEGER NOT NULL,
                total_pass_duration_ticks INTEGER NOT NULL,
                pass_count INTEGER NOT NULL,
                successful_pass_count INTEGER NOT NULL,
                failed_pass_count INTEGER NOT NULL,
                average_pass_duration_ticks INTEGER NOT NULL,
                p95_pass_duration_ticks INTEGER NOT NULL,
                all_passes_succeeded INTEGER NOT NULL,
                average_container_memory_mb REAL NOT NULL DEFAULT 0
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText = "PRAGMA table_info(benchmark_runs);";
        await using var reader = await schemaCommand.ExecuteReaderAsync(cancellationToken);
        var hasMemoryColumn = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetString(1) == "average_container_memory_mb")
            {
                hasMemoryColumn = true;
                break;
            }
        }

        await reader.DisposeAsync();
        if (!hasMemoryColumn)
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText =
                """
                ALTER TABLE benchmark_runs
                ADD COLUMN average_container_memory_mb REAL NOT NULL DEFAULT 0;
                """;
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
