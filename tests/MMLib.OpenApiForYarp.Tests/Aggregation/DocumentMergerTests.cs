using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Aggregation;
using MMLib.OpenApiForYarp.Configuration;

namespace MMLib.OpenApiForYarp.Tests.Aggregation;

public class DocumentMergerTests
{
    private readonly RecordingLogger<OpenApiDocumentMerger> _logger = new();

    private OpenApiDocument Merge(params (string ClusterId, OpenApiDocument Document)[] docs)
        => new OpenApiDocumentMerger(_logger).Merge(docs, new MergedDocumentOptions { Title = "Gateway", Version = "9.9" });

    [Fact]
    public void TwoServices_NoConflict_AllPathsPresent()
    {
        var merged = Merge(
            ("products", TestDocuments.WithPaths("/api/products", "/api/products/{id}")),
            ("orders", TestDocuments.WithPaths("/api/orders", "/api/orders/{id}")));

        merged.Paths.Count.ShouldBe(4);
        merged.Paths.Keys.ShouldBe(["/api/products", "/api/products/{id}", "/api/orders", "/api/orders/{id}"], ignoreOrder: true);
    }

    [Fact]
    public void TwoServices_PathConflict_KeepsFirst_AndWarns()
    {
        var merged = Merge(
            ("products", TestDocuments.WithPaths("/api/shared")),
            ("orders", TestDocuments.WithPaths("/api/shared")));

        merged.Paths.Count.ShouldBe(1);
        _logger.HasWarning.ShouldBeTrue();
    }

    [Fact]
    public void ThreeServices_SchemaConflict_Deduplicated()
    {
        var merged = Merge(
            ("a", TestDocuments.WithPaths("/a").WithSchemas("Product")),
            ("b", TestDocuments.WithPaths("/b").WithSchemas("Product")),
            ("c", TestDocuments.WithPaths("/c").WithSchemas("Order")));

        merged.Components!.Schemas!.Keys.ShouldBe(["Product", "Order"], ignoreOrder: true);
    }

    [Fact]
    public void MergedInfo_ComesFromGateway()
    {
        var merged = Merge(("products", TestDocuments.WithPaths("/a")));

        merged.Info!.Title.ShouldBe("Gateway");
        merged.Info.Version.ShouldBe("9.9");
    }

    [Fact]
    public void SecuritySchemes_Deduplicated()
    {
        var merged = Merge(
            ("a", TestDocuments.WithPaths("/a").WithSecuritySchemes("Bearer")),
            ("b", TestDocuments.WithPaths("/b").WithSecuritySchemes("Bearer")));

        merged.Components!.SecuritySchemes!.Keys.ShouldBe(["Bearer"]);
    }
}
