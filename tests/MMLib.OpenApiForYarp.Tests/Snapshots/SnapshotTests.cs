using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Aggregation;
using MMLib.OpenApiForYarp.Configuration;
using MMLib.OpenApiForYarp.OpenApi;
using MMLib.OpenApiForYarp.PathTransformation;
using MMLib.OpenApiForYarp.Transformers;
using Microsoft.Extensions.Logging.Abstractions;

namespace MMLib.OpenApiForYarp.Tests.Snapshots;

/// <summary>
/// Golden-file snapshot tests over the serialized transformed/merged documents, to catch
/// regressions in edge cases that are hard to assert on manually.
/// </summary>
public class SnapshotTests
{
    [Fact]
    public async Task Petstore_WithPrefix()
    {
        OpenApiDocument doc = FixtureLoader.Load("petstore.openapi.json");
        var route = FakeYarp.Route("pets", "pets-cluster", "/api/pets/{**catch-all}", ("PathPattern", "/pets/{**catch-all}"));

        new YarpPathRewriter().RewriteDocumentPaths(doc, [route]);
        string json = await OpenApiSerializer.SerializeAsync(doc, OpenApiSpecVersion.OpenApi3_0, TestContext.Current.CancellationToken);

        await Verifier.VerifyJson(json).UseDirectory("Snapshots");
    }

    [Fact]
    public async Task OrdersService_AddOnlyPublished()
    {
        OpenApiDocument doc = FixtureLoader.Load("orders.openapi.json");
        var route = FakeYarp.Route("orders", "orders-cluster", "/api/orders/{**catch-all}", ("PathPattern", "/orders/{**catch-all}"));
        var options = new YarpOpenApiClusterOptions { AddOnlyPublishedPaths = true };
        var context = TestContexts.ForCluster(doc, TestServices.Empty, options: options, routes: [route]);

        await new PathRewriteTransformer().TransformAsync(doc, context, TestContext.Current.CancellationToken);
        await new PublishedPathsFilterTransformer().TransformAsync(doc, context, TestContext.Current.CancellationToken);
        string json = await OpenApiSerializer.SerializeAsync(doc, OpenApiSpecVersion.OpenApi3_0, TestContext.Current.CancellationToken);

        await Verifier.VerifyJson(json).UseDirectory("Snapshots");
    }

    [Fact]
    public async Task TwoServices_Merged()
    {
        OpenApiDocument pets = FixtureLoader.Load("petstore.openapi.json");
        new YarpPathRewriter().RewriteDocumentPaths(pets,
            [FakeYarp.Route("pets", "pets", "/api/pets/{**catch-all}", ("PathPattern", "/pets/{**catch-all}"))]);

        OpenApiDocument auth = FixtureLoader.Load("auth-service.openapi.json");
        new YarpPathRewriter().RewriteDocumentPaths(auth,
            [FakeYarp.Route("auth", "auth", "/api/auth/{**catch-all}", ("PathPattern", "/{**catch-all}"))]);

        OpenApiDocument merged = new OpenApiDocumentMerger(NullLogger<OpenApiDocumentMerger>.Instance)
            .Merge([("pets", pets), ("auth", auth)], new MergedDocumentOptions { Title = "Gateway API", Version = "1.0.0" });
        string json = await OpenApiSerializer.SerializeAsync(merged, OpenApiSpecVersion.OpenApi3_0, TestContext.Current.CancellationToken);

        await Verifier.VerifyJson(json).UseDirectory("Snapshots");
    }
}
