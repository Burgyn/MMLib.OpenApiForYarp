# MMLib.OpenApiForYarp

**Aggregate the OpenAPI documentation of your downstream microservices onto a [YARP](https://microsoft.github.io/reverse-proxy/) gateway — and serve it through [Scalar](https://scalar.com/) or Swagger UI.**

MMLib.OpenApiForYarp is the spiritual successor to [**MMLib.SwaggerForOcelot**](https://github.com/Burgyn/MMLib.SwaggerForOcelot) (3.8M+ NuGet downloads), rebuilt for the modern .NET stack: **YARP** instead of Ocelot, **Scalar** as the default UI, and **`Microsoft.OpenApi`** as the engine (never Swashbuckle in the core). It fetches each downstream service's OpenAPI document at runtime, rewrites its paths to match how the gateway exposes them, and serves a clean per-service (or merged) document — with a fully extensible transformation pipeline.

- ✅ Path rewriting driven by your existing **YARP routes & transforms** — no parallel config
- ✅ One transformed document per cluster at `/openapi/{cluster}.json` (+ optional merged `/openapi/all.json`)
- ✅ **Scalar** (default) and **Swagger UI** adapters — the core is UI-agnostic
- ✅ Security-scheme propagation, published-paths filtering, regex include/exclude
- ✅ First-class, reorderable **transformer pipeline** (built-ins are themselves swappable transformers)
- ✅ **Service discovery** (`Microsoft.Extensions.ServiceDiscovery` / .NET Aspire) support
- ✅ `net8.0` and `net10.0`

---

## Installation

```bash
# Core + Scalar UI (recommended)
dotnet add package MMLib.OpenApiForYarp
dotnet add package MMLib.OpenApiForYarp.Scalar

# Core + Swagger UI (opt-in alternative)
dotnet add package MMLib.OpenApiForYarp
dotnet add package MMLib.OpenApiForYarp.SwaggerUI
```

## Quick start

**`Program.cs`**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddOpenApiForYarp();

var app = builder.Build();

app.MapReverseProxy();
app.MapOpenApiForYarp();   // /openapi/{cluster}.json  (+ /openapi/all.json when merging)
app.MapScalarForYarp();    // Scalar UI at /scalar

app.Run();
```

**`appsettings.json`**

```jsonc
{
  "ReverseProxy": {
    "Routes": {
      "products-route": {
        "ClusterId": "products-cluster",
        "Match": { "Path": "/api/products/{**catch-all}" },
        "Transforms": [ { "PathPattern": "/products/{**catch-all}" } ]
      },
      "orders-route": {
        "ClusterId": "orders-cluster",
        "Match": { "Path": "/api/orders/{**catch-all}" },
        "Transforms": [ { "PathPattern": "/orders/{**catch-all}" } ]
      }
    },
    "Clusters": {
      "products-cluster": { "Destinations": { "default": { "Address": "https://localhost:5101" } } },
      "orders-cluster":   { "Destinations": { "default": { "Address": "https://localhost:5102" } } }
    }
  },
  "YarpOpenApi": {
    "MergeDocuments": false,
    "Clusters": {
      "products-cluster": { "Title": "Products API", "OpenApiPath": "/openapi/v1.json" },
      "orders-cluster":   { "Title": "Orders API",   "OpenApiPath": "/openapi/v1.json", "AddOnlyPublishedPaths": true }
    }
  }
}
```

`AddOpenApiForYarp()` binds the `YarpOpenApi` section above — no extra wiring needed (you can also configure it [in code](#configuration)). Browse to **`/scalar`** — each downstream service appears as its own tab, with paths shown exactly as a client calls them through the gateway.

## How path rewriting works

The library reads each cluster's YARP route(s) and inverts the path transforms to compute the **gateway-facing** path for every downstream path.

| Downstream path (service) | YARP route | Aggregated path (gateway) |
|---|---|---|
| `GET /products/{id}` | Match `/api/products/{**catch-all}`, `PathPattern: /products/{**catch-all}` | `GET /api/products/{id}` |
| `GET /orders/{id}`   | Match `/api/orders/{**catch-all}`, `PathRemovePrefix: /api`* | `GET /api/orders/{id}` |

Supported path transforms: `PathPattern`, `PathPrefix`, `PathRemovePrefix`, `PathSet`, and no-transform passthrough. Non-path transforms (headers, query, etc.) never affect the documented paths. Path parameters and catch-all remainders are preserved verbatim.

> \* Unlike `yarp-swagger`, the common YARP path transforms are applied **out of the box** — you don't need to hand-write a transform factory for each route.

## Configuration

There are two ways to configure the library (you can mix them):

**1. Configuration section.** `AddOpenApiForYarp()` automatically binds the **`YarpOpenApi`** section
(from `appsettings.json`, environment variables, or any configuration source) — it sits alongside
YARP's own `ReverseProxy` section, as shown in the [quick start](#quick-start). This is the
recommended approach.

**2. In code.** Pass a delegate to configure (or override the bound values) programmatically — no
`YarpOpenApi` section required:

```csharp
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddOpenApiForYarp(options =>
    {
        options.MergeDocuments = true;
        options.CacheDuration = TimeSpan.FromSeconds(30);
        options.MergedDocument.Title = "My Gateway";
        options.MergedDocument.RenameDuplicateSchemas = true;

        options.Clusters["products-cluster"] = new YarpOpenApiClusterOptions
        {
            Title = "Products API",
            OpenApiPath = "/openapi/v1.json",
        };
        options.Clusters["orders-cluster"] = new YarpOpenApiClusterOptions
        {
            Title = "Orders API",
            AddOnlyPublishedPaths = true,
            ExcludePaths = ["^/api/orders/internal"],
        };
    });
```

The delegate runs after the `YarpOpenApi` section is bound, so it overrides anything from configuration.

## Configuration reference

The `YarpOpenApi` section (and the `YarpOpenApiOptions` object passed to the code delegate) supports:

### Root options

| Option | Type | Default | Description |
|---|---|---|---|
| `Clusters` | `dictionary` | `{}` | Per-cluster options, keyed by YARP cluster id. |
| `MergeDocuments` | `bool` | `false` | Serve a merged document combining every cluster. |
| `MergedDocument` | `object` | see below | `info` for the merged document (gateway-owned). |
| `CacheDuration` | `TimeSpan` | `00:01:00` | How long a fetched downstream document is cached. |
| `FetchTimeout` | `TimeSpan` | `00:00:30` | Timeout when fetching a downstream document. |
| `DocumentRoutePattern` | `string` | `/openapi/{cluster}.json` | Route template for per-cluster documents. |

### Per-cluster options (`YarpOpenApi:Clusters:<clusterId>`)

| Option | Type | Default | Description |
|---|---|---|---|
| `Title` | `string?` | downstream / cluster id | Title shown in the UI for this service. |
| `OpenApiPath` | `string` | `/openapi/v1.json` | Path on the downstream service serving its OpenAPI JSON. |
| `AddOnlyPublishedPaths` | `bool` | `false` | Keep only paths the gateway actually proxies. |
| `IncludePaths` | `string[]?` | `null` | Regex patterns; keep only matching gateway paths. |
| `ExcludePaths` | `string[]?` | `null` | Regex patterns; drop matching gateway paths. |
| `SecurityScheme` | `string?` | `null` | Keep only this single named security scheme. |

### Merged document options (`YarpOpenApi:MergedDocument`)

| Option | Type | Default | Description |
|---|---|---|---|
| `Title` | `string` | `Gateway API` | Merged document title. |
| `Version` | `string` | `1.0.0` | Merged document version. |
| `Description` | `string?` | `null` | Merged document description. |
| `RoutePattern` | `string` | `/openapi/all.json` | Route serving the merged document. |
| `DocumentName` | `string` | `all` | Identifier used by UI adapters. |
| `RenameDuplicateSchemas` | `bool` | `false` | On a same-named schema with **different** content, rename the colliding one (and rewrite that service's `$ref`s) instead of keeping the first and warning. |

## Features

### Merged document
Set `MergeDocuments: true` to additionally serve `/openapi/all.json` combining every cluster; the merged `info` comes from `MergedDocument`. Components are unioned by name: **identically-shaped** schemas (e.g. a shared `Money` value object exposed by several services) merge silently into one. When two services define the **same name with different content**, the first is kept and a warning is logged — or, with `MergedDocument:RenameDuplicateSchemas: true`, the colliding schema is renamed (prefixed with its cluster) and that service's `$ref`s are rewritten, so the merged document stays correct for every service. Path conflicts always keep the first and warn.

### Published-paths filter & regex
`AddOnlyPublishedPaths: true` drops any downstream path that isn't reachable through a YARP route. `IncludePaths` / `ExcludePaths` apply regular expressions to the rewritten gateway paths for finer control.

### Security propagation
`securitySchemes` are propagated from each downstream document. Across services they are deduplicated by name (first wins, conflicts warned). Use the per-cluster `SecurityScheme` to keep only a specific scheme.

### Service discovery (.NET Aspire)
When `Microsoft.Extensions.ServiceDiscovery` is registered, logical destination addresses (e.g. `https://products-service`) are resolved to real endpoints before the OpenAPI document is fetched — no breaking change for static configuration. See [`sample/AppHost`](sample/README.md) for an Aspire example.

### Extensibility — the transformation pipeline
The built-in steps (path rewrite → security propagation → published-paths filter) are themselves transformers registered first; you can append, reorder, or replace them at three granularities:

```csharp
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(/* ... */)
    .AddOpenApiForYarp()
    .AddDocumentTransformer<MyDocumentTransformer>()    // whole document
    .AddOperationTransformer<MyOperationTransformer>()  // per operation
    .AddSchemaTransformer<MySchemaTransformer>();        // per schema
// .ClearOpenApiTransformers() drops the built-ins so you can define your own order
```

Each transformer receives a strongly-typed context (`ClusterName`, `Route`, `Cluster`, `Options`, `Services`, and a per-run `Items` bag):

```csharp
public sealed class MyDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info!.Description = $"Proxied via {context.ClusterName}.";
        return Task.CompletedTask;
    }
}
```

#### One class, both transforms (YARP `ITransformFactory` parity)
A class that implements **both** YARP's `ITransformFactory` (the proxy transform) and this library's `IOpenApiDocumentTransformer` is wired up from a single registration — keeping the request transform and the documentation transform in sync:

```csharp
.AddTransformFactory<MyTransformFactory>(); // registers both sides from one type
```

## Architecture — UI-agnostic by design

The core (`MMLib.OpenApiForYarp`) has **no UI dependency**. It exposes the documents through `IClusterDocumentSource`; the `.Scalar` and `.SwaggerUI` packages are thin adapters over it. To build your own UI adapter, resolve `IClusterDocumentSource` and point your UI at each `RoutePattern`:

```csharp
var source = app.Services.GetRequiredService<IClusterDocumentSource>();
foreach (var doc in source.GetDocuments())
{
    // doc.Name, doc.Title, doc.RoutePattern  (e.g. /openapi/products-cluster.json)
}
if (source.MergedDocument is { } merged) { /* ... */ }
```

## Comparison

| | MMLib.SwaggerForOcelot | yarp-swagger | **MMLib.OpenApiForYarp** |
|---|---|---|---|
| Gateway | Ocelot | YARP | **YARP** |
| OpenAPI engine | Swashbuckle / `JObject` | Swashbuckle `IDocumentFilter` | **`Microsoft.OpenApi` object model** |
| Default UI | Swagger UI | host's Swagger UI | **Scalar** (+ Swagger UI adapter) |
| Path rewrite | template diffing | manual factory per transform | **automatic from YARP transforms** |
| Config source | separate `SwaggerEndPoints` | reuses `ReverseProxy` | **reuses `ReverseProxy`** |
| Extensibility | `ReConfigureUpstreamSwaggerJson` (string) | `ISwaggerTransformFactory` | **typed document/operation/schema pipeline** |
| Service discovery | service section | — | **`Microsoft.Extensions.ServiceDiscovery` / Aspire** |
| Target | netstandard / various | netX | **net8.0; net10.0** |

## Building from source

The repository uses [NUKE](https://nuke.build/):

```bash
./build.sh Compile        # build
./build.sh Test           # build + run all tests (net8.0 + net10.0)
./build.sh Pack           # produce NuGet packages in ./artifacts
./build.sh MutationTest   # run Stryker.NET mutation testing on the core library
```

CI (build + test) and publish (on push to `main`, publishing only when the version in `Directory.Build.props` changes) run via the generated GitHub Actions workflows.

### Mutation testing

[Stryker.NET](https://stryker-mutator.io/) is configured (`stryker-config.json`, local tool in
`.config/dotnet-tools.json`). Run it directly with:

```bash
dotnet tool restore
dotnet stryker            # mutates src/MMLib.OpenApiForYarp, runs the unit + integration tests
```

An HTML report is written to `StrykerOutput/`. Scope a run with e.g.
`dotnet stryker --mutate "**/PathTransformation/*.cs"`.

## Known limitations (v1)

- No request aggregation.
- No dynamic configuration reload.
- Authenticated "Try it out" is not wired up.

## Contributing

Issues and pull requests are welcome. Run `./build.sh Test` before submitting; new behavior should come with tests.

## License

[MIT](LICENSE) © Milan Martiniak
