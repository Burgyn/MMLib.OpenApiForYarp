using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Abstractions;
using MMLib.OpenApiForYarp.Configuration;
using MMLib.OpenApiForYarp.Fetching;
using MMLib.OpenApiForYarp.Pipeline;
using MMLib.OpenApiForYarp.Yarp;

namespace MMLib.OpenApiForYarp.Aggregation;

/// <summary>Builds the transformed, gateway-facing OpenAPI document for a single cluster.</summary>
internal interface IClusterDocumentBuilder
{
    /// <summary>The ids of all clusters that can produce a document.</summary>
    IReadOnlyList<string> GetClusterIds();

    /// <summary>
    /// Returns the transformed document for a cluster, or <see langword="null"/> if the cluster is
    /// unknown or its document could not be fetched. Results are cached for the configured duration.
    /// </summary>
    Task<OpenApiDocument?> BuildAsync(string clusterId, CancellationToken cancellationToken);
}

/// <summary>
/// Fetches a cluster's downstream document, runs it through the transformer pipeline, applies the
/// configured title, and caches the <em>transformed</em> result. Caching the transformed document
/// (rather than the raw one) avoids re-running the in-place pipeline on subsequent requests.
/// </summary>
internal sealed class ClusterDocumentBuilder(
    IDownstreamOpenApiClient client,
    YarpConfigSource configSource,
    OpenApiTransformerPipeline pipeline,
    IMemoryCache cache,
    IOptions<YarpOpenApiOptions> options,
    IServiceProvider services,
    ILogger<ClusterDocumentBuilder> logger) : IClusterDocumentBuilder
{
    private const string CacheKeyPrefix = "MMLib.OpenApiForYarp:cluster:";

    private readonly IDownstreamOpenApiClient _client = client;
    private readonly YarpConfigSource _configSource = configSource;
    private readonly OpenApiTransformerPipeline _pipeline = pipeline;
    private readonly IMemoryCache _cache = cache;
    private readonly IOptions<YarpOpenApiOptions> _options = options;
    private readonly IServiceProvider _services = services;
    private readonly ILogger<ClusterDocumentBuilder> _logger = logger;

    public IReadOnlyList<string> GetClusterIds() => _configSource.GetClusterIds();

    public async Task<OpenApiDocument?> BuildAsync(string clusterId, CancellationToken cancellationToken)
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
        if (document is null)
        {
            return null;
        }

        var context = new OpenApiDocumentTransformerContext
        {
            ClusterName = clusterId,
            Routes = descriptor.Routes,
            Cluster = descriptor.Cluster,
            Options = clusterOptions,
            Document = document,
            Services = _services,
        };

        await _pipeline.RunAsync(document, context, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(clusterOptions.Title))
        {
            document.Info ??= new OpenApiInfo { Version = "1.0.0" };
            document.Info.Title = clusterOptions.Title;
        }

        _cache.Set(cacheKey, document, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = opts.CacheDuration,
        });

        return document;
    }
}
