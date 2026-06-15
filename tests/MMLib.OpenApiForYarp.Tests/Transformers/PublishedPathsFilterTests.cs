using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Abstractions;
using MMLib.OpenApiForYarp.Configuration;
using MMLib.OpenApiForYarp.Transformers;

namespace MMLib.OpenApiForYarp.Tests.Transformers;

public class PublishedPathsFilterTests
{
    private readonly PathRewriteTransformer _rewrite = new();
    private readonly PublishedPathsFilterTransformer _filter = new();

    private async Task<OpenApiDocument> RunAsync(YarpOpenApiClusterOptions options)
    {
        var doc = TestDocuments.WithPaths(
            "/products/{id}", "/products", "/orders/{id}", "/admin/secret", "/internal/health");

        var productsRoute = FakeYarp.Route("products", "c", "/api/products/{**catch-all}", ("PathPattern", "/products/{**catch-all}"));
        var ordersRoute = FakeYarp.Route("orders", "c", "/api/orders/{**catch-all}", ("PathPattern", "/orders/{**catch-all}"));
        var context = TestContexts.ForCluster(doc, TestServices.Empty, options: options, routes: [productsRoute, ordersRoute]);

        await _rewrite.TransformAsync(doc, context, CancellationToken.None);
        await _filter.TransformAsync(doc, context, CancellationToken.None);
        return doc;
    }

    [Fact]
    public async Task FiltersUnpublishedPaths()
    {
        var doc = await RunAsync(new YarpOpenApiClusterOptions { AddOnlyPublishedPaths = true });

        doc.Paths.Keys.ShouldBe(["/api/products/{id}", "/api/products", "/api/orders/{id}"], ignoreOrder: true);
        doc.Paths.ShouldNotContainKey("/admin/secret");
        doc.Paths.ShouldNotContainKey("/internal/health");
    }

    [Fact]
    public async Task KeepsAllWhenDisabled()
    {
        var doc = await RunAsync(new YarpOpenApiClusterOptions { AddOnlyPublishedPaths = false });

        doc.Paths.Count.ShouldBe(5);
        doc.Paths.ShouldContainKey("/admin/secret");
    }

    [Fact]
    public async Task RegexPattern_IncludeOnly()
    {
        var doc = await RunAsync(new YarpOpenApiClusterOptions { IncludePaths = ["^/api/products"] });

        doc.Paths.Keys.ShouldBe(["/api/products/{id}", "/api/products"], ignoreOrder: true);
    }

    [Fact]
    public async Task RegexPattern_ExcludePartial()
    {
        var doc = await RunAsync(new YarpOpenApiClusterOptions { ExcludePaths = ["/internal"] });

        doc.Paths.ShouldNotContainKey("/internal/health");
        doc.Paths.ShouldContainKey("/api/products/{id}");
    }
}
