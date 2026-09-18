using Npgsql;
using PerfTest.Seeders.Sql;

namespace PerfTest.Seeders;

public sealed class PostgresSeeder(string connectionString)
    : DapperDatabaseSeederBase(
        "postgres",
        () => new NpgsqlConnection(connectionString))
{
    protected override string ResetSchemaSql => """
        SET synchronous_commit = off;
        DROP TABLE IF EXISTS limits;
        DROP TABLE IF EXISTS persons;

        CREATE TABLE persons (
            id VARCHAR(32) PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            cpf VARCHAR(11) NOT NULL UNIQUE,
            associate_code VARCHAR(64) NOT NULL
        );

        CREATE TABLE limits (
            id VARCHAR(32) PRIMARY KEY,
            person_id VARCHAR(32) NOT NULL,
            operation_type INTEGER NOT NULL,
            amount INTEGER NOT NULL,
            occurred TIMESTAMPTZ NOT NULL,
            origin_service INTEGER NOT NULL,
            policy_number VARCHAR(64) NOT NULL
        );
        """;

    protected override string CreateIndexesSql => """
        CREATE INDEX ix_limits_person_occurred ON limits (person_id, occurred);
        CREATE INDEX ix_limits_origin_service ON limits (origin_service);
        """;

    protected override string PostSeedSql => """
        CREATE EXTENSION IF NOT EXISTS pg_prewarm;
        SELECT pg_prewarm(c.oid)
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public'
          AND c.relkind IN ('r', 'i');
        ANALYZE persons;
        ANALYZE limits;
        """;
}
