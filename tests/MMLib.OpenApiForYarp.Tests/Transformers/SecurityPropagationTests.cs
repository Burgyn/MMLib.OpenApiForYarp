using MMLib.OpenApiForYarp.Configuration;
using MMLib.OpenApiForYarp.Transformers;

namespace MMLib.OpenApiForYarp.Tests.Transformers;

public class SecurityPropagationTests
{
    private readonly SecurityPropagationTransformer _transformer = new();

    [Fact]
    public async Task PropagateBearer_SingleService_KeepsScheme()
    {
        var doc = TestDocuments.WithPaths("/products").WithSecuritySchemes("Bearer");
        var context = TestContexts.ForCluster(doc, TestServices.Empty);

        await _transformer.TransformAsync(doc, context, CancellationToken.None);

        doc.Components!.SecuritySchemes!.ShouldContainKey("Bearer");
    }

    [Fact]
    public async Task ExplicitOverride_PerCluster_KeepsOnlyNamedScheme()
    {
        var doc = TestDocuments.WithPaths("/products").WithSecuritySchemes("Bearer", "ApiKey");
        var options = new YarpOpenApiClusterOptions { SecurityScheme = "Bearer" };
        var context = TestContexts.ForCluster(doc, TestServices.Empty, options: options);

        await _transformer.TransformAsync(doc, context, CancellationToken.None);

        doc.Components!.SecuritySchemes!.Keys.ShouldBe(["Bearer"]);
    }

    [Fact]
    public async Task NoSecurityInDownstream_NoSchemeInOutput()
    {
        var doc = TestDocuments.WithPaths("/products");
        var context = TestContexts.ForCluster(doc, TestServices.Empty);

        await _transformer.TransformAsync(doc, context, CancellationToken.None);

        (doc.Components?.SecuritySchemes is null || doc.Components.SecuritySchemes.Count == 0).ShouldBeTrue();
    }
}
