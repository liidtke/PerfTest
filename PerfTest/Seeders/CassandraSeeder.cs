using global::Cassandra;

namespace PerfTest.Seeders;

public sealed class CassandraSeeder(ISession session)
    : CassandraDatabaseSeederBase("cassandra", session);
