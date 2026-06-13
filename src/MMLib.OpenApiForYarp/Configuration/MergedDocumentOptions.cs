namespace MMLib.OpenApiForYarp.Configuration;

/// <summary>
/// Describes the <c>info</c> block of the merged document served at <c>/openapi/all.json</c>.
/// These values come from the gateway's own configuration, not from any downstream service.
/// </summary>
public sealed class MergedDocumentOptions
{
    /// <summary>The title of the merged document. Defaults to <c>"Gateway API"</c>.</summary>
    public string Title { get; set; } = "Gateway API";

    /// <summary>The version of the merged document. Defaults to <c>"1.0.0"</c>.</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>An optional description for the merged document.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// The route at which the merged document is served. Defaults to <c>/openapi/all.json</c>.
    /// </summary>
    public string RoutePattern { get; set; } = "/openapi/all.json";

    /// <summary>The identifier used for the merged document in UI adapters. Defaults to <c>"all"</c>.</summary>
    public string DocumentName { get; set; } = "all";
}
