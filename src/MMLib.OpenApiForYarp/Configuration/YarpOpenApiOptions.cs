namespace MMLib.OpenApiForYarp.Configuration;

/// <summary>
/// Root options for OpenAPI aggregation, bound from the <c>YarpOpenApi</c> configuration section
/// (alongside YARP's own <c>ReverseProxy</c> section).
/// </summary>
public sealed class YarpOpenApiOptions
{
    /// <summary>The default configuration section name: <c>YarpOpenApi</c>.</summary>
    public const string SectionName = "YarpOpenApi";

    /// <summary>
    /// Per-cluster options keyed by YARP cluster id (case-insensitive). Clusters without an entry
    /// still get default options.
    /// </summary>
    public IDictionary<string, YarpOpenApiClusterOptions> Clusters { get; set; }
        = new Dictionary<string, YarpOpenApiClusterOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When <see langword="true"/>, a merged document containing every cluster's paths is served
    /// (see <see cref="MergedDocument"/>). Defaults to <see langword="false"/>.
    /// </summary>
    public bool MergeDocuments { get; set; }

    /// <summary>Options for the merged document (used only when <see cref="MergeDocuments"/> is <see langword="true"/>).</summary>
    public MergedDocumentOptions MergedDocument { get; set; } = new();

    /// <summary>
    /// How long a fetched downstream document is cached before being re-fetched. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The timeout applied when fetching a downstream document. Defaults to 30 seconds.</summary>
    public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The route template for per-cluster documents. The literal <c>{cluster}</c> token is replaced
    /// with the cluster id. Defaults to <c>/openapi/{cluster}.json</c>.
    /// </summary>
    public string DocumentRoutePattern { get; set; } = "/openapi/{cluster}.json";

    /// <summary>
    /// Builds the gateway-facing route for a given cluster's document from <see cref="DocumentRoutePattern"/>.
    /// </summary>
    /// <param name="clusterId">The YARP cluster id.</param>
    /// <returns>The resolved route, e.g. <c>/openapi/products-cluster.json</c>.</returns>
    public string GetDocumentRoute(string clusterId)
        => DocumentRoutePattern.Replace("{cluster}", clusterId, StringComparison.Ordinal);
}
