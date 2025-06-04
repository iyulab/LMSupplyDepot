using LMSupplyDepots.External.HuggingFace.Client;
using System.Text.RegularExpressions;
namespace LMSupplyDepots.External.HuggingFace.Models;

public record LMModelArtifactFileInfo
{
    public required string Path { get; init; }
    public required long Size { get; init; }
}

public record LMModelArtifact
{
    public required string Name { get; init; }
    public required IReadOnlyList<LMModelArtifactFileInfo> Files { get; init; }
    public required long TotalSize { get; init; }
    public string? Format { get; init; }
}

public static partial class LMModelArtifactAnalyzer
{
    [GeneratedRegex(@"^(?<base>.+?)-(?<number>\d{5})-of-(?<total>\d{5})\.(?<format>[^.]+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SplitFilePatternGenerator();
    private static readonly Regex SplitFilePattern = SplitFilePatternGenerator();

    public static async Task<IReadOnlyList<LMModelArtifact>> GetModelArtifactsAsync(
        this IHuggingFaceClient client,
        string repoId,
        CancellationToken cancellationToken = default)
    {
        var model = await client.FindModelByRepoIdAsync(repoId, cancellationToken);
        var fileSizes = await client.GetRepositoryFileSizesAsync(repoId, cancellationToken);
        return GetModelArtifacts(model, fileSizes);
    }

    private static string GetArtifactName(string fileName, string format)
    {
        // Remove extension from all files since format is stored separately
        return Path.GetFileNameWithoutExtension(fileName);
    }

    public static IReadOnlyList<LMModelArtifact> GetModelArtifacts(
        HuggingFaceModel model,
        Dictionary<string, long> fileSizes)
    {
        var modelFiles = model.GetModelWeightPaths()
            .Select(file => new
            {
                Path = file,
                Size = fileSizes.GetValueOrDefault(file, 0),
                Format = Path.GetExtension(file).TrimStart('.'),
                Match = SplitFilePattern.Match(Path.GetFileName(file))
            })
            .ToList();

        var artifacts = new List<LMModelArtifact>();

        // Group split files
        var splitFiles = modelFiles
            .Where(f => f.Match.Success)
            .GroupBy(f => new {
                Base = f.Match.Groups["base"].Value,
                Format = f.Match.Groups["format"].Value
            })
            .ToList();

        foreach (var group in splitFiles)
        {
            var files = group.OrderBy(f => f.Path)
                .Select(f => new LMModelArtifactFileInfo
                {
                    Path = f.Path,
                    Size = f.Size
                })
                .ToArray();
            var totalSize = files.Sum(f => f.Size);

            var artifactName = GetArtifactName(
                $"{group.Key.Base}.{group.Key.Format}",
                group.Key.Format
            );

            artifacts.Add(new LMModelArtifact
            {
                Name = artifactName,
                Files = files,
                TotalSize = totalSize,
                Format = group.Key.Format
            });

            // Remove processed files
            modelFiles.RemoveAll(f => files.Any(ff => ff.Path == f.Path));
        }

        // Process remaining single files
        foreach (var file in modelFiles)
        {
            var artifactName = GetArtifactName(
                Path.GetFileName(file.Path),
                file.Format
            );

            artifacts.Add(new LMModelArtifact
            {
                Name = artifactName,
                Files = [new LMModelArtifactFileInfo
                {
                    Path = file.Path,
                    Size = file.Size
                }],
                TotalSize = file.Size,
                Format = file.Format
            });
        }

        return [.. artifacts.OrderBy(a => a.Name)];
    }
}