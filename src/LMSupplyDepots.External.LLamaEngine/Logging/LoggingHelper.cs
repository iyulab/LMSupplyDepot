using Microsoft.Extensions.Logging;
using System.Globalization;

namespace LMSupplyDepots.External.LLamaEngine.Logging;

/// <summary>
/// GPU 관련 메시지를 올바른 로그 레벨로 처리하는 헬퍼 클래스
/// </summary>
public static class LoggingHelper
{
    /// <summary>
    /// GPU/하드웨어 관련 메시지를 적절한 로그 레벨로 출력합니다.
    /// </summary>
    public static void LogGpuMessage(ILogger logger, string message, Exception? exception = null)
    {
        if (string.IsNullOrEmpty(message))
            return;

        try
        {
            var logLevel = DetermineLogLevel(message);
            
            switch (logLevel)
            {
                case LogLevel.Information:
                    if (exception == null)
                        logger.LogInformation(message);
                    else
                        logger.LogInformation(exception, message);
                    break;
                    
                case LogLevel.Warning:
                    if (exception == null)
                        logger.LogWarning(message);
                    else
                        logger.LogWarning(exception, message);
                    break;
                    
                case LogLevel.Error:
                default:
                    if (exception == null)
                        logger.LogError(message);
                    else
                        logger.LogError(exception, message);
                    break;
            }
        }
        catch (Exception ex)
        {
            // 로깅 자체에서 에러가 발생한 경우 최소한의 로깅
            try
            {
                logger.LogWarning(ex, "Error occurred while logging GPU message: {OriginalMessage}", message);
            }
            catch
            {
                // 로깅이 완전히 실패한 경우 무시 (무한 루프 방지)
            }
        }
    }

    /// <summary>
    /// 메시지 내용을 분석하여 적절한 로그 레벨을 결정합니다.
    /// </summary>
    private static LogLevel DetermineLogLevel(string message)
    {
        if (string.IsNullOrEmpty(message))
            return LogLevel.Information;

        var lowerMessage = message.ToLowerInvariant();

        // GPU 감지 관련 정보성 메시지들
        if (IsInformationalGpuMessage(lowerMessage))
        {
            return LogLevel.Information;
        }

        // 경고성 메시지들
        if (IsWarningMessage(lowerMessage))
        {
            return LogLevel.Warning;
        }

        // 실제 에러 메시지들
        if (IsErrorMessage(lowerMessage))
        {
            return LogLevel.Error;
        }

        // 기본적으로 정보성으로 처리
        return LogLevel.Information;
    }

    private static bool IsInformationalGpuMessage(string message)
    {
        // ggml_vulkan 관련 정보 메시지들
        if (message.Contains("ggml_vulkan"))
        {
            return message.Contains("found") && message.Contains("devices") ||
                   message.Contains("initialized") ||
                   message.Contains("available") ||
                   message.Contains("loaded");
        }

        // 기타 하드웨어 감지 관련 정보 메시지들
        return message.Contains("hardware acceleration") && message.Contains("detected") ||
               message.Contains("cuda") && message.Contains("available") ||
               message.Contains("vulkan") && message.Contains("available") ||
               message.Contains("backend") && message.Contains("initialized") ||
               message.Contains("gpu") && message.Contains("layers") && message.Contains("configured");
    }

    private static bool IsWarningMessage(string message)
    {
        return message.Contains("warning") ||
               message.Contains("fallback") ||
               message.Contains("failed to initialize") && !message.Contains("error") ||
               message.Contains("not available") ||
               message.Contains("disabled");
    }

    private static bool IsErrorMessage(string message)
    {
        return message.Contains("error") ||
               message.Contains("failed") && message.Contains("critical") ||
               message.Contains("exception") ||
               message.Contains("crash");
    }

    /// <summary>
    /// 안전한 문자열 포맷팅을 수행합니다.
    /// </summary>
    public static string SafeFormat(string format, params object?[] args)
    {
        if (string.IsNullOrEmpty(format))
            return "[Empty message]";
            
        if (args == null || args.Length == 0)
            return format;
            
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            // 포맷팅 실패 시 안전한 대안 반환
            var argsString = string.Join(", ", args.Select(a => a?.ToString() ?? "null"));
            return $"{format} [SafeFormat Args: {argsString}]";
        }
        catch (Exception ex)
        {
            return $"{format} [SafeFormat Error: {ex.Message}]";
        }
    }
}