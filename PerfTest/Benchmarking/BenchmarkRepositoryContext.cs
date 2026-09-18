using global::Cassandra;
using MongoDB.Driver;
using PerfTest.Repositories.Cassandra;
using PerfTest.Repositories.MariaDb;
using PerfTest.Repositories.MongoDb;
using PerfTest.Repositories.Postgres;
using PerfTest.Repositories.ScyllaDb;

namespace PerfTest.Benchmarking;

internal sealed class BenchmarkRepositoryContext : IAsyncDisposable
{
    private readonly List<IDisposable> _resources = [];

    private BenchmarkRepositoryContext(IRepository repository)
    {
        Repository = repository;
    }

    public IRepository Repository { get; }

    public static BenchmarkRepositoryContext Create(Database database) =>
        database switch
        {
            Database.MariaDb => new BenchmarkRepositoryContext(
                new MariaDbRepository(GetConnectionString(
                    "MariaDb",
                    "Server=localhost;Port=3306;Database=perf_test;User=perf_test;Password=perf_test"))),
            Database.Postgres => new BenchmarkRepositoryContext(
                new PostgresRepository(GetConnectionString(
                    "Postgres",
                    "Host=localhost;Port=5432;Database=perf_test;Username=perf_test;Password=perf_test"))),
            Database.MongoDb => CreateMongoDb(),
            Database.Cassandra => CreateCassandra(
                "Cassandra",
                "127.0.0.1:9042",
                session => new CassandraRepository(session)),
            Database.ScyllaDb => CreateCassandra(
                "ScyllaDb",
                "127.0.0.1:9043",
                session => new ScyllaDbRepository(session)),
            _ => throw new ArgumentOutOfRangeException(nameof(database), database, "Unknown database.")
        };

    public ValueTask DisposeAsync()
    {
        for (var index = _resources.Count - 1; index >= 0; index--)
            _resources[index].Dispose();
        return ValueTask.CompletedTask;
    }

    private static BenchmarkRepositoryContext CreateMongoDb()
    {
        var client = new MongoClient(GetConnectionString(
            "MongoDb",
            "mongodb://perf_test:perf_test@localhost:27017/perf_test?authSource=admin"));
        return new BenchmarkRepositoryContext(
            new MongoDbRepository(client.GetDatabase("perf_test")));
    }

    private static BenchmarkRepositoryContext CreateCassandra(
        string connectionName,
        string fallback,
        Func<ISession, IRepository> repositoryFactory)
    {
        var endpoint = GetConnectionString(connectionName, fallback);
        var separator = endpoint.LastIndexOf(':');
        var host = separator < 0 ? endpoint : endpoint[..separator];
        var port = separator < 0 ? 9042 : int.Parse(endpoint[(separator + 1)..]);
        var cluster = Cluster.Builder()
            .AddContactPoint(host)
            .WithPort(port)
            .Build();
        var session = cluster.Connect("perf_test");
        var context = new BenchmarkRepositoryContext(repositoryFactory(session));
        context._resources.Add(cluster);
        context._resources.Add(session);
        return context;
    }

    private static string GetConnectionString(string name, string fallback) =>
        Environment.GetEnvironmentVariable($"ConnectionStrings__{name}") ?? fallback;
}
