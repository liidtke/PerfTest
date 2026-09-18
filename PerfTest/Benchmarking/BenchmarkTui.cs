using Spectre.Console;

namespace PerfTest.Benchmarking;

public static class BenchmarkTui
{
    public static async Task RunAsync(
        Func<string, CancellationToken, Task> seedAsync,
        CancellationToken cancellationToken = default)
    {
        AnsiConsole.Write(new FigletText("PerfTest").Color(Color.Cyan1));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = Select(
                "Select an [green]action[/]:",
                "Select an action:",
                Enum.GetValues<TuiAction>(),
                value => value switch
                {
                    TuiAction.RunBenchmark => "Run benchmark",
                    TuiAction.ViewResults => "View results",
                    TuiAction.SeedAllDatabases => "Seed databases",
                    TuiAction.Exit => "Exit",
                    _ => value.ToString()
                });

            switch (action)
            {
                case TuiAction.RunBenchmark:
                    await RunBenchmarkAsync(cancellationToken);
                    break;
                case TuiAction.ViewResults:
                    await ShowResultsMenuAsync(cancellationToken);
                    break;
                case TuiAction.SeedAllDatabases:
                {
                    var target = Select(
                        "Select a [green]database to seed[/]:",
                        "Select a database to seed:",
                        Enum.GetValues<SeedTarget>(),
                        value => value switch
                        {
                            SeedTarget.All => "All databases",
                            SeedTarget.MariaDb => "MariaDB",
                            SeedTarget.Postgres => "PostgreSQL",
                            SeedTarget.MongoDb => "MongoDB",
                            SeedTarget.Cassandra => "Cassandra",
                            SeedTarget.ScyllaDb => "ScyllaDB",
                            _ => value.ToString()
                        });
                    var targetName = target.ToString().ToLowerInvariant();
                    var targetDescription = target == SeedTarget.All
                        ? "all five databases"
                        : target.ToString();
                    var confirmed = AnsiConsole.Confirm(
                        $"[yellow]This will reset and seed {targetDescription} with the SQLite dataset. Continue?[/]",
                        false);
                    if (!confirmed)
                    {
                        AnsiConsole.MarkupLine("[grey]Seeding cancelled. No database was changed.[/]");
                        break;
                    }

                    AnsiConsole.MarkupLine($"[cyan]Seeding {targetDescription}...[/]");
                    await seedAsync(targetName, cancellationToken);
                    AnsiConsole.MarkupLine($"[green]Successfully seeded {targetDescription}.[/]");
                    break;
                }
                case TuiAction.Exit:
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public static async Task RunBenchmarkAsync(
        CancellationToken cancellationToken = default)
    {
        AnsiConsole.Write(new Rule("[cyan]Benchmark[/]"));

        var testType = Select(
            "Select a [green]test type[/]:",
            "Select a test type:",
            Enum.GetValues<TestType>());

        var databaseTarget = Select(
            "Select a [green]database[/]:",
            "Select a database:",
            new[] { new DatabaseTarget(null, "All databases") }
                .Concat(Enum.GetValues<Database>()
                    .Select(database => new DatabaseTarget(database, database.ToString()))),
            target => target.Name);

        var strength = Select(
            "Select [green]strength[/] (1-10):",
            "Select strength (1-10):",
            Enumerable.Range(1, 10));

        var durationMinutes = AnsiConsole.Prompt(
            new TextPrompt<int>("Enter benchmark [green]duration in minutes[/]:")
                .DefaultValue(1)
                .ValidationErrorMessage("[red]Duration must be between 1 and 1,440 minutes.[/]")
                .Validate(minutes => minutes is >= 1 and <= 1_440));

        AnsiConsole.Write(
            new Panel(
                $"Test type: [cyan]{testType}[/]\n" +
                $"Database: [cyan]{databaseTarget.Name}[/]\n" +
                $"Strength: [cyan]{strength}[/]\n" +
                $"Maximum threads: [cyan]{strength * Benchmark.FactorThreads}[/]\n" +
                $"Duration per database: [cyan]{FormatDuration(TimeSpan.FromMinutes(durationMinutes))}[/]")
                .Header("Benchmark configuration")
                .BorderColor(Color.Green));

        var resultStore = new BenchmarkResultStore();
        var databases = databaseTarget.Database is { } selectedDatabase
            ? new[] { selectedDatabase }
            : Enum.GetValues<Database>();
        for (var index = 0; index < databases.Length; index++)
        {
            var database = databases[index];
            if (databases.Length > 1)
            {
                AnsiConsole.Write(
                    new Rule($"[cyan]{index + 1}/{databases.Length}: {database}[/]"));
            }

            var benchmark = new Benchmark(
                testType,
                database,
                strength,
                TimeSpan.FromMinutes(durationMinutes));
            await using var databaseRuntime = await ComposeDatabaseRuntime.StartAsync(
                ComposeDatabaseRuntime.ServiceName(database),
                cancellationToken);

            var memorySampler = new ContainerMemorySampler(databaseRuntime.ContainerName);
            BenchmarkResult result;
            try
            {
                result = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync(
                        $"Running benchmark on {database}...",
                        _ => benchmark.RunAsync(cancellationToken));
            }
            finally
            {
                await memorySampler.DisposeAsync();
            }

            result = result with
            {
                AverageContainerMemoryMb = memorySampler.AverageMemoryMb
            };

            await resultStore.SaveAsync(result, cancellationToken);
            AnsiConsole.MarkupLine(
                $"[grey]Result saved to {Markup.Escape(resultStore.Path)}.[/]");
            WriteResultTable(result);
        }
    }

    private static async Task ShowResultsMenuAsync(CancellationToken cancellationToken)
    {
        var resultStore = new BenchmarkResultStore();
        while (true)
        {
            var action = Select(
                "Select a [green]results view[/]:",
                "Select a results view:",
                Enum.GetValues<ResultAction>(),
                value => value switch
                {
                    ResultAction.ListAllByDatabase => "List all by database",
                    ResultAction.ListAllByType => "List all by type",
                    ResultAction.LatestResult => "Latest result",
                    ResultAction.Back => "Back to main menu",
                    _ => value.ToString()
                });

            switch (action)
            {
                case ResultAction.ListAllByDatabase:
                {
                    var database = Select(
                        "Select a [green]database[/]:",
                        "Select a database:",
                        Enum.GetValues<Database>());
                    var results = await resultStore.GetByDatabaseAsync(
                        database,
                        cancellationToken);
                    WriteResultList($"Results for {database}", results);
                    WaitForReturn();
                    break;
                }
                case ResultAction.ListAllByType:
                {
                    var testType = Select(
                        "Select a [green]test type[/]:",
                        "Select a test type:",
                        Enum.GetValues<TestType>());
                    var results = await resultStore.GetByTestTypeAsync(
                        testType,
                        cancellationToken);
                    WriteResultList($"Results for {testType}", results);
                    WaitForReturn();
                    break;
                }
                case ResultAction.LatestResult:
                    await ShowLatestResultAsync(resultStore, cancellationToken);
                    WaitForReturn();
                    break;
                case ResultAction.Back:
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private static async Task ShowLatestResultAsync(
        BenchmarkResultStore resultStore,
        CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new Rule("[cyan]Latest benchmark result[/]"));
        var storedResult = await resultStore.GetLatestAsync(cancellationToken);
        if (storedResult is null)
        {
            AnsiConsole.MarkupLine("[yellow]No benchmark results have been saved yet.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[grey]Run #{storedResult.Id:N0}, completed " +
                $"{Markup.Escape(storedResult.CompletedAt.ToLocalTime().ToString("g"))}[/]");
            WriteResultTable(storedResult.Result);
        }
    }

    private static void WaitForReturn()
    {
        if (AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.Prompt(
                new TextPrompt<string>("[grey]Press Enter to return to the results menu.[/]")
                    .AllowEmpty());
        }
    }

    private static void WriteResultList(
        string title,
        IReadOnlyList<StoredBenchmarkResult> storedResults)
    {
        AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(title)}[/]"));
        if (storedResults.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No matching benchmark results were found.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Run")
            .AddColumn("Completed")
            .AddColumn("Type")
            .AddColumn("Database")
            .AddColumn("Strength")
            .AddColumn("Passes")
            .AddColumn("Failed")
            .AddColumn("Average")
            .AddColumn("P95")
            .AddColumn("Memory");
        foreach (var storedResult in storedResults)
        {
            var result = storedResult.Result;
            table.AddRow(
                $"#{storedResult.Id:N0}",
                storedResult.CompletedAt.ToLocalTime().ToString("g"),
                result.TestType.ToString(),
                result.Database.ToString(),
                result.Strength.ToString("N0"),
                result.PassCount.ToString("N0"),
                result.FailedPassCount.ToString("N0"),
                FormatLatency(result.AveragePassDuration),
                FormatLatency(result.P95PassDuration),
                $"{result.AverageContainerMemoryMb:N2} MB");
        }

        AnsiConsole.Write(table);
    }

    private static void WriteResultTable(BenchmarkResult result)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[green]Benchmark result[/]")
            .AddColumn("Metric")
            .AddColumn("Value");
        table.AddRow("Test type", result.TestType.ToString());
        table.AddRow("Database", result.Database.ToString());
        table.AddRow("Strength", result.Strength.ToString("N0"));
        table.AddRow("Maximum threads", result.MaxThreads.ToString("N0"));
        table.AddRow("Requested duration", FormatDuration(result.RequestedDuration));
        table.AddRow("Time to complete all passes", FormatDuration(result.TimeToCompleteAllPasses));
        table.AddRow("Total pass duration", FormatDuration(result.TotalPassDuration));
        table.AddRow("Passes", result.PassCount.ToString("N0"));
        table.AddRow("Successful passes", result.SuccessfulPassCount.ToString("N0"));
        table.AddRow("Failed passes", result.FailedPassCount.ToString("N0"));
        table.AddRow("Average pass duration", FormatLatency(result.AveragePassDuration));
        table.AddRow("95th percentile", FormatLatency(result.P95PassDuration));
        table.AddRow(
            "Average container memory",
            $"{result.AverageContainerMemoryMb:N2} MB");
        table.AddRow(
            "All passes successful",
            result.AllPassesSucceeded ? "[green]Yes[/]" : "[red]No[/]");
        AnsiConsole.Write(table);
    }

    private static T Select<T>(
        string title,
        string plainTextTitle,
        IEnumerable<T> choices,
        Func<T, string>? converter = null)
        where T : notnull
    {
        var availableChoices = choices.ToArray();
        converter ??= value => value.ToString() ?? string.Empty;

        if (AnsiConsole.Profile.Capabilities.Ansi)
        {
            return AnsiConsole.Prompt(
                new SelectionPrompt<T>()
                    .Title(title)
                    .UseConverter(converter)
                    .PageSize(Math.Min(10, availableChoices.Length))
                    .AddChoices(availableChoices));
        }

        Console.WriteLine(plainTextTitle);
        for (var index = 0; index < availableChoices.Length; index++)
            Console.WriteLine($"  {index + 1}. {converter(availableChoices[index])}");

        while (true)
        {
            Console.Write($"Enter a number (1-{availableChoices.Length}): ");
            var input = Console.ReadLine();
            if (input is null)
                throw new InvalidOperationException(
                    "Interactive input is unavailable. Run this command in a terminal or attach stdin.");

            if (int.TryParse(input, out var selection) &&
                selection >= 1 &&
                selection <= availableChoices.Length)
            {
                return availableChoices[selection - 1];
            }

            Console.WriteLine("Invalid selection.");
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        $"{duration.TotalSeconds:N3} s";

    private static string FormatLatency(TimeSpan duration) =>
        duration.TotalMilliseconds >= 1
            ? $"{duration.TotalMilliseconds:N3} ms"
            : $"{duration.TotalMicroseconds:N3} µs";

    private enum TuiAction
    {
        RunBenchmark,
        ViewResults,
        SeedAllDatabases,
        Exit
    }

    private enum ResultAction
    {
        ListAllByDatabase,
        ListAllByType,
        LatestResult,
        Back
    }

    private sealed record DatabaseTarget(Database? Database, string Name);

    private enum SeedTarget
    {
        All,
        MariaDb,
        Postgres,
        MongoDb,
        Cassandra,
        ScyllaDb
    }
}
