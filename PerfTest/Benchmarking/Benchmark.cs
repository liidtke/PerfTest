namespace PerfTest.Benchmarking;

public enum TestType
{
    Mixed,
    Stress
}

public enum Database
{
    MariaDb,
    Postgres,
    MongoDb,
    Cassandra,
    ScyllaDb
}

public interface IBenchmarkTest : IAsyncDisposable
{
    Task ExecutePassAsync(
        Database database,
        CancellationToken cancellationToken = default);
}

public sealed class Benchmark
{
    public const int FactorThreads = 10;

    private readonly IBenchmarkTest _test;

    public Benchmark(TestType testType, Database db, int strength, TimeSpan duration)
    {
        if (!Enum.IsDefined(testType))
            throw new ArgumentOutOfRangeException(nameof(testType), testType, "Unknown test type.");
        if (!Enum.IsDefined(db))
            throw new ArgumentOutOfRangeException(nameof(db), db, "Unknown database.");
        if (strength is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(
                nameof(strength),
                strength,
                "Strength must be between 1 and 10.");
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Duration must be greater than zero.");

        TestType = testType;
        Database = db;
        Strength = strength;
        Duration = duration;
        MaxThreads = strength * FactorThreads;
        _test = testType switch
        {
            TestType.Mixed => new MixedTest(),
            TestType.Stress => new StressTest(),
            _ => throw new ArgumentOutOfRangeException(nameof(testType), testType, "Unknown test type.")
        };
    }

    public TestType TestType { get; }

    public Database Database { get; }

    public int Strength { get; }

    public TimeSpan Duration { get; }

    public int MaxThreads { get; }

    public async Task<BenchmarkResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunCoreAsync(cancellationToken);
        }
        finally
        {
            await _test.DisposeAsync();
        }
    }

    private async Task<BenchmarkResult> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        var wallClock = System.Diagnostics.Stopwatch.StartNew();
        var statistics = new PassStatistics();
        long successfulPasses = 0;
        var concurrency = 1;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var passes = new Task<PassResult>[concurrency];
            for (var index = 0; index < concurrency; index++)
                passes[index] = RunPassAsync(cancellationToken);

            var results = await Task.WhenAll(passes);
            foreach (var result in results)
            {
                statistics.Add(result.DurationTicks);
                if (result.Succeeded)
                    successfulPasses++;
            }

            if (concurrency < MaxThreads)
                concurrency++;
        } while (wallClock.Elapsed < Duration);

        wallClock.Stop();

        return new BenchmarkResult(
            TestType,
            Database,
            Strength,
            MaxThreads,
            Duration,
            wallClock.Elapsed,
            TimeSpan.FromTicks(statistics.TotalTicks),
            statistics.Count,
            successfulPasses,
            statistics.Count - successfulPasses,
            TimeSpan.FromTicks(statistics.TotalTicks / statistics.Count),
            TimeSpan.FromTicks(statistics.GetPercentile(0.95)),
            successfulPasses == statistics.Count);
    }

    private async Task<PassResult> RunPassAsync(CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _test.ExecutePassAsync(Database, cancellationToken);
            return new PassResult(true, stopwatch.Elapsed.Ticks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new PassResult(false, stopwatch.Elapsed.Ticks);
        }
    }

    private readonly record struct PassResult(bool Succeeded, long DurationTicks);

    private sealed class PassStatistics
    {
        private readonly SortedDictionary<long, long> _microsecondBuckets = [];

        public long Count { get; private set; }
        public long TotalTicks { get; private set; }

        public void Add(long durationTicks)
        {
            Count++;
            TotalTicks += durationTicks;
            var bucket = durationTicks / 10 * 10;
            _microsecondBuckets[bucket] =
                _microsecondBuckets.GetValueOrDefault(bucket) + 1;
        }

        public long GetPercentile(double percentile)
        {
            var target = (long)Math.Ceiling(Count * percentile);
            long observed = 0;
            foreach (var (durationTicks, occurrences) in _microsecondBuckets)
            {
                observed += occurrences;
                if (observed >= target)
                    return durationTicks;
            }

            throw new InvalidOperationException("No pass duration was recorded.");
        }
    }
}

public sealed record BenchmarkResult(
    TestType TestType,
    Database Database,
    int Strength,
    int MaxThreads,
    TimeSpan RequestedDuration,
    TimeSpan TimeToCompleteAllPasses,
    TimeSpan TotalPassDuration,
    long PassCount,
    long SuccessfulPassCount,
    long FailedPassCount,
    TimeSpan AveragePassDuration,
    TimeSpan P95PassDuration,
    bool AllPassesSucceeded)
{
    public double AverageContainerMemoryMb { get; init; }
}
