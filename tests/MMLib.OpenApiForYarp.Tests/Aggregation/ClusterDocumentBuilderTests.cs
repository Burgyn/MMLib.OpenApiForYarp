using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Aggregation;
using MMLib.OpenApiForYarp.Configuration;
using MMLib.OpenApiForYarp.Fetching;
using MMLib.OpenApiForYarp.Pipeline;
using MMLib.OpenApiForYarp.Transformers;
using MMLib.OpenApiForYarp.Yarp;

namespace MMLib.OpenApiForYarp.Tests.Aggregation;

public class ClusterDocumentBuilderTests
{
    private sealed class FakeDownstreamClient(Func<OpenApiDocument?> factory) : IDownstreamOpenApiClient
    {
        public int Calls { get; private set; }

        public Task<OpenApiDocument?> FetchAsync(string clusterId, string address, string openApiPath, TimeSpan timeout, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(factory());
        }
    }

    private static ClusterDocumentBuilder Create(
        IDownstreamOpenApiClient client,
        IEnumerable<Type>? documentTransformers = null,
        YarpOpenApiClusterOptions? clusterOptions = null)
    {
        var configSource = new YarpConfigSource(
        [
            FakeYarp.Provider(
                routes: [FakeYarp.Route("r", "products-cluster", "/api/products/{**catch-all}", ("PathPattern", "/products/{**catch-all}"))],
                clusters: [FakeYarp.Cluster("products-cluster", "https://localhost:5101")]),
        ]);

        var services = new ServiceCollection();
        services.AddTransient<PathRewriteTransformer>();
        var provider = services.BuildServiceProvider();

        var registry = new OpenApiTransformerRegistry();
        foreach (Type type in documentTransformers ?? [])
        {
            registry.AddDocumentTransformer(type);
        }

        var options = Options.Create(new YarpOpenApiOptions
        {
            CacheDuration = TimeSpan.FromMinutes(5),
            Clusters = { ["products-cluster"] = clusterOptions ?? new YarpOpenApiClusterOptions() },
        });

        return new ClusterDocumentBuilder(
            client,
            configSource,
            new OpenApiTransformerPipeline(registry),
            new MemoryCache(new MemoryCacheOptions()),
            options,
            provider,
            NullLogger<ClusterDocumentBuilder>.Instance);
    }

    [Fact]
    public async Task Caches_Transformed_Document_Between_Calls()
    {
        var client = new FakeDownstreamClient(() => TestDocuments.WithPaths("/products"));
        var builder = Create(client);

        await builder.BuildAsync("products-cluster", TestContext.Current.CancellationToken);
        await builder.BuildAsync("products-cluster", TestContext.Current.CancellationToken);

        client.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Applies_PathRewrite_Transformer()
    {
        var client = new FakeDownstreamClient(() => TestDocuments.WithPaths("/products/{id}"));
        var builder = Create(client, documentTransformers: [typeof(PathRewriteTransformer)]);

        OpenApiDocument? doc = await builder.BuildAsync("products-cluster", TestContext.Current.CancellationToken);

        doc.ShouldNotBeNull();
        doc!.Paths.ShouldContainKey("/api/products/{id}");
    }

    [Fact]
    public async Task Applies_Title_Override()
    {
        var client = new FakeDownstreamClient(() => TestDocuments.WithPaths("/products"));
        var builder = Create(client, clusterOptions: new YarpOpenApiClusterOptions { Title = "Products API" });

        OpenApiDocument? doc = await builder.BuildAsync("products-cluster", TestContext.Current.CancellationToken);

        doc!.Info!.Title.ShouldBe("Products API");
    }

    [Fact]
    public async Task Returns_Null_For_Unknown_Cluster()
    {
        var builder = Create(new FakeDownstreamClient(() => TestDocuments.WithPaths("/x")));

        (await builder.BuildAsync("missing", TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}
