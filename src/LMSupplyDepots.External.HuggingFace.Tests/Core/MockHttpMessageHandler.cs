using LMSupplyDepots.External.HuggingFace.Models;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LMSupplyDepots.External.HuggingFace.Tests.Core;

public class MockHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // 기본 인증 체크
        var hasAuth = request.Headers.Contains("Authorization");
        if (!hasAuth)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("Authentication required")
            });
        }

        // 모델 상세 조회
        if (request.RequestUri!.PathAndQuery.Contains("/api/models/"))
        {
            // URL 디코딩하여 실제 모델 ID 추출
            var encodedModelId = request.RequestUri.PathAndQuery.Split("/api/models/")[1];
            var modelId = Uri.UnescapeDataString(encodedModelId);

            if (modelId.Contains("nonexistent"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("Model not found")
                });
            }

            var model = CreateMockModel(modelId, true, isTextGeneration: true);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(model),
                    Encoding.UTF8,
                    "application/json")
            });
        }

        // 모델 목록 조회
        if (request.RequestUri.PathAndQuery.Contains("/api/models"))
        {
            var queryParams = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var filters = (queryParams["filter"] ?? "").Split(',');
            var isTextGenerationRequest = filters.Contains("text-generation");
            var isEmbeddingRequest = filters.Contains("sentence-similarity");

            var allModels = new List<HuggingFaceModel>
            {
                CreateMockModel("model1", true, isTextGeneration: true),
                CreateMockModel("model2", true, isTextGeneration: true),
                CreateMockModel("embed1", true, isTextGeneration: false)
            };

            var filteredModels = allModels.Where(m =>
                (isTextGenerationRequest && m.Tags.Contains("text-generation")) ||
                (isEmbeddingRequest && m.Tags.Contains("sentence-similarity")))
                .ToList();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(filteredModels),
                    Encoding.UTF8,
                    "application/json")
            });
        }

        // 파일 다운로드 또는 정보 조회
        if (request.RequestUri.PathAndQuery.Contains("/resolve/main/"))
        {
            if (request.RequestUri.PathAndQuery.Contains("nonexistent"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("File not found")
                });
            }

            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response.Content.Headers.ContentLength = 1024;
                response.Content.Headers.LastModified = DateTimeOffset.UtcNow;
                return Task.FromResult(response);
            }

            var content = Encoding.UTF8.GetBytes("mock file content");
            var downloadResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
            downloadResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            downloadResponse.Content.Headers.ContentLength = content.Length;
            downloadResponse.Content.Headers.LastModified = DateTimeOffset.UtcNow;

            return Task.FromResult(downloadResponse);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HuggingFaceModel CreateMockModel(string id, bool includeGguf, bool isTextGeneration)
    {
        var tags = new List<string> { "gguf" };
        if (isTextGeneration)
        {
            tags.Add("text-generation");
        }
        else
        {
            tags.Add("sentence-similarity");
        }

        var jsonString = $@"{{
            ""_id"": ""{id}"",
            ""modelId"": ""{id}"",
            ""author"": ""test-author"",
            ""downloads"": 1000,
            ""likes"": 100,
            ""lastModified"": ""{DateTime.UtcNow:o}"",
            ""createdAt"": ""{DateTime.UtcNow.AddDays(-10):o}"",
            ""private"": false,
            ""tags"": {JsonSerializer.Serialize(tags)},
            ""siblings"": [
                {{ ""rfilename"": ""config.json"" }},
                {{ ""rfilename"": ""model.bin"" }}
                {(includeGguf ? @", { ""rfilename"": ""model.gguf"" }" : "")}
            ]
        }}";

        return JsonSerializer.Deserialize<HuggingFaceModel>(jsonString) ?? new HuggingFaceModel();
    }
}