using MySqlConnector;
using PerfTest.Seeders.Sql;

namespace PerfTest.Seeders;

public sealed class MariaDbSeeder(string connectionString)
    : DapperDatabaseSeederBase(
        "mariadb",
        () => new MySqlConnection(connectionString))
{
    protected override string ResetSchemaSql => """
        DROP TABLE IF EXISTS limits;
        DROP TABLE IF EXISTS persons;

        CREATE TABLE persons (
            id VARCHAR(32) NOT NULL PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            cpf CHAR(11) NOT NULL UNIQUE,
            associate_code VARCHAR(64) NOT NULL
        ) ENGINE=InnoDB;

        CREATE TABLE limits (
            id VARCHAR(32) NOT NULL PRIMARY KEY,
            person_id VARCHAR(32) NOT NULL,
            operation_type INT NOT NULL,
            amount INT NOT NULL,
            occurred DATETIME(6) NOT NULL,
            origin_service INT NOT NULL,
            policy_number VARCHAR(64) NOT NULL
        ) ENGINE=InnoDB;
        """;

    protected override string CreateIndexesSql => """
        CREATE INDEX ix_limits_person_occurred ON limits (person_id, occurred);
        CREATE INDEX ix_limits_origin_service ON limits (origin_service);
        """;
}
