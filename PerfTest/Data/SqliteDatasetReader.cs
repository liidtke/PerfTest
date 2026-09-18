using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace PerfTest.Data;

public sealed class SqliteDatasetReader(string datasetPath)
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(datasetPath),
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

    public async IAsyncEnumerable<IReadOnlyList<Person>> ReadPersonsAsync(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, cpf, associate_code FROM persons ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var batch = new List<Person>(batchSize);
        while (await reader.ReadAsync(cancellationToken))
        {
            batch.Add(new Person
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                CPF = reader.GetString(2),
                AssociateCode = reader.GetString(3)
            });

            if (batch.Count == batchSize)
            {
                yield return batch;
                batch = new List<Person>(batchSize);
            }
        }

        if (batch.Count > 0)
            yield return batch;
    }

    public async IAsyncEnumerable<IReadOnlyList<Limit>> ReadLimitsAsync(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, person_id, operation_type, amount, occurred, origin_service, policy_number
            FROM limits
            ORDER BY id
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var batch = new List<Limit>(batchSize);
        while (await reader.ReadAsync(cancellationToken))
        {
            batch.Add(new Limit
            {
                Id = reader.GetString(0),
                PersonId = reader.GetString(1),
                OperationType = reader.GetInt32(2),
                Amount = reader.GetInt32(3),
                Occurred = DateTime.Parse(
                    reader.GetString(4),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                OriginService = reader.GetInt32(5),
                PolicyNumber = reader.GetString(6)
            });

            if (batch.Count == batchSize)
            {
                yield return batch;
                batch = new List<Limit>(batchSize);
            }
        }

        if (batch.Count > 0)
            yield return batch;
    }

    public async Task<IReadOnlyList<string>> ReadFixedCpfsAsync(
        CancellationToken cancellationToken = default)
    {
        var cpfs = new List<string>(SqliteDatasetGenerator.FixedCpfCount);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT cpf FROM benchmark_cpfs ORDER BY ordinal";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            cpfs.Add(reader.GetString(0));
        return cpfs;
    }
}
