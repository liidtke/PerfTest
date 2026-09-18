using global::Cassandra;

namespace PerfTest.Repositories.Cassandra;

public sealed class CassandraRepository(ISession session)
    : CassandraRepositoryBase(session);
