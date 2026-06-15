using System.Net.Http;
using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.OpenApi;
using MMLib.OpenApiForYarp.PathTransformation;
using Yarp.ReverseProxy.Configuration;

namespace MMLib.OpenApiForYarp.Tests.PathTransformation;

/// <summary>
/// Proves the path rewriter only rewrites path keys and leaves the full richness of the document
/// (every parameter kind, request bodies, inline and $ref schemas, components, security) intact.
/// </summary>
public class RichDocumentPreservationTests
{
    private static OpenApiDocument Rewrite(params (string Key, string Value)[] transforms)
    {
        OpenApiDocument document = FixtureLoader.Load("rich-api.openapi.json");
        var route = new RouteConfig
        {
            RouteId = "catalog",
            ClusterId = "catalog-cluster",
            Match = new RouteMatch { Path = "/api/catalog/{**catch-all}" },
            Transforms = [.. transforms.Select(t => new Dictionary<string, string> { [t.Key] = t.Value })],
        };
        new YarpPathRewriter().RewriteDocumentPaths(document, [route]);
        return document;
    }

    private static OpenApiDocument Rewritten() => Rewrite(("PathPattern", "/{**catch-all}"));

    private static OpenApiOperation Operation(OpenApiDocument doc, string path, HttpMethod method)
        => ((OpenApiPathItem)doc.Paths[path]).Operations![method];

    [Fact]
    public void Paths_AreRebased_OntoGatewayPrefix()
    {
        OpenApiDocument doc = Rewritten();

        doc.Paths.Keys.ShouldBe(
        [
            "/api/catalog/items",
            "/api/catalog/items/{itemId}",
            "/api/catalog/items/{itemId}/attachments",
            "/api/catalog/search",
        ], ignoreOrder: true);
    }

    [Fact]
    public void QueryPathHeaderCookie_Parameters_ArePreserved()
    {
        OpenApiDocument doc = Rewritten();

        OpenApiOperation list = Operation(doc, "/api/catalog/items", HttpMethod.Get);
        list.Parameters!.Select(p => p.Name).ShouldBe(
            ["page", "pageSize", "tags", "status", "minPrice", "includeDeleted", "X-Tenant-Id", "Accept-Language"], ignoreOrder: true);

        IOpenApiParameter tenant = list.Parameters!.Single(p => p.Name == "X-Tenant-Id");
        tenant.In.ShouldBe(ParameterLocation.Header);
        tenant.Required.ShouldBeTrue();

        IOpenApiParameter tags = list.Parameters!.Single(p => p.Name == "tags");
        tags.In.ShouldBe(ParameterLocation.Query);
        tags.Schema!.Type!.Value.HasFlag(JsonSchemaType.Array).ShouldBeTrue();

        OpenApiOperation get = Operation(doc, "/api/catalog/items/{itemId}", HttpMethod.Get);
        get.Parameters!.Single(p => p.Name == "session").In.ShouldBe(ParameterLocation.Cookie);
        get.Parameters!.Single(p => p.Name == "itemId").In.ShouldBe(ParameterLocation.Path);
    }

    [Fact]
    public void RequestBodies_JsonRef_And_Multipart_ArePreserved()
    {
        OpenApiDocument doc = Rewritten();

        OpenApiOperation create = Operation(doc, "/api/catalog/items", HttpMethod.Post);
        create.RequestBody!.Content!.ShouldContainKey("application/json");
        create.RequestBody!.Content!["application/json"].Schema.ShouldBeOfType<OpenApiSchemaReference>();
        create.Security!.ShouldNotBeEmpty();

        OpenApiOperation upload = Operation(doc, "/api/catalog/items/{itemId}/attachments", HttpMethod.Post);
        upload.RequestBody!.Content!.ShouldContainKey("multipart/form-data");
    }

    [Fact]
    public void InlineResponseSchema_StaysInline_NotPromotedToComponent()
    {
        OpenApiDocument doc = Rewritten();

        OpenApiOperation upload = Operation(doc, "/api/catalog/items/{itemId}/attachments", HttpMethod.Post);
        IOpenApiSchema inline = upload.Responses!["201"].Content!["application/json"].Schema!;

        // Inline object, NOT a $ref to a named component ("not everything in scheme").
        inline.ShouldBeOfType<OpenApiSchema>();
        inline.Properties!.Keys.ShouldBe(["id", "url"], ignoreOrder: true);
    }

    [Fact]
    public void Components_And_SecuritySchemes_ArePreserved()
    {
        OpenApiDocument doc = Rewritten();

        doc.Components!.Schemas!.Keys.ShouldBe(["Money", "Supplier", "Item", "CreateItem", "ProblemDetails"], ignoreOrder: true);
        doc.Components.SecuritySchemes!.Keys.ShouldBe(["Bearer", "ApiKey"], ignoreOrder: true);

        // Nested $ref inside a component schema survives (Item.price -> Money).
        IOpenApiSchema item = doc.Components.Schemas["Item"];
        item.Properties!["price"].ShouldBeOfType<OpenApiSchemaReference>();
    }

    [Fact]
    public async Task SerializedOutput_RetainsRefs_Formats_Enums_Dictionaries()
    {
        OpenApiDocument doc = Rewritten();
        string json = await OpenApiSerializer.SerializeAsync(doc, OpenApiSpecVersion.OpenApi3_0, CancellationToken.None);

        json.ShouldContain("#/components/schemas/Money");
        json.ShouldContain("multipart/form-data");
        json.ShouldContain("\"cookie\"");
        json.ShouldContain("uuid");
        json.ShouldContain("date-time");
        json.ShouldContain("additionalProperties");
        json.ShouldContain("\"enum\"");
    }

    [Fact]
    public void NonPathTransforms_DoNotAlterParametersOrPaths()
    {
        // A route carrying header + query transforms must not add params or change the documented surface.
        OpenApiDocument doc = Rewrite(
            ("PathPattern", "/{**catch-all}"),
            ("RequestHeader", "X-Gateway"),
            ("QueryValueParameter", "source"));

        doc.Paths.ShouldContainKey("/api/catalog/items");
        OpenApiOperation list = Operation(doc, "/api/catalog/items", HttpMethod.Get);
        list.Parameters!.Count.ShouldBe(8);
        list.Parameters!.Select(p => p.Name).ShouldNotContain("source");
        list.Parameters!.Select(p => p.Name).ShouldNotContain("X-Gateway");
    }
}
