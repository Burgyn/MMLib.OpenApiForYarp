using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ServiceDiscovery;
using MMLib.OpenApiForYarp.Fetching;

namespace MMLib.OpenApiForYarp.IntegrationTests;

/// <summary>
/// Exercises the REAL <see cref="ServiceDiscoveryDestinationResolver"/> against a real
/// configuration-backed <see cref="ServiceEndpointResolver"/> (as .NET Aspire wires it), to ensure
/// a logical name resolves to a concrete address before the OpenAPI document is fetched.
/// </summary>
public class ServiceDiscoveryResolutionTests
{
    private static ServiceDiscoveryDestinationResolver CreateResolver(Dictionary<string, string?> services)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(services).Build();
        var collection = new ServiceCollection();
        collection.AddSingleton<IConfiguration>(configuration);
        collection.AddServiceDiscovery();
        var provider = collection.BuildServiceProvider();

        return new ServiceDiscoveryDestinationResolver(
            provider.GetRequiredService<ServiceEndpointResolver>(),
            NullLogger<ServiceDiscoveryDestinationResolver>.Instance);
    }

    [Fact]
    public async Task Resolves_Logical_Http_Name_To_Concrete_Address()
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            ["Services:products-service:http:0"] = "http://localhost:5101",
        });

        string resolved = await resolver.ResolveAsync("http://products-service", CancellationToken.None);

        resolved.ShouldBe("http://localhost:5101");
    }
}
