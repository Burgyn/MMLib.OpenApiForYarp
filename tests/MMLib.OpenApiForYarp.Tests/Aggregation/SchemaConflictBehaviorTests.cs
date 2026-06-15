using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Aggregation;
using MMLib.OpenApiForYarp.Configuration;
using MMLib.OpenApiForYarp.OpenApi;

namespace MMLib.OpenApiForYarp.Tests.Aggregation;

/// <summary>
/// Documents what happens when two services define the SAME schema name with DIFFERENT shapes.
/// The merger is name-keyed: it keeps the first occurrence and warns; the second service's
/// definition is dropped from the merged document (its refs then resolve to the first one).
/// Per-service documents are unaffected.
/// </summary>
public class SchemaConflictBehaviorTests
{
    private static OpenApiDocument Parse(string json) => OpenApiSerializer.Parse(json).Document!;

    private const string ProductsWithMoneyAmount = """
        {
          "openapi": "3.0.1", "info": { "title": "Products", "version": "1.0.0" },
          "paths": { "/api/products/{id}/price": { "get": { "responses": { "200": { "description": "OK",
            "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Money" } } } } } } } },
          "components": { "schemas": { "Money": { "type": "object", "properties": {
            "amount": { "type": "number", "format": "double" }, "currency": { "type": "string" } } } } }
        }
        """;

    // Same name "Money", DIFFERENT shape (value/iso/symbol instead of amount/currency).
    private const string OrdersWithMoneyValue = """
        {
          "openapi": "3.0.1", "info": { "title": "Orders", "version": "1.0.0" },
          "paths": { "/api/orders/{id}/total": { "get": { "responses": { "200": { "description": "OK",
            "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Money" } } } } } } } },
          "components": { "schemas": { "Money": { "type": "object", "properties": {
            "value": { "type": "number" }, "iso": { "type": "string" }, "symbol": { "type": "string" } } } } }
        }
        """;

    [Fact]
    public void DifferentShapes_SameName_KeepsFirst_DropsSecond_AndWarns()
    {
        var logger = new RecordingLogger<OpenApiDocumentMerger>();

        OpenApiDocument merged = new OpenApiDocumentMerger(logger).Merge(
        [
            ("products-cluster", Parse(ProductsWithMoneyAmount)),
            ("orders-cluster", Parse(OrdersWithMoneyValue)),
        ], new MergedDocumentOptions { Title = "Gateway", Version = "1.0.0" });

        // Exactly one "Money" survives — the FIRST service's shape (amount/currency).
        OpenApiSchema money = (OpenApiSchema)merged.Components!.Schemas!["Money"];
        money.Properties!.Keys.ShouldBe(["amount", "currency"], ignoreOrder: true);
        money.Properties.Keys.ShouldNotContain("value");

        // The collision is reported.
        logger.Entries.ShouldContain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("Money"));

        // Both services' paths are still present and both still $ref "Money" — in the merged doc they
        // now resolve to the first service's schema (the documented limitation of name-keyed merging).
        merged.Paths.ShouldContainKey("/api/products/{id}/price");
        merged.Paths.ShouldContainKey("/api/orders/{id}/total");
        var ordersOp = ((OpenApiPathItem)merged.Paths["/api/orders/{id}/total"]).Operations![System.Net.Http.HttpMethod.Get];
        ordersOp.Responses!["200"].Content!["application/json"].Schema.ShouldBeOfType<OpenApiSchemaReference>();
    }
}
