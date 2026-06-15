using MMLib.OpenApiForYarp.Transformers;

namespace MMLib.OpenApiForYarp.Tests.Transformers;

public class PathRewriteTransformerTests
{
    [Fact]
    public async Task RewritesPaths_AndRecordsPublishedSet()
    {
        var transformer = new PathRewriteTransformer();
        var doc = TestDocuments.WithPaths("/products/{id}");
        var route = FakeYarp.Route("products", "c", "/api/products/{**catch-all}", ("PathPattern", "/products/{**catch-all}"));
        var context = TestContexts.ForCluster(doc, TestServices.Empty, routes: [route]);

        await transformer.TransformAsync(doc, context, CancellationToken.None);

        doc.Paths.ShouldContainKey("/api/products/{id}");
        doc.Paths.ShouldNotContainKey("/products/{id}");
        var published = (HashSet<string>)context.Items[PathRewriteTransformer.PublishedPathsKey]!;
        published.ShouldContain("/api/products/{id}");
    }
}
