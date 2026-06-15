using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Fetching;

namespace MMLib.OpenApiForYarp.Tests.Fetching;

public class DestinationResolverTests
{
    [Fact]
    public async Task Static_Returns_Address_Verbatim()
    {
        var resolver = new StaticDestinationResolver();
        (await resolver.ResolveAsync("https://localhost:5101", CancellationToken.None))
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

    [Fact]
    public void ServiceDiscovery_BuildAddress_FromUriEndpoint()
        => ServiceDiscoveryDestinationResolver
            .BuildAddress("http://products-service", new Microsoft.Extensions.ServiceDiscovery.UriEndPoint(new Uri("http://localhost:5101/")))
            .ShouldBe("http://localhost:5101");
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

        OpenApiDocument? doc = await client.FetchAsync("products", "https://localhost:5101", "/openapi/v1.json", TimeSpan.FromSeconds(5), CancellationToken.None);

        doc.ShouldNotBeNull();
        doc!.Info!.Title.ShouldBe("Products API");
    }

    [Fact]
    public async Task Fetch_Returns_Null_On_HttpError()
    {
        var client = Create(StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        OpenApiDocument? doc = await client.FetchAsync("products", "https://localhost:5101", "/openapi/v1.json", TimeSpan.FromSeconds(5), CancellationToken.None);

        doc.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://localhost:5101", "/openapi/v1.json", "https://localhost:5101/openapi/v1.json")]
    [InlineData("https://localhost:5101/", "/openapi/v1.json", "https://localhost:5101/openapi/v1.json")]
    [InlineData("https://localhost:5101/app", "/openapi/v1.json", "https://localhost:5101/app/openapi/v1.json")]
    public void CombineUrl_Joins_Correctly(string baseAddress, string path, string expected)
        => DownstreamOpenApiClient.CombineUrl(baseAddress, path).ShouldBe(expected);
}
