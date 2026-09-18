using System.Diagnostics;
using System.Globalization;

namespace PerfTest.Benchmarking;

internal sealed class ContainerMemorySampler : IAsyncDisposable
{
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(5);

    private readonly string _containerName;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly Task _samplingTask;
    private double _totalMemoryMb;
    private long _sampleCount;

    public ContainerMemorySampler(string containerName)
    {
        _containerName = containerName;
        _samplingTask = SampleContinuouslyAsync();
    }

    public double AverageMemoryMb =>
        _sampleCount == 0 ? 0 : _totalMemoryMb / _sampleCount;

    public async ValueTask DisposeAsync()
    {
        await _stopSource.CancelAsync();
        try
        {
            await _samplingTask;
        }
        catch (OperationCanceledException)
        {
            // Cancellation ends the sampling loop normally.
        }
        finally
        {
            _stopSource.Dispose();
        }
    }

    private async Task SampleContinuouslyAsync()
    {
        while (true)
        {
            _stopSource.Token.ThrowIfCancellationRequested();
            var memoryMb = await ReadMemoryMbAsync(_stopSource.Token);
            _totalMemoryMb += memoryMb;
            _sampleCount++;
            await Task.Delay(SamplingInterval, _stopSource.Token);
        }
    }

    private async Task<double> ReadMemoryMbAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "podman",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("stats");
        startInfo.ArgumentList.Add("--no-stream");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{.MemUsage}}");
        startInfo.ArgumentList.Add(_containerName);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start podman stats.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
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

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to read memory for container '{_containerName}': {error.Trim()}");
        }

        return ParseMemoryMb(output.Split('/', 2)[0].Trim());
    }

    private static double ParseMemoryMb(string value)
    {
        var unitStart = 0;
        while (unitStart < value.Length &&
               (char.IsDigit(value[unitStart]) || value[unitStart] is '.' or ','))
        {
            unitStart++;
        }

        if (unitStart == 0 ||
            !double.TryParse(
                value[..unitStart],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            throw new FormatException($"Unknown container memory value '{value}'.");
        }

        return value[unitStart..].Trim() switch
        {
            "B" => amount / 1_000_000,
            "kB" or "KB" => amount / 1_000,
            "MB" => amount,
            "GB" => amount * 1_000,
            "KiB" => amount * 1_024 / 1_000_000,
            "MiB" => amount * 1_048_576 / 1_000_000,
            "GiB" => amount * 1_073_741_824 / 1_000_000,
            var unit => throw new FormatException(
                $"Unknown container memory unit '{unit}' in '{value}'.")
        };
    }
}
