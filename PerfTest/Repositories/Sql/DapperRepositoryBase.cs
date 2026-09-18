using System.Data.Common;
using Dapper;

namespace PerfTest.Repositories.Sql;

public abstract class DapperRepositoryBase(Func<DbConnection> connectionFactory) : IRepository
{
    public async Task<Person?> GetPersonAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, name, cpf, associate_code AS AssociateCode
            FROM persons
            WHERE id = @Id
            """;

        await using var connection = connectionFactory();
        return await connection.QuerySingleOrDefaultAsync<Person>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<Person?> GetPersonByCpfAsync(
        string cpf,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, name, cpf, associate_code AS AssociateCode
            FROM persons
            WHERE cpf = @Cpf
            """;

        await using var connection = connectionFactory();
        return await connection.QuerySingleOrDefaultAsync<Person>(
            new CommandDefinition(sql, new { Cpf = cpf }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Limit>> GetLimitsByPersonAsync(
        string personId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   person_id AS PersonId,
                   operation_type AS OperationType,
                   amount,
                   occurred,
                   origin_service AS OriginService,
                   policy_number AS PolicyNumber
            FROM limits
            WHERE person_id = @PersonId
            ORDER BY occurred
            """;

        await using var connection = connectionFactory();
        var limits = await connection.QueryAsync<Limit>(
            new CommandDefinition(sql, new { PersonId = personId }, cancellationToken: cancellationToken));
        return limits.AsList();
    }

    public async Task<IReadOnlyList<Limit>> GetLatestLimitsByPersonAsync(
        string personId,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        const string sql = """
            SELECT id,
                   person_id AS PersonId,
                   operation_type AS OperationType,
                   amount,
                   occurred,
                   origin_service AS OriginService,
                   policy_number AS PolicyNumber
            FROM limits
            WHERE person_id = @PersonId
            ORDER BY occurred DESC
            LIMIT @Count
            """;

        await using var connection = connectionFactory();
        var limits = await connection.QueryAsync<Limit>(
            new CommandDefinition(
                sql,
                new { PersonId = personId, Count = count },
                cancellationToken: cancellationToken));
        return limits.AsList();
    }

    public async Task<IReadOnlyList<Limit>> GetLimitsByPersonAndDateAsync(
        string personId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   person_id AS PersonId,
                   operation_type AS OperationType,
                   amount,
                   occurred,
                   origin_service AS OriginService,
                   policy_number AS PolicyNumber
            FROM limits
            WHERE person_id = @PersonId
              AND occurred >= @From
              AND occurred <= @To
            ORDER BY occurred
            """;

        await using var connection = connectionFactory();
        var limits = await connection.QueryAsync<Limit>(
            new CommandDefinition(
                sql,
                new { PersonId = personId, From = from, To = to },
                cancellationToken: cancellationToken));
        return limits.AsList();
    }

    public async Task<Limit?> GetLimitAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   person_id AS PersonId,
                   operation_type AS OperationType,
                   amount,
                   occurred,
                   origin_service AS OriginService,
                   policy_number AS PolicyNumber
            FROM limits
            WHERE id = @Id
            """;

        await using var connection = connectionFactory();
        return await connection.QuerySingleOrDefaultAsync<Limit>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task CreatePersonAsync(
        Person person,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO persons (id, name, cpf, associate_code)
            VALUES (@Id, @Name, @CPF, @AssociateCode)
            """;

        await using var connection = connectionFactory();
        await connection.ExecuteAsync(
            new CommandDefinition(sql, person, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdatePersonAsync(
        Person person,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE persons
            SET name = @Name,
                cpf = @CPF,
                associate_code = @AssociateCode
            WHERE id = @Id
            """;

        await using var connection = connectionFactory();
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, person, cancellationToken: cancellationToken));
        return affectedRows > 0;
    }

    public async Task CreateLimitAsync(
        Limit limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO limits
                (id, person_id, operation_type, amount, occurred, origin_service, policy_number)
            VALUES
                (@Id, @PersonId, @OperationType, @Amount, @Occurred, @OriginService, @PolicyNumber)
            """;

        await using var connection = connectionFactory();
        await connection.ExecuteAsync(
            new CommandDefinition(sql, limit, cancellationToken: cancellationToken));
    }
}
