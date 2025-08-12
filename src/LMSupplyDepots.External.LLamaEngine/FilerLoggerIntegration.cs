using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace LMSupplyDepots.External.LLamaEngine;

/// <summary>
/// LLamaEngine과 Filer AppLog 통합을 위한 헬퍼 클래스
/// </summary>
internal static class FilerLoggerIntegration
{
    /// <summary>
    /// 메시지 내용을 분석하여 올바른 로그 레벨을 결정합니다.
    /// </summary>
    public static void LogMessage(ILogger logger, string message, Exception? exception = null)
    {
        // GPU/Vulkan 관련 메시지는 정보성이므로 Info 레벨로 처리
        if (IsInformationalMessage(message))
        {
            if (exception == null)
            {
                logger.LogInformation(message);
            }
            else
            {
                logger.LogInformation(exception, message);
            }
            return;
        }

        // 경고성 메시지인지 확인
        if (IsWarningMessage(message))
        {
            if (exception == null)
            {
                logger.LogWarning(message);
            }
            else
            {
                logger.LogWarning(exception, message);
            }
            return;
        }

        // 실제 에러인 경우만 Error 레벨로 처리
        if (exception == null)
        {
            logger.LogError(message);
        }
        else
        {
            logger.LogError(exception, message);
        }
    }

    /// <summary>
    /// 정보성 메시지인지 확인합니다.
    /// </summary>
    private static bool IsInformationalMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        var lowerMessage = message.ToLowerInvariant();

        // GPU/Vulkan 감지 관련 정보성 메시지들
        return lowerMessage.Contains("ggml_vulkan") ||
               lowerMessage.Contains("found") && lowerMessage.Contains("vulkan") && lowerMessage.Contains("devices") ||
               lowerMessage.Contains("cuda") && lowerMessage.Contains("available") ||
               lowerMessage.Contains("backend") && lowerMessage.Contains("initialized") ||
               lowerMessage.Contains("hardware acceleration") ||
               lowerMessage.Contains("detected");
    }

    /// <summary>
    /// 경고성 메시지인지 확인합니다.
    /// </summary>
    private static bool IsWarningMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        var lowerMessage = message.ToLowerInvariant();

        return lowerMessage.Contains("warning") ||
               lowerMessage.Contains("fallback") ||
               lowerMessage.Contains("failed to initialize") && !lowerMessage.Contains("error");
    }

    /// <summary>
    /// 안전한 메시지 포맷팅 (AppLog.FormatMessage와 유사하지만 더 안전함)
    /// </summary>
    public static string SafeFormatMessage(string message, params object?[] args)
    {
        if (string.IsNullOrEmpty(message))
            return "[No message]";
            
        if (args == null || args.Length == 0)
            return message;
            
        try
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, message, args);
        }
        catch (FormatException)
        {
            // 포맷팅 실패 시 원본 메시지와 인자들을 안전하게 결합
            return $"{message} [Args: {string.Join(", ", args.Select(a => a?.ToString() ?? "null"))}]";
        }
    }
}