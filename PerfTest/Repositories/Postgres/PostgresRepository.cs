using Npgsql;
using PerfTest.Repositories.Sql;

namespace PerfTest.Repositories.Postgres;

public sealed class PostgresRepository(string connectionString)
    : DapperRepositoryBase(() => new NpgsqlConnection(Tune(connectionString)))
{
    private static string Tune(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SslMode = SslMode.Disable,
            NoResetOnClose = true,
            MaxAutoPrepare = 32,
            AutoPrepareMinUsages = 2,
            MaxPoolSize = 200,
            Timeout = 15,
            CommandTimeout = 60,
            Enlist = false,
            Multiplexing = true,
            Pooling = true
        };
        return builder.ConnectionString;
    }
}
