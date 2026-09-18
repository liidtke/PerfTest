using global::Cassandra;
using MongoDB.Driver;
using PerfTest;
using PerfTest.Benchmarking;
using PerfTest.Data;
using PerfTest.Seeders;

const string defaultDatasetPath = "data/perf-test-dataset.sqlite";
using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

try
{
    if (args.Length == 0)
    {
        await BenchmarkTui.RunAsync(
            (target, token) => SeedDatabasesAsync(target, defaultDatasetPath, token),
            cancellationSource.Token);
        return;
    }

    switch (args[0].ToLowerInvariant())
    {
        case "benchmark":
            await BenchmarkTui.RunBenchmarkAsync(cancellationSource.Token);
            break;
        case "generate":
        {
            var path = args.ElementAtOrDefault(1) ?? defaultDatasetPath;
            var generator = new SqliteDatasetGenerator();
            await generator.GenerateAsync(
                path,
                new Progress<string>(Console.WriteLine),
                cancellationSource.Token);
            break;
        }
        case "seed":
        {
            var target = args.ElementAtOrDefault(1)?.ToLowerInvariant() ?? "all";
            var path = args.ElementAtOrDefault(2) ?? defaultDatasetPath;
            await SeedDatabasesAsync(target, path, cancellationSource.Token);

            break;
        }
        default:
            PrintUsage();
            Environment.ExitCode = 2;
            break;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation cancelled.");
    Environment.ExitCode = 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

static async Task SeedDatabasesAsync(
    string target,
    string datasetPath,
    CancellationToken cancellationToken)
{
    if (!File.Exists(datasetPath))
        throw new FileNotFoundException(
            $"Dataset '{Path.GetFullPath(datasetPath)}' does not exist. Run the generate command first.",
            datasetPath);

    var targets = target == "all"
        ? ComposeDatabaseRuntime.AllServiceNames
        : [target];
    if (target != "all" &&
        !ComposeDatabaseRuntime.AllServiceNames.Contains(target, StringComparer.OrdinalIgnoreCase))
    {
        throw new ArgumentException(
            $"Unknown target '{target}'. Expected: all, {string.Join(", ", ComposeDatabaseRuntime.AllServiceNames)}");
    }

    var dataset = new SqliteDatasetReader(datasetPath);
    var progress = new Progress<SeedProgress>(ReportSeedProgress);
    foreach (var database in targets)
    {
        await using var _ = await ComposeDatabaseRuntime.StartAsync(database, cancellationToken);
        var (seeders, resources) = CreateSeeders(database);
        try
        {
            var manager = new DatabaseSeedingManager(seeders);
            await manager.SeedAsync(database, dataset, progress, cancellationToken);
        }
        finally
        {
            for (var index = resources.Count - 1; index >= 0; index--)
                resources[index].Dispose();
        }
    }
}

static (IReadOnlyList<IDatabaseSeeder> Seeders, List<IDisposable> Resources) CreateSeeders(
    string target)
{
    var seeders = new List<IDatabaseSeeder>();
    var resources = new List<IDisposable>();

    switch (target.ToLowerInvariant())
    {
        case "mariadb":
            seeders.Add(new MariaDbSeeder(GetConnectionString(
                "MariaDb",
                "Server=localhost;Port=3306;Database=perf_test;User=perf_test;Password=perf_test")));
            break;
        case "postgres":
            seeders.Add(new PostgresSeeder(GetConnectionString(
                "Postgres",
                "Host=localhost;Port=5432;Database=perf_test;Username=perf_test;Password=perf_test")));
            break;
        case "mongodb":
        {
            var client = new MongoClient(GetConnectionString(
                "MongoDb",
                "mongodb://perf_test:perf_test@localhost:27017/perf_test?authSource=admin"));
            seeders.Add(new MongoDbSeeder(client.GetDatabase("perf_test")));
            break;
        }
        case "cassandra":
            AddCassandraSeeder(
                "Cassandra",
                "127.0.0.1:9042",
                session => new CassandraSeeder(session),
                seeders,
                resources);
            break;
        case "scylladb":
            AddCassandraSeeder(
                "ScyllaDb",
                "127.0.0.1:9043",
                session => new ScyllaDbSeeder(session),
                seeders,
                resources);
            break;
        default:
            throw new ArgumentException(
                $"Unknown target '{target}'. Expected: {string.Join(", ", ComposeDatabaseRuntime.AllServiceNames)}");
    }

    return (seeders, resources);
}

static void AddCassandraSeeder(
    string connectionName,
    string fallback,
    Func<ISession, IDatabaseSeeder> factory,
    ICollection<IDatabaseSeeder> seeders,
    ICollection<IDisposable> resources)
{
    var endpoint = GetConnectionString(connectionName, fallback);
    var separator = endpoint.LastIndexOf(':');
    var host = separator < 0 ? endpoint : endpoint[..separator];
    var port = separator < 0 ? 9042 : int.Parse(endpoint[(separator + 1)..]);
    var cluster = Cluster.Builder()
        .AddContactPoint(host)
        .WithPort(port)
        .Build();
    var session = cluster.Connect();
    resources.Add(cluster);
    resources.Add(session);
    seeders.Add(factory(session));
}

static string GetConnectionString(string name, string fallback) =>
    Environment.GetEnvironmentVariable($"ConnectionStrings__{name}") ?? fallback;

static void ReportSeedProgress(SeedProgress progress)
{
    if (progress.Inserted == progress.Total ||
        progress.Entity == "persons" && progress.Inserted % 100_000 == 0 ||
        progress.Entity == "limits" && progress.Inserted % 500_000 == 0)
    {
        Console.WriteLine(
            $"[{progress.Database}] {progress.Entity}: " +
            $"{progress.Inserted:N0}/{progress.Total:N0}");
    }
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Usage:
          PerfTest
          PerfTest benchmark
          PerfTest generate [sqlite-path]
          PerfTest seed [all|mariadb|postgres|mongodb|cassandra|scylladb] [sqlite-path]

        Run without arguments (or use benchmark) to open the interactive benchmark UI.
        Default SQLite path: data/perf-test-dataset.sqlite
        """);
}