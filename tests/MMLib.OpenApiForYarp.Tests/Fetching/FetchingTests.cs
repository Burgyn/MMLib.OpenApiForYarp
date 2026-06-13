using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Configuration;
using MMLib.OpenApiForYarp.Fetching;
using MMLib.OpenApiForYarp.Yarp;

namespace MMLib.OpenApiForYarp.Tests.Fetching;

public class DestinationResolverTests
{
    [Fact]
    public async Task Static_Returns_Address_Verbatim()
    {
        var resolver = new StaticDestinationResolver();
        (await resolver.ResolveAsync("https://localhost:5101", TestContext.Current.CancellationToken))
            .ShouldBe("https://localhost:5101");
    }

    [Fact]
    public void ServiceDiscovery_BuildAddress_FromDnsEndpoint()
        => ServiceDiscoveryDestinationResolver
            .BuildAddress("https://products-service", new DnsEndPoint("10.0.0.5", 8443))
            .ShouldBe("https://10.0.0.5:8443");

    [Fact]
    public void ServiceDiscovery_BuildAddress_FromIpEndpoint()
        => ServiceDiscoveryDestinationResolver
            .BuildAddress("http://orders-service", new IPEndPoint(IPAddress.Loopback, 5102))
            .ShouldBe("http://127.0.0.1:5102");
}

public class DownstreamOpenApiClientTests
{
    private const string Json = """
        { "openapi": "3.0.1", "info": { "title": "Products API", "version": "1.0.0" }, "paths": {} }
        """;

    private static DownstreamOpenApiClient Create(StubHttpMessageHandler handler)
        => new(new StubHttpClientFactory(handler), new StaticDestinationResolver(), NullLogger<DownstreamOpenApiClient>.Instance);

    [Fact]
    public async Task Fetch_Parses_Document()
    {
        var client = Create(StubHttpMessageHandler.Json(Json));

        OpenApiDocument? doc = await client.FetchAsync("products", "https://localhost:5101", "/openapi/v1.json", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        doc.ShouldNotBeNull();
        doc!.Info!.Title.ShouldBe("Products API");
    }

    [Fact]
    public async Task Fetch_Returns_Null_On_HttpError()
    {
        var client = Create(StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        OpenApiDocument? doc = await client.FetchAsync("products", "https://localhost:5101", "/openapi/v1.json", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        doc.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://localhost:5101", "/openapi/v1.json", "https://localhost:5101/openapi/v1.json")]
    [InlineData("https://localhost:5101/", "/openapi/v1.json", "https://localhost:5101/openapi/v1.json")]
    [InlineData("https://localhost:5101/app", "/openapi/v1.json", "https://localhost:5101/app/openapi/v1.json")]
    public void CombineUrl_Joins_Correctly(string baseAddress, string path, string expected)
        => DownstreamOpenApiClient.CombineUrl(baseAddress, path).ShouldBe(expected);
}

public class CachedDownstreamOpenApiProviderTests
{
    private sealed class CountingClient(OpenApiDocument? document) : IDownstreamOpenApiClient
    {
        public int Calls { get; private set; }

        public Task<OpenApiDocument?> FetchAsync(string clusterId, string address, string openApiPath, TimeSpan timeout, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(document);
        }
    }

    private static (CachedDownstreamOpenApiProvider Provider, CountingClient Client) Create(TimeSpan cacheDuration)
    {
        var configSource = new YarpConfigSource(
        [
            FakeYarp.Provider(
                routes: [FakeYarp.Route("r", "c", "/api/{**catch-all}")],
                clusters: [FakeYarp.Cluster("c", "https://localhost:5101")]),
        ]);
        var client = new CountingClient(TestDocuments.WithPaths("/products"));
        var options = Options.Create(new YarpOpenApiOptions
        {
            CacheDuration = cacheDuration,
            Clusters = { ["c"] = new YarpOpenApiClusterOptions() },
        });
        var provider = new CachedDownstreamOpenApiProvider(
            client, configSource, new MemoryCache(new MemoryCacheOptions()), options,
            NullLogger<CachedDownstreamOpenApiProvider>.Instance);
        return (provider, client);
    }

    [Fact]
    public async Task Caches_Document_Between_Calls()
    {
        var (provider, client) = Create(TimeSpan.FromMinutes(5));

        await provider.GetDocumentAsync("c", TestContext.Current.CancellationToken);
        await provider.GetDocumentAsync("c", TestContext.Current.CancellationToken);

        client.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Returns_Null_For_Unknown_Cluster()
    {
        var (provider, _) = Create(TimeSpan.FromMinutes(5));

        (await provider.GetDocumentAsync("missing", TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}
