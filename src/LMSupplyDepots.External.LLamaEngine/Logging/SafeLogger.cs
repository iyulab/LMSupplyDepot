using Microsoft.Extensions.Logging;
using System.Globalization;

namespace LMSupplyDepots.External.LLamaEngine.Logging;

/// <summary>
/// GPU 감지 메시지를 올바른 로그 레벨로 처리하는 안전한 로거 래퍼
/// </summary>
public class SafeLogger : ILogger
{
    private readonly ILogger _innerLogger;

    public SafeLogger(ILogger innerLogger)
    {
        _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _innerLogger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel)
        => _innerLogger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        try
        {
            var message = formatter(state, exception);
            
            // GPU/Vulkan 관련 메시지 필터링
            var adjustedLogLevel = AdjustLogLevel(message, logLevel);
            
            _innerLogger.Log(adjustedLogLevel, eventId, state, exception, formatter);
        }
        catch (FormatException)
        {
            // 포맷 에러가 발생한 경우 안전한 로깅
            var safeMessage = $"Logging format error - Original state: {state}, Exception: {exception?.Message}";
            _innerLogger.LogWarning(safeMessage);
        }
        catch (Exception ex)
        {
            // 기타 로깅 에러
            _innerLogger.LogError(ex, "Unexpected error during logging");
        }
    }

    /// <summary>
    /// 메시지 내용에 따라 적절한 로그 레벨을 결정합니다.
    /// </summary>
    private static LogLevel AdjustLogLevel(string message, LogLevel originalLevel)
    {
        if (string.IsNullOrEmpty(message))
            return originalLevel;

        var lowerMessage = message.ToLowerInvariant();

        // GPU 감지 관련 메시지는 정보성이므로 Info 레벨로 조정
        if (IsGpuDetectionMessage(lowerMessage))
        {
            return LogLevel.Information;
        }

        // 백엔드 초기화 관련 메시지도 정보성
        if (IsBackendInitializationMessage(lowerMessage))
        {
            return LogLevel.Information;
        }

        // 하드웨어 가속 관련 메시지
        if (IsHardwareAccelerationMessage(lowerMessage))
        {
            return LogLevel.Information;
        }

        return originalLevel;
    }

    private static bool IsGpuDetectionMessage(string message)
    {
        return message.Contains("ggml_vulkan") ||
               message.Contains("found") && message.Contains("vulkan") && message.Contains("devices") ||
               message.Contains("cuda") && message.Contains("available") ||
               message.Contains("gpu") && message.Contains("detected");
    }

    private static bool IsBackendInitializationMessage(string message)
    {
        return message.Contains("backend") && (
            message.Contains("initialized") ||
            message.Contains("loading") ||
            message.Contains("available")
        );
    }

    private static bool IsHardwareAccelerationMessage(string message)
    {
        return message.Contains("hardware acceleration") ||
               message.Contains("gpu layers") ||
               message.Contains("vulkan backend") ||
               message.Contains("cuda backend");
    }
}

/// <summary>
/// SafeLogger를 위한 팩토리 클래스
/// </summary>
public class SafeLogger<T> : SafeLogger, ILogger<T>
{
    public SafeLogger(ILogger<T> innerLogger) : base(innerLogger)
    {
    }
}