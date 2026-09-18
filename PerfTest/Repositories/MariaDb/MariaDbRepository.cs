using MySqlConnector;
using PerfTest.Repositories.Sql;

namespace PerfTest.Repositories.MariaDb;

public sealed class MariaDbRepository(string connectionString)
    : DapperRepositoryBase(() => new MySqlConnection(connectionString));
