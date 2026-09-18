using global::Cassandra;

namespace PerfTest.Seeders;

public sealed class ScyllaDbSeeder(ISession session)
    : CassandraDatabaseSeederBase("scylladb", session);
