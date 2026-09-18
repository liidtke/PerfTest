using System.Data.Common;
using System.Text;
using Dapper;
using PerfTest.Data;

namespace PerfTest.Seeders.Sql;

public abstract class DapperDatabaseSeederBase(
    string databaseName,
    Func<DbConnection> connectionFactory) : IDatabaseSeeder
{
    public string DatabaseName { get; } = databaseName;

    protected abstract string ResetSchemaSql { get; }
    protected abstract string CreateIndexesSql { get; }
    protected virtual string PostSeedSql => "";

    public async Task SeedAsync(
        SqliteDatasetReader dataset,
        IProgress<SeedProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            ResetSchemaSql,
            commandTimeout: 0,
            cancellationToken: cancellationToken));

        long inserted = 0;
        await foreach (var batch in dataset.ReadPersonsAsync(1_000, cancellationToken))
        {
            await InsertPersonsAsync(connection, batch, cancellationToken);
            inserted += batch.Count;
            progress?.Report(new SeedProgress(
                DatabaseName,
                "persons",
                inserted,
                SqliteDatasetGenerator.PersonCount));
        }

        inserted = 0;
        await foreach (var batch in dataset.ReadLimitsAsync(1_000, cancellationToken))
        {
            await InsertLimitsAsync(connection, batch, cancellationToken);
            inserted += batch.Count;
            progress?.Report(new SeedProgress(
                DatabaseName,
                "limits",
                inserted,
                SqliteDatasetGenerator.LimitCount));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            CreateIndexesSql,
            commandTimeout: 0,
            cancellationToken: cancellationToken));

        if (!string.IsNullOrWhiteSpace(PostSeedSql))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                PostSeedSql,
                commandTimeout: 0,
                cancellationToken: cancellationToken));
        }
    }

    private static Task<int> InsertPersonsAsync(
        DbConnection connection,
        IReadOnlyList<Person> persons,
        CancellationToken cancellationToken)
    {
        var sql = new StringBuilder(
            "INSERT INTO persons (id, name, cpf, associate_code) VALUES ");
        var parameters = new DynamicParameters();

        for (var index = 0; index < persons.Count; index++)
        {
            if (index > 0)
                sql.Append(',');
            sql.Append($"(@Id{index},@Name{index},@Cpf{index},@AssociateCode{index})");
            var person = persons[index];
            parameters.Add($"Id{index}", person.Id);
            parameters.Add($"Name{index}", person.Name);
            parameters.Add($"Cpf{index}", person.CPF);
            parameters.Add($"AssociateCode{index}", person.AssociateCode);
        }

        return connection.ExecuteAsync(new CommandDefinition(
            sql.ToString(),
            parameters,
            commandTimeout: 0,
            cancellationToken: cancellationToken));
    }

    private static Task<int> InsertLimitsAsync(
        DbConnection connection,
        IReadOnlyList<Limit> limits,
        CancellationToken cancellationToken)
    {
        var sql = new StringBuilder(
            """
            INSERT INTO limits
                (id, person_id, operation_type, amount, occurred, origin_service, policy_number)
            VALUES
            """);
        var parameters = new DynamicParameters();

        for (var index = 0; index < limits.Count; index++)
        {
            if (index > 0)
                sql.Append(',');
            sql.Append(
                $"(@Id{index},@PersonId{index},@OperationType{index},@Amount{index}," +
                $"@Occurred{index},@OriginService{index},@PolicyNumber{index})");
            var limit = limits[index];
            parameters.Add($"Id{index}", limit.Id);
            parameters.Add($"PersonId{index}", limit.PersonId);
            parameters.Add($"OperationType{index}", limit.OperationType);
            parameters.Add($"Amount{index}", limit.Amount);
            parameters.Add($"Occurred{index}", limit.Occurred);
            parameters.Add($"OriginService{index}", limit.OriginService);
            parameters.Add($"PolicyNumber{index}", limit.PolicyNumber);
        }

        return connection.ExecuteAsync(new CommandDefinition(
            sql.ToString(),
            parameters,
            commandTimeout: 0,
            cancellationToken: cancellationToken));
    }
}
