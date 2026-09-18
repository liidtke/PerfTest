using System.Collections.Concurrent;
using global::Cassandra;

namespace PerfTest.Repositories.Cassandra;

public abstract class CassandraRepositoryBase(ISession session) : IRepository
{
    private readonly ConcurrentDictionary<string, PreparedStatement> _prepared = new();

    public async Task<Person?> GetPersonAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteAsync(
            await BindAsync(
                "SELECT id, name, cpf, associate_code FROM persons WHERE id = ?",
                cancellationToken,
                id),
            cancellationToken);
        var row = rows.FirstOrDefault();
        return row is null ? null : MapPerson(row);
    }

    public async Task<Person?> GetPersonByCpfAsync(
        string cpf,
        CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteAsync(
            await BindAsync(
                "SELECT id, name, cpf, associate_code FROM persons_by_cpf WHERE cpf = ?",
                cancellationToken,
                cpf),
            cancellationToken);
        var row = rows.FirstOrDefault();
        return row is null ? null : MapPerson(row);
    }

    public async Task<IReadOnlyList<Limit>> GetLimitsByPersonAsync(
        string personId,
        CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteAsync(
            await BindAsync(
                """
                SELECT id, person_id, operation_type, amount, occurred, origin_service, policy_number
                FROM limits_by_person
                WHERE person_id = ?
                """,
                cancellationToken,
                personId),
            cancellationToken);
        return rows.Select(MapLimit).ToList();
    }

    public async Task<IReadOnlyList<Limit>> GetLatestLimitsByPersonAsync(
        string personId,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var rows = await ExecuteAsync(
            await BindAsync(
                """
                SELECT id, person_id, operation_type, amount, occurred, origin_service, policy_number
                FROM limits_by_person
                WHERE person_id = ?
                ORDER BY occurred DESC
                LIMIT ?
                """,
                cancellationToken,
                personId,
                count),
            cancellationToken);
        return rows.Select(MapLimit).ToList();
    }

    public async Task<IReadOnlyList<Limit>> GetLimitsByPersonAndDateAsync(
        string personId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteAsync(
            await BindAsync(
                """
                SELECT id, person_id, operation_type, amount, occurred, origin_service, policy_number
                FROM limits_by_person
                WHERE person_id = ? AND occurred >= ? AND occurred <= ?
                """,
                cancellationToken,
                personId,
                ToDateTimeOffset(from),
                ToDateTimeOffset(to)),
            cancellationToken);
        return rows.Select(MapLimit).ToList();
    }

    public async Task<Limit?> GetLimitAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteAsync(
            await BindAsync(
                """
                SELECT id, person_id, operation_type, amount, occurred, origin_service, policy_number
                FROM limits_by_id
                WHERE id = ?
                """,
                cancellationToken,
                id),
            cancellationToken);
        var row = rows.FirstOrDefault();
        return row is null ? null : MapLimit(row);
    }

    public async Task CreatePersonAsync(
        Person person,
        CancellationToken cancellationToken = default)
    {
        var batch = new BatchStatement()
            .Add(new SimpleStatement(
                """
                INSERT INTO persons (id, name, cpf, associate_code)
                VALUES (?, ?, ?, ?)
                """,
                person.Id,
                person.Name,
                person.CPF,
                person.AssociateCode))
            .Add(new SimpleStatement(
                """
                INSERT INTO persons_by_cpf (cpf, id, name, associate_code)
                VALUES (?, ?, ?, ?)
                """,
                person.CPF,
                person.Id,
                person.Name,
                person.AssociateCode));
        await ExecuteAsync(batch, cancellationToken);
    }

    public async Task<bool> UpdatePersonAsync(
        Person person,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetPersonAsync(person.Id, cancellationToken);
        if (existing is null)
            return false;

        var rows = await ExecuteAsync(
            new SimpleStatement(
                """
                UPDATE persons
                SET name = ?, cpf = ?, associate_code = ?
                WHERE id = ?
                IF EXISTS
                """,
                person.Name,
                person.CPF,
                person.AssociateCode,
                person.Id),
            cancellationToken);
        if (!rows.First().GetValue<bool>("[applied]"))
            return false;

        var batch = new BatchStatement();
        if (!string.Equals(existing.CPF, person.CPF, StringComparison.Ordinal))
        {
            batch.Add(new SimpleStatement(
                "DELETE FROM persons_by_cpf WHERE cpf = ?",
                existing.CPF));
        }
        batch.Add(new SimpleStatement(
            """
            INSERT INTO persons_by_cpf (cpf, id, name, associate_code)
            VALUES (?, ?, ?, ?)
            """,
            person.CPF,
            person.Id,
            person.Name,
            person.AssociateCode));
        await ExecuteAsync(batch, cancellationToken);
        return true;
    }

    public async Task CreateLimitAsync(
        Limit limit,
        CancellationToken cancellationToken = default)
    {
        var values = new object[]
        {
            limit.Id,
            limit.PersonId,
            limit.OperationType,
            limit.Amount,
            ToDateTimeOffset(limit.Occurred),
            limit.OriginService,
            limit.PolicyNumber
        };

        var batch = new BatchStatement()
            .Add(new SimpleStatement(
                """
                INSERT INTO limits_by_person
                    (id, person_id, operation_type, amount, occurred, origin_service, policy_number)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                values))
            .Add(new SimpleStatement(
                """
                INSERT INTO limits_by_id
                    (id, person_id, operation_type, amount, occurred, origin_service, policy_number)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                values));

        await ExecuteAsync(batch, cancellationToken);
    }

    private async Task<BoundStatement> BindAsync(
        string cql,
        CancellationToken cancellationToken,
        params object[] values)
    {
        if (!_prepared.TryGetValue(cql, out var prepared))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = await session.PrepareAsync(cql).WaitAsync(cancellationToken);
            prepared = _prepared.GetOrAdd(cql, candidate);
        }

        return prepared.Bind(values);
    }

    private async Task<RowSet> ExecuteAsync(
        IStatement statement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await session.ExecuteAsync(statement).WaitAsync(cancellationToken);
    }

    private static Person MapPerson(Row row) => new()
    {
        Id = row.GetValue<string>("id"),
        Name = row.GetValue<string>("name"),
        CPF = row.GetValue<string>("cpf"),
        AssociateCode = row.GetValue<string>("associate_code")
    };

    private static Limit MapLimit(Row row) => new()
    {
        Id = row.GetValue<string>("id"),
        PersonId = row.GetValue<string>("person_id"),
        OperationType = row.GetValue<int>("operation_type"),
        Amount = row.GetValue<int>("amount"),
        Occurred = row.GetValue<DateTimeOffset>("occurred").UtcDateTime,
        OriginService = row.GetValue<int>("origin_service"),
        PolicyNumber = row.GetValue<string>("policy_number")
    };

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime());
}
