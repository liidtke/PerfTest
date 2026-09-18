using Bogus;
using Microsoft.Data.Sqlite;

namespace PerfTest.Data;

public sealed class SqliteDatasetGenerator
{
    public const int FixedCpfCount = 1_000;
    public const int PersonCount = 1_000_000;
    public const int LimitCount = 5_000_000;
    private const int Seed = 20_260_917;

    public Task GenerateAsync(
        string outputPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Generate(outputPath, progress, cancellationToken),
            cancellationToken);

    private static void Generate(
        string outputPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{fullPath}.tmp";
        File.Delete(temporaryPath);

        try
        {
            using var connection = new SqliteConnection($"Data Source={temporaryPath}");
            connection.Open();
            Execute(connection, """
                PRAGMA journal_mode = OFF;
                PRAGMA synchronous = OFF;
                PRAGMA temp_store = MEMORY;
                PRAGMA locking_mode = EXCLUSIVE;

                CREATE TABLE persons (
                    id TEXT NOT NULL PRIMARY KEY,
                    name TEXT NOT NULL,
                    cpf TEXT NOT NULL UNIQUE,
                    associate_code TEXT NOT NULL
                ) WITHOUT ROWID;

                CREATE TABLE limits (
                    id TEXT NOT NULL PRIMARY KEY,
                    person_id TEXT NOT NULL,
                    operation_type INTEGER NOT NULL,
                    amount INTEGER NOT NULL,
                    occurred TEXT NOT NULL,
                    origin_service INTEGER NOT NULL,
                    policy_number TEXT NOT NULL
                ) WITHOUT ROWID;

                CREATE TABLE benchmark_cpfs (
                    ordinal INTEGER NOT NULL PRIMARY KEY,
                    person_id TEXT NOT NULL,
                    cpf TEXT NOT NULL UNIQUE
                );

                CREATE TABLE dataset_metadata (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                ) WITHOUT ROWID;
                """);

            InsertPersons(connection, progress, cancellationToken);
            InsertLimits(connection, progress, cancellationToken);

            progress?.Report("Creating SQLite indexes...");
            Execute(connection, """
                CREATE INDEX ix_limits_person_occurred
                    ON limits (person_id, occurred);
                CREATE INDEX ix_limits_origin_service
                    ON limits (origin_service);

                INSERT INTO dataset_metadata (key, value) VALUES
                    ('seed', '20260917'),
                    ('fixed_cpf_count', '1000'),
                    ('person_count', '1000000'),
                    ('limit_count', '5000000');
                PRAGMA optimize;
                """);
            connection.Close();

            File.Move(temporaryPath, fullPath, true);
            progress?.Report($"Dataset written to {fullPath}");
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private static void InsertPersons(
        SqliteConnection connection,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Randomizer.Seed = new Random(Seed);
        var faker = new Faker("pt_BR");
        using var transaction = connection.BeginTransaction();
        using var personCommand = connection.CreateCommand();
        personCommand.Transaction = transaction;
        personCommand.CommandText = """
            INSERT INTO persons (id, name, cpf, associate_code)
            VALUES ($id, $name, $cpf, $associateCode)
            """;
        var id = personCommand.Parameters.Add("$id", SqliteType.Text);
        var name = personCommand.Parameters.Add("$name", SqliteType.Text);
        var cpf = personCommand.Parameters.Add("$cpf", SqliteType.Text);
        var associateCode = personCommand.Parameters.Add("$associateCode", SqliteType.Text);
        personCommand.Prepare();

        using var fixedCpfCommand = connection.CreateCommand();
        fixedCpfCommand.Transaction = transaction;
        fixedCpfCommand.CommandText = """
            INSERT INTO benchmark_cpfs (ordinal, person_id, cpf)
            VALUES ($ordinal, $personId, $cpf)
            """;
        var ordinalParameter = fixedCpfCommand.Parameters.Add("$ordinal", SqliteType.Integer);
        var fixedPersonId = fixedCpfCommand.Parameters.Add("$personId", SqliteType.Text);
        var fixedCpf = fixedCpfCommand.Parameters.Add("$cpf", SqliteType.Text);
        fixedCpfCommand.Prepare();

        for (var ordinal = 0; ordinal < PersonCount; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var personId = $"person-{ordinal + 1:D7}";
            var personCpf = CreateCpf(100_000_000 + ordinal);

            id.Value = personId;
            name.Value = faker.Name.FullName();
            cpf.Value = personCpf;
            associateCode.Value = $"associate-{(ordinal * 104_729L + Seed) % 100_000_000:D8}";
            personCommand.ExecuteNonQuery();

            if (ordinal < FixedCpfCount)
            {
                ordinalParameter.Value = ordinal + 1;
                fixedPersonId.Value = personId;
                fixedCpf.Value = personCpf;
                fixedCpfCommand.ExecuteNonQuery();
            }

            if ((ordinal + 1) % 100_000 == 0)
                progress?.Report($"Generated {ordinal + 1:N0}/{PersonCount:N0} persons");
        }

        transaction.Commit();
    }

    private static void InsertLimits(
        SqliteConnection connection,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO limits
                (id, person_id, operation_type, amount, occurred, origin_service, policy_number)
            VALUES
                ($id, $personId, $operationType, $amount, $occurred, $originService, $policyNumber)
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var personId = command.Parameters.Add("$personId", SqliteType.Text);
        var operationType = command.Parameters.Add("$operationType", SqliteType.Integer);
        var amount = command.Parameters.Add("$amount", SqliteType.Integer);
        var occurred = command.Parameters.Add("$occurred", SqliteType.Text);
        var originService = command.Parameters.Add("$originService", SqliteType.Integer);
        var policyNumber = command.Parameters.Add("$policyNumber", SqliteType.Text);
        command.Prepare();

        for (var ordinal = 0; ordinal < LimitCount; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var personOrdinal = ordinal / 5 + 1;

            id.Value = $"limit-{ordinal + 1:D8}";
            personId.Value = $"person-{personOrdinal:D7}";
            operationType.Value = (ordinal * 7 + 3) % 20;
            amount.Value = (ordinal * 97L + 1_003) % 1_000_000 + 1;
            occurred.Value = epoch.AddMinutes((ordinal * 43L) % 1_051_200)
                .ToString("O");
            originService.Value = (ordinal * 31 + 11) % 121;
            policyNumber.Value = $"policy-{(ordinal * 65_537L + Seed) % 100_000_000_000:D11}";
            command.ExecuteNonQuery();

            if ((ordinal + 1) % 500_000 == 0)
                progress?.Report($"Generated {ordinal + 1:N0}/{LimitCount:N0} limits");
        }

        transaction.Commit();
    }

    private static string CreateCpf(int baseNumber)
    {
        Span<int> digits = stackalloc int[11];
        for (var index = 8; index >= 0; index--)
        {
            digits[index] = baseNumber % 10;
            baseNumber /= 10;
        }

        digits[9] = CalculateCpfDigit(digits[..9], 10);
        digits[10] = CalculateCpfDigit(digits[..10], 11);
        return string.Create(11, digits.ToArray(), static (chars, values) =>
        {
            for (var index = 0; index < values.Length; index++)
                chars[index] = (char)('0' + values[index]);
        });
    }

    private static int CalculateCpfDigit(ReadOnlySpan<int> digits, int weight)
    {
        var sum = 0;
        foreach (var digit in digits)
            sum += digit * weight--;
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
