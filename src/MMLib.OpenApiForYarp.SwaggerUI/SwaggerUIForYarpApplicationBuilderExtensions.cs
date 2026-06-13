using Microsoft.Extensions.DependencyInjection;
using MMLib.OpenApiForYarp;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the Swagger UI, listing every gateway document in the document selector.</summary>
public static class SwaggerUIForYarpApplicationBuilderExtensions
{
    /// <summary>
    /// Serves the Swagger UI at <paramref name="routePrefix"/>, adding one Swagger endpoint per
    /// downstream cluster (and the merged document when enabled), each pointing at the gateway's
    /// <c>/openapi/{cluster}.json</c> route.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="routePrefix">The route prefix at which the Swagger UI is served. Defaults to <c>swagger</c>.</param>
    /// <param name="configure">Optional hook to further customize <see cref="SwaggerUIOptions"/>.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder MapSwaggerUIForYarp(
        this IApplicationBuilder app,
        string routePrefix = "swagger",
        Action<SwaggerUIOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        IClusterDocumentSource source = app.ApplicationServices.GetRequiredService<IClusterDocumentSource>();

        return app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = routePrefix.Trim('/');

            foreach (ClusterDocumentInfo document in source.GetDocuments())
            {
                options.SwaggerEndpoint(document.RoutePattern, document.Title);
            }

            if (source.MergedDocument is { } merged)
            {
                options.SwaggerEndpoint(merged.RoutePattern, merged.Title);
            }

            configure?.Invoke(options);
        });
    }
}
