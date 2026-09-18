using Bogus;
using PerfTest.Data;

namespace PerfTest.Benchmarking;

public abstract class BenchmarkTestBase : IBenchmarkTest
{
    private const long GeneratedCpfRange = 700_000_000;
    private const long GeneratedCpfMultiplier = 104_729;
    private static long _generatedCpfSequence = Random.Shared.NextInt64(GeneratedCpfRange);

    private readonly Lazy<Task<IReadOnlyList<string>>> _fixedCpfs;
    private BenchmarkRepositoryContext? _context;
    private readonly object _contextLock = new();

    protected BenchmarkTestBase()
    {
        var datasetPath =
            Environment.GetEnvironmentVariable("DatasetPath") ??
            "data/perf-test-dataset.sqlite";
        _fixedCpfs = new Lazy<Task<IReadOnlyList<string>>>(
            () => new SqliteDatasetReader(datasetPath).ReadFixedCpfsAsync(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public abstract Task ExecutePassAsync(
        Database database,
        CancellationToken cancellationToken = default);

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
    }

    protected IRepository GetRepository(Database database)
    {
        if (_context is not null)
            return _context.Repository;

        lock (_contextLock)
        {
            _context ??= BenchmarkRepositoryContext.Create(database);
            return _context.Repository;
        }
    }

    protected async Task<IReadOnlyList<string>> GetRandomExistingCpfsAsync(
        int count,
        CancellationToken cancellationToken)
    {
        var cpfs = await _fixedCpfs.Value.WaitAsync(cancellationToken);
        if (count > cpfs.Count)
            throw new InvalidOperationException(
                $"Requested {count} CPFs, but the fixed dataset contains only {cpfs.Count}.");

        var selectedIndexes = new HashSet<int>();
        while (selectedIndexes.Count < count)
            selectedIndexes.Add(Random.Shared.Next(cpfs.Count));
        return selectedIndexes.Select(index => cpfs[index]).ToArray();
    }

    protected static Person CreateRandomPerson()
    {
        var faker = new Faker("pt_BR");
        return new Person
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = faker.Name.FullName(),
            CPF = CreateUniqueCpf(),
            AssociateCode = faker.Random.AlphaNumeric(16)
        };
    }

    protected static Limit CreateRandomLimit(string personId)
    {
        var faker = new Faker("pt_BR");
        return new Limit
        {
            Id = Guid.NewGuid().ToString("N"),
            PersonId = personId,
            OperationType = faker.Random.Int(0, 19),
            Amount = faker.Random.Int(1, 1_000_000),
            Occurred = faker.Date.RecentOffset(730).UtcDateTime,
            OriginService = faker.Random.Int(0, 120),
            PolicyNumber = faker.Random.AlphaNumeric(20)
        };
    }

    private static string CreateUniqueCpf()
    {
        var sequence = Interlocked.Increment(ref _generatedCpfSequence);
        var baseNumber = 200_000_000 +
                         sequence * GeneratedCpfMultiplier % GeneratedCpfRange;
        Span<int> digits = stackalloc int[11];
        for (var index = 8; index >= 0; index--)
        {
            digits[index] = (int)(baseNumber % 10);
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
}
