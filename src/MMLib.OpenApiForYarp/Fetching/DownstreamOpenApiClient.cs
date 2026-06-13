using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using MMLib.OpenApiForYarp.OpenApi;

namespace MMLib.OpenApiForYarp.Fetching;

/// <summary>Fetches and parses a downstream service's OpenAPI document over HTTP.</summary>
internal interface IDownstreamOpenApiClient
{
    Task<OpenApiDocument?> FetchAsync(string clusterId, string address, string openApiPath, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Default <see cref="IDownstreamOpenApiClient"/>: resolves the destination address, fetches the
/// document via a named <see cref="IHttpClientFactory"/> client with a per-request timeout, and
/// parses it. Fetch/parse failures are logged and surfaced as <see langword="null"/> rather than
/// thrown, so one unavailable service does not break the whole gateway document.
/// </summary>
internal sealed class DownstreamOpenApiClient(
    IHttpClientFactory httpClientFactory,
    IServiceDestinationResolver destinationResolver,
    ILogger<DownstreamOpenApiClient> logger) : IDownstreamOpenApiClient
{
    /// <summary>The name of the <see cref="IHttpClientFactory"/> client used for downstream fetches.</summary>
    public const string HttpClientName = "MMLib.OpenApiForYarp";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IServiceDestinationResolver _destinationResolver = destinationResolver;
    private readonly ILogger<DownstreamOpenApiClient> _logger = logger;

    public async Task<OpenApiDocument?> FetchAsync(string clusterId, string address, string openApiPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string resolved = await _destinationResolver.ResolveAsync(address, cancellationToken).ConfigureAwait(false);
        string url = CombineUrl(resolved, openApiPath);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
            string json = await client.GetStringAsync(url, timeoutCts.Token).ConfigureAwait(false);

            (OpenApiDocument? document, OpenApiDiagnostic? diagnostic) = OpenApiSerializer.Parse(json);
            if (document is null)
            {
                _logger.LogWarning("Downstream OpenAPI for cluster {ClusterId} at {Url} could not be parsed.", clusterId, url);
                return null;
            }

            if (diagnostic is { Errors.Count: > 0 })
            {
                _logger.LogWarning("Downstream OpenAPI for cluster {ClusterId} at {Url} parsed with {ErrorCount} error(s).", clusterId, url, diagnostic.Errors.Count);
            }

            return document;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Fetching downstream OpenAPI for cluster {ClusterId} at {Url} timed out after {Timeout}.", clusterId, url, timeout);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch downstream OpenAPI for cluster {ClusterId} at {Url}.", clusterId, url);
            return null;
        }
    }

    internal static string CombineUrl(string baseAddress, string openApiPath)
    {
        string left = baseAddress.TrimEnd('/');
        string right = openApiPath.StartsWith('/') ? openApiPath : "/" + openApiPath;
        return left + right;
    }
}
