using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MMLib.OpenApiForYarp;
using Scalar.AspNetCore;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the Scalar API reference UI, registering every gateway document as a Scalar document.</summary>
public static class ScalarForYarpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Scalar UI at <paramref name="endpointPrefix"/>, adding one Scalar document per
    /// downstream cluster (and the merged document when enabled). Each document points at the
    /// gateway's <c>/openapi/{cluster}.json</c> route.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="endpointPrefix">The route at which the Scalar UI is served. Defaults to <c>/scalar</c>.</param>
    /// <param name="configure">Optional hook to further customize <see cref="ScalarOptions"/>.</param>
    /// <returns>A builder for the mapped endpoint.</returns>
    public static IEndpointConventionBuilder MapScalarForYarp(
        this IEndpointRouteBuilder endpoints,
        string endpointPrefix = "/scalar",
        Action<ScalarOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        IClusterDocumentSource source = endpoints.ServiceProvider.GetRequiredService<IClusterDocumentSource>();

        return endpoints.MapScalarApiReference(endpointPrefix, (ScalarOptions options) =>
        {
            options.WithTitle("Gateway API Reference");

            foreach (ClusterDocumentInfo document in source.GetDocuments())
            {
                options.AddDocument(document.Name, document.Title, document.RoutePattern);
            }

            if (source.MergedDocument is { } merged)
            {
                options.AddDocument(merged.Name, merged.Title, merged.RoutePattern, isDefault: true);
            }

            configure?.Invoke(options);
        });
    }
}
