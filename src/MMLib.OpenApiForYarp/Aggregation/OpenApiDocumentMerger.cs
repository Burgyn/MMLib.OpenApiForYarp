using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Configuration;

namespace MMLib.OpenApiForYarp.Aggregation;

/// <summary>Merges several transformed cluster documents into one combined document.</summary>
internal sealed class OpenApiDocumentMerger(ILogger<OpenApiDocumentMerger> logger)
{
    private readonly ILogger<OpenApiDocumentMerger> _logger = logger;

    /// <summary>
    /// Merges the supplied documents. The merged <c>info</c> comes from <paramref name="info"/>;
    /// paths, component schemas, and security schemes are unioned by key with first-occurrence
    /// winning and a warning logged on conflict.
    /// </summary>
    public OpenApiDocument Merge(IReadOnlyList<(string ClusterId, OpenApiDocument Document)> documents, MergedDocumentOptions info)
    {
        var merged = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = info.Title,
                Version = info.Version,
                Description = info.Description,
            },
            Paths = [],
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal),
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal),
            },
        };

        foreach ((string clusterId, OpenApiDocument document) in documents)
        {
            MergePaths(merged, document, clusterId);
            MergeSchemas(merged, document, clusterId);
            MergeSecuritySchemes(merged, document, clusterId);
        }

        return merged;
    }

    private void MergePaths(OpenApiDocument merged, OpenApiDocument source, string clusterId)
    {
        if (source.Paths is null)
        {
            return;
        }

        foreach ((string path, IOpenApiPathItem item) in source.Paths)
        {
            if (merged.Paths.ContainsKey(path))
            {
                _logger.LogWarning("Path '{Path}' from cluster '{ClusterId}' conflicts with an existing path; keeping the first occurrence.", path, clusterId);
                continue;
            }

            merged.Paths[path] = item;
        }
    }

    private void MergeSchemas(OpenApiDocument merged, OpenApiDocument source, string clusterId)
    {
        if (source.Components?.Schemas is not { } schemas)
        {
            return;
        }

        foreach ((string name, IOpenApiSchema schema) in schemas)
        {
            if (merged.Components!.Schemas!.ContainsKey(name))
            {
                _logger.LogWarning("Schema '{Schema}' from cluster '{ClusterId}' conflicts with an existing schema; keeping the first occurrence.", name, clusterId);
                continue;
            }

            merged.Components.Schemas[name] = schema;
        }
    }

    private void MergeSecuritySchemes(OpenApiDocument merged, OpenApiDocument source, string clusterId)
    {
        if (source.Components?.SecuritySchemes is not { } schemes)
        {
            return;
        }

        foreach ((string name, IOpenApiSecurityScheme scheme) in schemes)
        {
            if (merged.Components!.SecuritySchemes!.ContainsKey(name))
            {
                _logger.LogWarning("Security scheme '{Name}' from cluster '{ClusterId}' is already defined; keeping the first occurrence.", name, clusterId);
                continue;
            }

            merged.Components.SecuritySchemes[name] = scheme;
        }
    }
}
