using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;

namespace MMLib.OpenApiForYarp.Fetching;

/// <summary>
/// Resolver used when <c>Microsoft.Extensions.ServiceDiscovery</c> is registered. Resolves a
/// logical address (e.g. <c>https://products-service</c>) to a concrete endpoint using the
/// sealed <see cref="ServiceEndpointResolver"/> before the OpenAPI document is fetched.
/// </summary>
internal sealed class ServiceDiscoveryDestinationResolver(
    ServiceEndpointResolver resolver,
    ILogger<ServiceDiscoveryDestinationResolver> logger) : IServiceDestinationResolver
{
    private readonly ServiceEndpointResolver _resolver = resolver;
    private readonly ILogger<ServiceDiscoveryDestinationResolver> _logger = logger;

    public async ValueTask<string> ResolveAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            ServiceEndpointSource source = await _resolver.GetEndpointsAsync(address, cancellationToken).ConfigureAwait(false);
            ServiceEndpoint? endpoint = source.Endpoints.Count > 0 ? source.Endpoints[0] : null;
            if (endpoint is null)
            {
                return address;
            }

            return BuildAddress(address, endpoint.EndPoint) ?? address;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Service discovery could not resolve {Address}; using it verbatim.", address);
            return address;
        }
    }

    /// <summary>Combines the original address's scheme with a resolved endpoint into a base URL.</summary>
    internal static string? BuildAddress(string originalAddress, EndPoint endPoint)
    {
        string scheme = Uri.TryCreate(originalAddress, UriKind.Absolute, out Uri? uri) ? uri.Scheme : Uri.UriSchemeHttps;

        return endPoint switch
        {
            DnsEndPoint dns => $"{scheme}://{dns.Host}:{dns.Port}",
            IPEndPoint ip => $"{scheme}://{ip.Address}:{ip.Port}",
            _ => null,
        };
    }
}
