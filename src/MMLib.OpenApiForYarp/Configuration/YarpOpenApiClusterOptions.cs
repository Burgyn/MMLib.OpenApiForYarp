namespace MMLib.OpenApiForYarp.Configuration;

/// <summary>
/// Per-cluster aggregation options. Keyed in <see cref="YarpOpenApiOptions.Clusters"/> by the
/// YARP cluster id, so each downstream cluster can be configured independently.
/// </summary>
public sealed class YarpOpenApiClusterOptions
{
    /// <summary>
    /// The display title for this cluster's document (shown as the tab/label in the UI).
    /// When <see langword="null"/>, the downstream document's own <c>info.title</c> or the
    /// cluster id is used.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The path on the downstream service that serves its OpenAPI JSON document.
    /// Defaults to <c>/openapi/v1.json</c> (the convention of <c>Microsoft.AspNetCore.OpenApi</c>).
    /// </summary>
    public string OpenApiPath { get; set; } = "/openapi/v1.json";

    /// <summary>
    /// When <see langword="true"/>, only paths that the gateway actually proxies (i.e. that match
    /// a YARP route for this cluster) are kept in the aggregated document. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AddOnlyPublishedPaths { get; set; }

    /// <summary>
    /// Optional regular expressions; when set, only gateway-facing paths matching at least one
    /// pattern are included. Applied after path rewriting.
    /// </summary>
    public string[]? IncludePaths { get; set; }

    /// <summary>
    /// Optional regular expressions; gateway-facing paths matching any pattern are excluded.
    /// Applied after <see cref="IncludePaths"/>.
    /// </summary>
    public string[]? ExcludePaths { get; set; }

    /// <summary>
    /// Optional name of the single security scheme to keep for this cluster. When set, all other
    /// downstream security schemes are dropped from this cluster's document.
    /// </summary>
    public string? SecurityScheme { get; set; }
}
