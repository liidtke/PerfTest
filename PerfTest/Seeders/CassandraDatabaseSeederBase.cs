using global::Cassandra;
using PerfTest.Data;

namespace PerfTest.Seeders;

public abstract class CassandraDatabaseSeederBase(
    string databaseName,
    ISession session) : IDatabaseSeeder
{
    private const int MaxConcurrentWrites = 64;

    public string DatabaseName { get; } = databaseName;

    public async Task SeedAsync(
        SqliteDatasetReader dataset,
        IProgress<SeedProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            """
            CREATE KEYSPACE IF NOT EXISTS perf_test
            WITH replication = {'class': 'SimpleStrategy', 'replication_factor': 1}
            """,
            cancellationToken);
        session.ChangeKeyspace("perf_test");
        await ResetSchemaAsync(cancellationToken);

        var insertPerson = await session.PrepareAsync(
            """
            INSERT INTO persons (id, name, cpf, associate_code)
            VALUES (?, ?, ?, ?)
            """).WaitAsync(cancellationToken);
        var insertPersonByCpf = await session.PrepareAsync(
            """
            INSERT INTO persons_by_cpf (cpf, id, name, associate_code)
            VALUES (?, ?, ?, ?)
            """).WaitAsync(cancellationToken);
        var insertLimitByPerson = await session.PrepareAsync(
            """
            INSERT INTO limits_by_person
                (id, person_id, operation_type, amount, occurred, origin_service, policy_number)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """).WaitAsync(cancellationToken);
        var insertLimitById = await session.PrepareAsync(
            """
            INSERT INTO limits_by_id
                (id, person_id, operation_type, amount, occurred, origin_service, policy_number)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """).WaitAsync(cancellationToken);

        long inserted = 0;
        await foreach (var batch in dataset.ReadPersonsAsync(500, cancellationToken))
        {
            await Parallel.ForEachAsync(
                batch,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxConcurrentWrites,
                    CancellationToken = cancellationToken
                },
                async (person, token) =>
                {
                    await ExecuteAsync(
                        insertPerson.Bind(
                            person.Id,
                            person.Name,
                            person.CPF,
                            person.AssociateCode),
                        token);
                    await ExecuteAsync(
                        insertPersonByCpf.Bind(
                            person.CPF,
                            person.Id,
                            person.Name,
                            person.AssociateCode),
                        token);
                });
            inserted += batch.Count;
            progress?.Report(new SeedProgress(
                DatabaseName,
                "persons",
                inserted,
                SqliteDatasetGenerator.PersonCount));
        }

        inserted = 0;
        await foreach (var batch in dataset.ReadLimitsAsync(500, cancellationToken))
        {
            await Parallel.ForEachAsync(
                batch,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxConcurrentWrites,
                    CancellationToken = cancellationToken
                },
                (limit, token) => new ValueTask(InsertLimitAsync(
                    insertLimitByPerson,
                    insertLimitById,
                    limit,
                    token)));
            inserted += batch.Count;
            progress?.Report(new SeedProgress(
                DatabaseName,
                "limits",
                inserted,
                SqliteDatasetGenerator.LimitCount));
        }
    }

    private async Task ResetSchemaAsync(CancellationToken cancellationToken)
    {
        await ExecuteAsync("DROP TABLE IF EXISTS limits_by_person", cancellationToken);
        await ExecuteAsync("DROP TABLE IF EXISTS limits_by_id", cancellationToken);
        await ExecuteAsync("DROP TABLE IF EXISTS persons_by_cpf", cancellationToken);
        await ExecuteAsync("DROP TABLE IF EXISTS persons", cancellationToken);
        await ExecuteAsync(
            """
            CREATE TABLE persons (
                id text PRIMARY KEY,
                name text,
                cpf text,
                associate_code text
            )
            """,
            cancellationToken);
        await ExecuteAsync(
            """
            CREATE TABLE persons_by_cpf (
                cpf text PRIMARY KEY,
                id text,
                name text,
                associate_code text
            )
            """,
            cancellationToken);
        await ExecuteAsync(
            """
            CREATE TABLE limits_by_person (
                person_id text,
                occurred timestamp,
                id text,
                operation_type int,
                amount int,
                origin_service int,
                policy_number text,
                PRIMARY KEY ((person_id), occurred, id)
            ) WITH CLUSTERING ORDER BY (occurred ASC, id ASC)
            """,
            cancellationToken);
        await ExecuteAsync(
            """
            CREATE TABLE limits_by_id (
                id text PRIMARY KEY,
                person_id text,
                operation_type int,
                amount int,
                occurred timestamp,
                origin_service int,
                policy_number text
            )
            """,
            cancellationToken);
    }

    private async Task InsertLimitAsync(
        PreparedStatement insertByPerson,
        PreparedStatement insertById,
        Limit limit,
        CancellationToken cancellationToken)
    {
        var values = new object[]
        {
            limit.Id,
            limit.PersonId,
            limit.OperationType,
            limit.Amount,
            new DateTimeOffset(limit.Occurred.ToUniversalTime()),
            limit.OriginService,
            limit.PolicyNumber
        };

        await ExecuteAsync(insertByPerson.Bind(values), cancellationToken);
        await ExecuteAsync(insertById.Bind(values), cancellationToken);
    }

    private Task<RowSet> ExecuteAsync(
        string cql,
        CancellationToken cancellationToken) =>
        ExecuteAsync(new SimpleStatement(cql), cancellationToken);

    private async Task<RowSet> ExecuteAsync(
        IStatement statement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await session.ExecuteAsync(statement).WaitAsync(cancellationToken);
    }
}
