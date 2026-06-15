using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Abstractions;
using MMLib.OpenApiForYarp.Fetching;

namespace MMLib.OpenApiForYarp.IntegrationTests;

internal sealed class AddPathTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Paths["/custom-added"] = new OpenApiPathItem();
        return Task.CompletedTask;
    }
}

internal sealed class RecordingDestinationResolver(IReadOnlyDictionary<string, string> map) : IServiceDestinationResolver
{
    public List<string> Resolved { get; } = [];

    public ValueTask<string> ResolveAsync(string address, CancellationToken cancellationToken)
    {
        Resolved.Add(address);
        return ValueTask.FromResult(map.TryGetValue(address, out string? v) ? v : address);
    }
}

public class IntegrationScenariosTests
{
    private static async Task<OpenApiDocument> GetDocumentAsync(HttpClient client, string path, CancellationToken ct)
    {
        HttpResponseMessage response = await client.GetAsync(path, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string json = await response.Content.ReadAsStringAsync(ct);
        OpenApiDocument? document = OpenApiDocument.Parse(json, "json").Document;
        document.ShouldNotBeNull();
        return document!;
    }

    [Fact]
    public async Task EndToEnd_SingleCluster_TransformsPaths()
    {
        await using var host = await GatewayTestHost.StartAsync(Fixtures.GatewayConfig(), Fixtures.BothServices());

        OpenApiDocument doc = await GetDocumentAsync(host.Client, "/openapi/products-cluster.json", CancellationToken.None);

        doc.Paths.ShouldContainKey("/api/products");
        doc.Paths.ShouldContainKey("/api/products/{id}");
        doc.Paths.ShouldNotContainKey("/products/{id}");
    }

    [Fact]
    public async Task EndToEnd_TwoServices_CorrectPathsInOutput()
    {
        await using var host = await GatewayTestHost.StartAsync(Fixtures.GatewayConfig(), Fixtures.BothServices());

        OpenApiDocument products = await GetDocumentAsync(host.Client, "/openapi/products-cluster.json", CancellationToken.None);
        OpenApiDocument orders = await GetDocumentAsync(host.Client, "/openapi/orders-cluster.json", CancellationToken.None);

        products.Paths.ShouldContainKey("/api/products/{id}");
        orders.Paths.ShouldContainKey("/api/orders/{id}");
    }

    [Fact]
    public async Task EndToEnd_MergedDocument_AllPathsPresent()
    {
        await using var host = await GatewayTestHost.StartAsync(Fixtures.GatewayConfig(mergeDocuments: true), Fixtures.BothServices());

        OpenApiDocument merged = await GetDocumentAsync(host.Client, "/openapi/all.json", CancellationToken.None);

        merged.Info!.Title.ShouldBe("Gateway API");
        merged.Paths.ShouldContainKey("/api/products/{id}");
        merged.Paths.ShouldContainKey("/api/orders/{id}");
    }

    [Fact]
    public async Task EndToEnd_AddOnlyPublishedPaths_FiltersCorrectly()
    {
        await using var host = await GatewayTestHost.StartAsync(Fixtures.GatewayConfig(ordersAddOnlyPublished: true), Fixtures.BothServices());

        OpenApiDocument orders = await GetDocumentAsync(host.Client, "/openapi/orders-cluster.json", CancellationToken.None);

        orders.Paths.ShouldContainKey("/api/orders");
        orders.Paths.ShouldContainKey("/api/orders/{id}");
        orders.Paths.ShouldNotContainKey("/internal/health");
    }

    [Fact]
    public async Task EndToEnd_PropagatesSecuritySchemes()
    {
        await using var host = await GatewayTestHost.StartAsync(Fixtures.GatewayConfig(), Fixtures.BothServices());

        OpenApiDocument doc = await GetDocumentAsync(host.Client, "/openapi/products-cluster.json", CancellationToken.None);

        doc.Components!.SecuritySchemes!.ShouldContainKey("Bearer");
    }

    [Fact]
    public async Task EndToEnd_ScalarUi_Loads()
    {
        await using var host = await GatewayTestHost.StartAsync(Fixtures.GatewayConfig(), Fixtures.BothServices());

        var ct = CancellationToken.None;
        HttpResponseMessage response = await host.Client.GetAsync("/scalar", ct);

        // Scalar redirects "/scalar" -> "/scalar/"; the TestServer client does not auto-follow.
        if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.MovedPermanently)
        {
            response = await host.Client.GetAsync(response.Headers.Location, ct);
        }

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
    }

    [Fact]
    public async Task EndToEnd_CustomTransformer_AppliedToOutput()
    {
        await using var host = await GatewayTestHost.StartAsync(
            Fixtures.GatewayConfig(),
            Fixtures.BothServices(),
            configureOpenApi: b => b.AddDocumentTransformer<AddPathTransformer>());

        OpenApiDocument doc = await GetDocumentAsync(host.Client, "/openapi/products-cluster.json", CancellationToken.None);

        doc.Paths.ShouldContainKey("/custom-added");
    }

    [Fact]
    public async Task EndToEnd_ServiceDiscovery_ResolvesAddress()
    {
        var resolver = new RecordingDestinationResolver(new Dictionary<string, string>
        {
            ["https://products-service"] = "http://localhost:5101",
        });

        await using var host = await GatewayTestHost.StartAsync(
            Fixtures.GatewayConfig(productsAddress: "https://products-service"),
            Fixtures.BothServices(),
            configureServices: services => services.AddSingleton<IServiceDestinationResolver>(resolver));

        OpenApiDocument doc = await GetDocumentAsync(host.Client, "/openapi/products-cluster.json", CancellationToken.None);

        doc.Paths.ShouldContainKey("/api/products/{id}");
        resolver.Resolved.ShouldContain("https://products-service");
    }

    [Fact]
    public async Task EndToEnd_CacheExpiry_RefetchesAfterTimeout()
    {
        var handler = Fixtures.BothServices();
        await using var host = await GatewayTestHost.StartAsync(
            Fixtures.GatewayConfig(cacheDuration: "00:00:00.200"),
            handler);
        var ct = CancellationToken.None;

        await GetDocumentAsync(host.Client, "/openapi/products-cluster.json", ct);
        await GetDocumentAsync(host.Client, "/openapi/products-cluster.json", ct);
        int afterCached = handler.CallCount;

        await Task.Delay(450, ct);
        await GetDocumentAsync(host.Client, "/openapi/products-cluster.json", ct);

        afterCached.ShouldBe(1);
        handler.CallCount.ShouldBe(2);
    }
}
