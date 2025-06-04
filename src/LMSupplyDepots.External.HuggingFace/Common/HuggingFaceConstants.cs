using System.Web;

namespace LMSupplyDepots.External.HuggingFace.Common;

public static class HuggingFaceConstants
{
    private const string Host = "huggingface.co";
    private const string BaseUrl = $"https://{Host}";
    private const string ApiPath = "/api";
    private const string ModelsPath = $"{ApiPath}/models";

    public static class Headers
    {
        public const string Authorization = "Authorization";
        public const string AuthorizationFormat = "Bearer {0}";
    }

    public static class QueryParams
    {
        public const string Full = "full";
        public const string Config = "config";
        public const string Download = "download";
        public const string Search = "search";
        public const string Filter = "filter";
        public const string Author = "author";
        public const string Limit = "limit";
        public const string Sort = "sort";
        public const string Direction = "direction";
    }

    public static class Defaults
    {
        public const string ModelsQuery = "full=true&config=false";
        public const string FileQuery = "download=true";
    }

    public static class UrlBuilder
    {
        public static string CreateModelSearchUrl(
            string? search = null,
            string[]? filters = null,
            int? limit = null,
            string? sort = null,
            bool descending = true)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query[QueryParams.Full] = "true";
            query[QueryParams.Config] = "false";

            if (!string.IsNullOrWhiteSpace(search))
                query[QueryParams.Search] = search;

            if (filters?.Length > 0)
            {
                query[QueryParams.Filter] = string.Join(",", filters);
            }

            if (limit.HasValue)
                query[QueryParams.Limit] = limit.ToString();

            if (!string.IsNullOrWhiteSpace(sort))
                query[QueryParams.Sort] = sort;

            query[QueryParams.Direction] = descending ? "-1" : "1";

            return $"{BaseUrl}{ModelsPath}?{query}";
        }

        public static string CreateModelUrl(string repoId)
        {
            if (string.IsNullOrWhiteSpace(repoId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repoId));

            return $"{BaseUrl}{ModelsPath}/{repoId}";
        }

        public static string CreateFileUrl(string repoId, string filePath)
        {
            if (string.IsNullOrWhiteSpace(repoId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repoId));

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            var query = HttpUtility.ParseQueryString(string.Empty);
            query[QueryParams.Download] = "true";

            return $"{BaseUrl}/{repoId}/resolve/main/{Uri.EscapeDataString(filePath)}?{query}";
        }

        public static string CreateRepositoryTreeUrl(string repoId, string? treePath = null)
        {
            if (string.IsNullOrWhiteSpace(repoId))
                throw new ArgumentException("Repository ID cannot be null or empty.", nameof(repoId));

            var path = treePath != null
                ? $"{repoId}/tree/main/{treePath}"
                : $"{repoId}/tree/main";

            return $"{BaseUrl}{ModelsPath}/{path}";
        }
    }
}