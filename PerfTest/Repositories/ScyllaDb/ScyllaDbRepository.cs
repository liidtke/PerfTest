using global::Cassandra;
using PerfTest.Repositories.Cassandra;

namespace PerfTest.Repositories.ScyllaDb;

public sealed class ScyllaDbRepository(ISession session)
    : CassandraRepositoryBase(session);
