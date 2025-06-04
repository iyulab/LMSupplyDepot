using LMSupplyDepots.Utils;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace LMSupplyDepots.Host.Controllers;

public static class HttpResponseExtensions
{
    public static async Task WriteAsJsonAsync<T>(this HttpResponse response, T obj, int statusCode = 200)
    {
        response.ContentType = "application/json";
        response.StatusCode = statusCode;

        await response.WriteAsync(JsonHelper.Serialize(obj));
    }

    // 필요한 경우 추가 옵션을 포함한 오버로드 메서드
    public static async Task WriteAsJsonAsync<T>(this HttpResponse response, T obj, JsonSerializerOptions options, int statusCode = 200)
    {
        response.ContentType = "application/json";
        response.StatusCode = statusCode;

        await response.WriteAsync(JsonHelper.Serialize(obj, options));
    }
}
