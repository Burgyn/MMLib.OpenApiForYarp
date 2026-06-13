using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Configuration;
using MMLib.OpenApiForYarp.Yarp;

namespace MMLib.OpenApiForYarp.Fetching;

/// <summary>Provides the raw (untransformed) downstream document for a cluster.</summary>
internal interface IDownstreamOpenApiProvider
{
    Task<OpenApiDocument?> GetDocumentAsync(string clusterId, CancellationToken cancellationToken);
}

/// <summary>
/// Cache-aside <see cref="IDownstreamOpenApiProvider"/>: serves a fetched document from
/// <see cref="IMemoryCache"/> until <see cref="YarpOpenApiOptions.CacheDuration"/> elapses, then
/// re-fetches. Looks up the cluster's destination from the live YARP configuration.
/// </summary>
internal sealed class CachedDownstreamOpenApiProvider(
    IDownstreamOpenApiClient client,
    YarpConfigSource configSource,
    IMemoryCache cache,
    IOptions<YarpOpenApiOptions> options,
    ILogger<CachedDownstreamOpenApiProvider> logger) : IDownstreamOpenApiProvider
{
    private const string CacheKeyPrefix = "MMLib.OpenApiForYarp:doc:";

    private readonly IDownstreamOpenApiClient _client = client;
    private readonly YarpConfigSource _configSource = configSource;
    private readonly IMemoryCache _cache = cache;
    private readonly IOptions<YarpOpenApiOptions> _options = options;
    private readonly ILogger<CachedDownstreamOpenApiProvider> _logger = logger;

    public async Task<OpenApiDocument?> GetDocumentAsync(string clusterId, CancellationToken cancellationToken)
    {
        string cacheKey = CacheKeyPrefix + clusterId;
        if (_cache.TryGetValue(cacheKey, out OpenApiDocument? cached))
        {
            return cached;
        }

        if (!_configSource.TryGetCluster(clusterId, out YarpClusterDescriptor descriptor))
        {
            _logger.LogWarning("No YARP cluster '{ClusterId}' found; cannot build its OpenAPI document.", clusterId);
            return null;
        }

        string? address = descriptor.Cluster.Destinations?.Values.FirstOrDefault()?.Address;
        if (string.IsNullOrEmpty(address))
        {
            _logger.LogWarning("Cluster '{ClusterId}' has no destination address.", clusterId);
            return null;
        }

        YarpOpenApiOptions opts = _options.Value;
        YarpOpenApiClusterOptions clusterOptions = opts.Clusters.TryGetValue(clusterId, out YarpOpenApiClusterOptions? c)
            ? c
            : new YarpOpenApiClusterOptions();

        OpenApiDocument? document = await _client
            .FetchAsync(clusterId, address, clusterOptions.OpenApiPath, opts.FetchTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (document is not null)
        {
            _cache.Set(cacheKey, document, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = opts.CacheDuration,
            });
        }

        return document;
    }
}
