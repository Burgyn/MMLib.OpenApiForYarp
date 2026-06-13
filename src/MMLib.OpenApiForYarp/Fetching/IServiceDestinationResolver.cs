namespace MMLib.OpenApiForYarp.Fetching;

/// <summary>
/// Resolves a YARP destination address into a concrete base address that can be used to fetch the
/// downstream OpenAPI document. For static configuration this is the address verbatim; with
/// <c>Microsoft.Extensions.ServiceDiscovery</c> a logical name (e.g. <c>https://products-service</c>)
/// is resolved to a real endpoint first.
/// </summary>
internal interface IServiceDestinationResolver
{
    /// <summary>Resolves <paramref name="address"/> to a concrete base address.</summary>
    ValueTask<string> ResolveAsync(string address, CancellationToken cancellationToken);
}
