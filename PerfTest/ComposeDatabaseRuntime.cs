using System.Diagnostics;
using PerfTest.Benchmarking;

namespace PerfTest;

internal static class ComposeDatabaseRuntime
{
    public static readonly string[] AllServiceNames =
        ["mariadb", "postgres", "mongodb", "cassandra", "scylladb"];

    public static string ServiceName(Database database) =>
        database switch
        {
            Database.MariaDb => "mariadb",
            Database.Postgres => "postgres",
            Database.MongoDb => "mongodb",
            Database.Cassandra => "cassandra",
            Database.ScyllaDb => "scylladb",
            _ => throw new ArgumentOutOfRangeException(nameof(database), database, "Unknown database.")
        };

    public static async Task<Session> StartAsync(
        string service,
        CancellationToken cancellationToken = default)
    {
        if (!AllServiceNames.Contains(service, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unknown compose service '{service}'. Expected: {string.Join(", ", AllServiceNames)}",
                nameof(service));

        service = service.ToLowerInvariant();
        var composeFile = FindComposeFile();
        Console.WriteLine($"Starting {service}...");
        try
        {
            await RunPodmanComposeAsync(
                composeFile,
                service,
                ["up", "-d", "--wait", "--wait-timeout", "600", service],
                cancellationToken);
        }
        catch
        {
            try
            {
                await RunPodmanComposeAsync(
                    composeFile,
                    service,
                    ["stop", service],
                    CancellationToken.None);
            }
            catch
            {
                // The original start failure is more useful than a follow-up stop error.
            }

            throw;
        }

        return new Session(composeFile, service);
    }

    private static string FindComposeFile()
    {
        var configured =
            Environment.GetEnvironmentVariable("COMPOSE_FILE") ??
            Environment.GetEnvironmentVariable("PERFTEST_COMPOSE_FILE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var configuredPath = Path.GetFullPath(configured);
            if (!File.Exists(configuredPath))
                throw new FileNotFoundException(
                    $"Compose file '{configuredPath}' does not exist.",
                    configuredPath);
            return configuredPath;
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start);
                 directory is not null;
                 directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "compose.yaml");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new FileNotFoundException(
            "compose.yaml was not found. Run from the repository root or set COMPOSE_FILE.");
    }

    private static async Task RunPodmanComposeAsync(
        string composeFile,
        string service,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var projectDirectory = Path.GetDirectoryName(composeFile)
            ?? throw new InvalidOperationException($"Compose file '{composeFile}' has no directory.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "podman",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("compose");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(composeFile);
        startInfo.ArgumentList.Add("--project-directory");
        startInfo.ArgumentList.Add(projectDirectory);
        startInfo.ArgumentList.Add("--profile");
        startInfo.ArgumentList.Add(service);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start podman.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may have already exited.
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"podman compose {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.");
        }
    }

    internal sealed class Session(string composeFile, string service) : IAsyncDisposable
    {
        public string ContainerName { get; } = $"perf-test-{service}-1";

        public async ValueTask DisposeAsync()
        {
            Console.WriteLine($"Stopping {service}...");
            await RunPodmanComposeAsync(
                composeFile,
                service,
                ["stop", service],
                CancellationToken.None);
        }
    }
}
