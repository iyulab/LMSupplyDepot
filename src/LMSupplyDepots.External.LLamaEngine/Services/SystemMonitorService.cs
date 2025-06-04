using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace LMSupplyDepots.External.LLamaEngine.Services;

public record SystemMetrics
{
    public double CpuUsagePercent { get; init; }
    public double MemoryUsagePercent { get; init; }
    public long AvailableMemoryBytes { get; init; }
    public long TotalMemoryBytes { get; init; }
    public Dictionary<string, GpuMetrics> GpuMetrics { get; init; } = [];
    public Dictionary<string, DiskMetrics> DiskMetrics { get; init; } = [];
}

public record GpuMetrics
{
    public string Name { get; init; } = "";
    public double UtilizationPercent { get; init; }
    public long MemoryUsedBytes { get; init; }
    public long TotalMemoryBytes { get; init; }
    public double TemperatureCelsius { get; init; }
}

public record DiskMetrics
{
    public string Name { get; init; } = "";
    public string MountPoint { get; init; } = "";
    public long AvailableSpaceBytes { get; init; }
    public long TotalSizeBytes { get; init; }
    public double UsagePercent { get; init; }
}

public interface ISystemMonitorService
{
    SystemMetrics GetCurrentMetrics();
    IAsyncEnumerable<SystemMetrics> MonitorResourcesAsync(
        TimeSpan interval,
        CancellationToken cancellationToken = default);
}

public class SystemMonitorService : ISystemMonitorService, IDisposable
{
    private readonly ILogger<SystemMonitorService> _logger;
    private readonly Process _process;
    private readonly object _lock = new();
    private double _lastCpuUsage = 0;
    private DateTime _lastMeasureTime;
    private TimeSpan _lastProcessorTime;

    public SystemMonitorService(ILogger<SystemMonitorService> logger)
    {
        _logger = logger;
        _process = Process.GetCurrentProcess();
        _lastMeasureTime = DateTime.UtcNow;
        _lastProcessorTime = _process.TotalProcessorTime;
    }

    private double GetCpuUsage()
    {
        lock (_lock)
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                var currentProcessorTime = _process.TotalProcessorTime;

                var elapsedTime = (currentTime - _lastMeasureTime).TotalSeconds;
                if (elapsedTime < 0.1) // At least 100ms interval needed
                {
                    return _lastCpuUsage;
                }

                var cpuUsedInSeconds = (currentProcessorTime - _lastProcessorTime).TotalSeconds;
                var cpuUsage = cpuUsedInSeconds / (Environment.ProcessorCount * elapsedTime) * 100.0;

                _lastMeasureTime = currentTime;
                _lastProcessorTime = currentProcessorTime;
                _lastCpuUsage = Math.Min(100, Math.Max(0, cpuUsage));

                return _lastCpuUsage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CPU usage calculation failed");
                return _lastCpuUsage;
            }
        }
    }

    public SystemMetrics GetCurrentMetrics()
    {
        try
        {
            var cpuUsage = GetCpuUsage();
            var memory = GC.GetGCMemoryInfo();
            var memoryUsage = (double)(memory.TotalCommittedBytes - memory.TotalAvailableMemoryBytes) / memory.TotalCommittedBytes * 100;

            return new SystemMetrics
            {
                CpuUsagePercent = cpuUsage,
                MemoryUsagePercent = Math.Min(100, Math.Max(0, memoryUsage)),
                AvailableMemoryBytes = memory.TotalAvailableMemoryBytes,
                TotalMemoryBytes = memory.TotalCommittedBytes,
                GpuMetrics = GetGpuMetrics(),
                DiskMetrics = GetDiskMetrics()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system metrics");
            return new SystemMetrics
            {
                CpuUsagePercent = 0,
                MemoryUsagePercent = 0,
                AvailableMemoryBytes = 0,
                TotalMemoryBytes = 0,
                GpuMetrics = [],
                DiskMetrics = []
            };
        }
    }

    public async IAsyncEnumerable<SystemMetrics> MonitorResourcesAsync(
        TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return GetCurrentMetrics();
            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    private Dictionary<string, GpuMetrics> GetGpuMetrics()
    {
        var gpuMetrics = new Dictionary<string, GpuMetrics>();
        try
        {
            // This could be implemented using OpenCL or another cross-platform GPU API
            // Here we provide a basic implementation using nvidia-smi
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (process.Start())
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var values = lines[i].Split(',').Select(x => x.Trim()).ToArray();
                        if (values.Length >= 5)
                        {
                            gpuMetrics[$"gpu{i}"] = new GpuMetrics
                            {
                                Name = values[0],
                                UtilizationPercent = double.Parse(values[1]),
                                MemoryUsedBytes = long.Parse(values[2]) * 1024 * 1024,
                                TotalMemoryBytes = long.Parse(values[3]) * 1024 * 1024,
                                TemperatureCelsius = double.Parse(values[4])
                            };
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting GPU metrics");
        }
        return gpuMetrics;
    }

    private Dictionary<string, DiskMetrics> GetDiskMetrics()
    {
        var diskMetrics = new Dictionary<string, DiskMetrics>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var usagePercent = (drive.TotalSize - drive.AvailableFreeSpace) / (double)drive.TotalSize * 100;

                diskMetrics[drive.Name] = new DiskMetrics
                {
                    Name = drive.Name,
                    MountPoint = drive.RootDirectory.FullName,
                    AvailableSpaceBytes = drive.AvailableFreeSpace,
                    TotalSizeBytes = drive.TotalSize,
                    UsagePercent = Math.Round(usagePercent, 2)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting disk metrics");
        }
        return diskMetrics;
    }

    public void Dispose()
    {
        _process.Dispose();
        GC.SuppressFinalize(this);
    }
}