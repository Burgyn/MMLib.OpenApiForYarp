using System.Net.Http;
using Microsoft.OpenApi;

namespace MMLib.OpenApiForYarp.IntegrationTests;

/// <summary>
/// End-to-end proof (real gateway + fetch + transform + serialize) that a feature-rich downstream
/// document survives aggregation: every parameter kind, request bodies (incl. multipart), inline and
/// $ref schemas, components and security are preserved while only path keys are rewritten — even when
/// the route also carries header/query (non-path) YARP transforms.
/// </summary>
public class RichDocumentEndToEndTests
{
    private const string CatalogAuthority = "localhost:5201";

    private static string RichSpec()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rich-api.openapi.json"));

    private const string Config = """
        {
          "ReverseProxy": {
            "Routes": {
              "catalog-route": {
                "ClusterId": "catalog-cluster",
                "Match": { "Path": "/api/catalog/{**catch-all}" },
                "Transforms": [
                  { "PathPattern": "/{**catch-all}" },
                  { "RequestHeader": "X-Gateway", "Set": "yarp" },
                  { "QueryValueParameter": "source", "Append": "gateway" }
                ]
              }
            },
            "Clusters": {
              "catalog-cluster": { "Destinations": { "default": { "Address": "http://localhost:5201" } } }
            }
          },
          "YarpOpenApi": {
            "Clusters": { "catalog-cluster": { "Title": "Catalog API", "OpenApiPath": "/openapi/v1.json" } }
          }
        }
        """;

    private static async Task<OpenApiDocument> GetDocumentAsync(HttpClient client, CancellationToken ct)
    {
        HttpResponseMessage response = await client.GetAsync("/openapi/catalog-cluster.json", ct);
        response.EnsureSuccessStatusCode();
        OpenApiDocument? doc = OpenApiDocument.Parse(await response.Content.ReadAsStringAsync(ct), "json").Document;
        doc.ShouldNotBeNull();
        return doc!;
    }

    private static OpenApiOperation Op(OpenApiDocument doc, string path, HttpMethod method)
        => ((OpenApiPathItem)doc.Paths[path]).Operations![method];

    [Fact]
    public async Task RichDocument_PreservedEndToEnd_ThroughGateway()
    {
        var handler = new RoutingStubHandler(new Dictionary<string, string> { [CatalogAuthority] = RichSpec() });
        await using var host = await GatewayTestHost.StartAsync(Config, handler);
        var ct = CancellationToken.None;

        OpenApiDocument doc = await GetDocumentAsync(host.Client, ct);

        // Paths rebased onto the gateway prefix.
        doc.Paths.Keys.ShouldBe(
        [
            "/api/catalog/items",
            "/api/catalog/items/{itemId}",
            "/api/catalog/items/{itemId}/attachments",
            "/api/catalog/search",
        ], ignoreOrder: true);

        // All parameter kinds preserved (and non-path transforms did NOT inject params).
        OpenApiOperation list = Op(doc, "/api/catalog/items", HttpMethod.Get);
        list.Parameters!.Count.ShouldBe(8);
        list.Parameters!.Single(p => p.Name == "X-Tenant-Id").In.ShouldBe(ParameterLocation.Header);
        list.Parameters!.Select(p => p.Name).ShouldNotContain("source");

        Op(doc, "/api/catalog/items/{itemId}", HttpMethod.Get)
            .Parameters!.Single(p => p.Name == "session").In.ShouldBe(ParameterLocation.Cookie);

        // Bodies: JSON $ref + multipart.
        Op(doc, "/api/catalog/items", HttpMethod.Post)
            .RequestBody!.Content!["application/json"].Schema.ShouldBeOfType<OpenApiSchemaReference>();
        Op(doc, "/api/catalog/items/{itemId}/attachments", HttpMethod.Post)
            .RequestBody!.Content!.ShouldContainKey("multipart/form-data");

        // Inline response schema stays inline.
        Op(doc, "/api/catalog/items/{itemId}/attachments", HttpMethod.Post)
            .Responses!["201"].Content!["application/json"].Schema.ShouldBeOfType<OpenApiSchema>();

        // Components + security preserved.
        doc.Components!.Schemas!.Keys.ShouldBe(["Money", "Supplier", "Item", "CreateItem", "ProblemDetails"], ignoreOrder: true);
        doc.Components.SecuritySchemes!.Keys.ShouldBe(["Bearer", "ApiKey"], ignoreOrder: true);
    }
}
