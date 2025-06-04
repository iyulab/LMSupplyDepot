using LMSupplyDepots.LLamaEngine.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace LMSupplyDepots.LLamaEngine.Tests;

public class SystemMonitorServiceTests : IDisposable
{
    private readonly Mock<ILogger<SystemMonitorService>> _loggerMock;
    private readonly SystemMonitorService _service;

    public SystemMonitorServiceTests()
    {
        _loggerMock = new Mock<ILogger<SystemMonitorService>>();
        _service = new SystemMonitorService(_loggerMock.Object);
    }

    [Fact]
    public async Task MonitorResourcesAsync_ProducesStreamOfMetrics()
    {
        // Arrange
        var interval = TimeSpan.FromMilliseconds(100);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var metricsCount = 0;

        // Act & Assert
        try
        {
            await foreach (var metrics in _service.MonitorResourcesAsync(interval, cts.Token))
            {
                Assert.NotNull(metrics);
                Assert.True(metrics.CpuUsagePercent >= 0);
                Assert.True(metrics.CpuUsagePercent <= 100);
                Assert.True(metrics.MemoryUsagePercent >= 0);
                metricsCount++;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation occurs
        }

        Assert.True(metricsCount > 0, $"Should have collected at least one metric (got {metricsCount})");
    }

    [Fact]
    public void GetCurrentMetrics_IncludesDiskMetrics()
    {
        // Act
        var metrics = _service.GetCurrentMetrics();

        // Assert
        Assert.NotNull(metrics.DiskMetrics);
        foreach (var disk in metrics.DiskMetrics.Values)
        {
            Assert.NotNull(disk.Name);
            Assert.NotNull(disk.MountPoint);
            Assert.True(disk.TotalSizeBytes >= disk.AvailableSpaceBytes);
            Assert.True(disk.UsagePercent >= 0 && disk.UsagePercent <= 100);
        }
    }

    [Fact]
    public void GetCurrentMetrics_HandlesConcurrentAccess()
    {
        // Arrange
        var tasks = new List<Task<SystemMetrics>>();

        // Act - 여러 스레드에서 동시에 메트릭 수집
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => _service.GetCurrentMetrics()));
        }

        // Assert - 모든 호출이 예외 없이 완료되는지 확인
        var results = Task.WhenAll(tasks).GetAwaiter().GetResult();

        foreach (var metrics in results)
        {
            Assert.NotNull(metrics);
            Assert.True(metrics.CpuUsagePercent >= 0);
            Assert.True(metrics.CpuUsagePercent <= 100);
        }
    }

    [Fact]
    public async Task MonitorResourcesAsync_RespectsInterval()
    {
        // Arrange
        var interval = TimeSpan.FromMilliseconds(100);
        var cts = new CancellationTokenSource();
        var startTime = DateTime.UtcNow;
        var metricsCount = 0;

        // Act
        await foreach (var _ in _service.MonitorResourcesAsync(interval, cts.Token))
        {
            metricsCount++;
            if (metricsCount >= 3)
            {
                cts.Cancel();
                break;
            }
        }

        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.True(elapsed >= interval * 2);
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}