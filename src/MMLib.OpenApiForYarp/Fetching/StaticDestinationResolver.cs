namespace MMLib.OpenApiForYarp.Fetching;

/// <summary>
/// Default resolver used when service discovery is not registered: returns the configured
/// destination address unchanged.
/// </summary>
internal sealed class StaticDestinationResolver : IServiceDestinationResolver
{
    public ValueTask<string> ResolveAsync(string address, CancellationToken cancellationToken)
        => ValueTask.FromResult(address);
}
