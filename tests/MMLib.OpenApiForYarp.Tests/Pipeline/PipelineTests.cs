using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using MMLib.OpenApiForYarp.Abstractions;
using MMLib.OpenApiForYarp.Pipeline;

namespace MMLib.OpenApiForYarp.Tests.Pipeline;

public class PipelineTests
{
    private sealed class TransformLog
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class DocA(TransformLog log) : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(OpenApiDocument d, OpenApiDocumentTransformerContext c, CancellationToken ct)
        {
            log.Entries.Add("A");
            return Task.CompletedTask;
        }
    }

    private sealed class DocB(TransformLog log) : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(OpenApiDocument d, OpenApiDocumentTransformerContext c, CancellationToken ct)
        {
            log.Entries.Add("B");
            return Task.CompletedTask;
        }
    }

    private sealed class DocC(TransformLog log) : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(OpenApiDocument d, OpenApiDocumentTransformerContext c, CancellationToken ct)
        {
            log.Entries.Add("C");
            return Task.CompletedTask;
        }
    }

    private sealed class AddPathTransformer : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(OpenApiDocument d, OpenApiDocumentTransformerContext c, CancellationToken ct)
        {
            d.Paths["/added"] = new OpenApiPathItem();
            return Task.CompletedTask;
        }
    }

    private sealed class TagOperationTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext c, CancellationToken ct)
        {
            operation.Description = "tagged";
            return Task.CompletedTask;
        }
    }

    private static (OpenApiTransformerPipeline Pipeline, ServiceProvider Services, TransformLog Log) Build(
        Action<OpenApiTransformerRegistry> configure)
    {
        var log = new TransformLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddTransient<DocA>().AddTransient<DocB>().AddTransient<DocC>()
            .AddTransient<AddPathTransformer>().AddTransient<TagOperationTransformer>();

        var registry = new OpenApiTransformerRegistry();
        configure(registry);
        var provider = services.BuildServiceProvider();
        return (new OpenApiTransformerPipeline(registry), provider, log);
    }

    [Fact]
    public async Task TransformerOrder_Respected()
    {
        var (pipeline, services, log) = Build(r =>
        {
            r.AddDocumentTransformer(typeof(DocA));
            r.AddDocumentTransformer(typeof(DocB));
            r.AddDocumentTransformer(typeof(DocC));
        });
        var doc = TestDocuments.WithPaths("/x");

        await pipeline.RunAsync(doc, TestContexts.ForCluster(doc, services), CancellationToken.None);

        log.Entries.ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public async Task CustomTransformer_AppendedAfterBuiltins()
    {
        var (pipeline, services, log) = Build(r =>
        {
            r.AddDocumentTransformer(typeof(DocA)); // stands in for a built-in
            r.AddDocumentTransformer(typeof(DocC)); // custom appended after
        });
        var doc = TestDocuments.WithPaths("/x");

        await pipeline.RunAsync(doc, TestContexts.ForCluster(doc, services), CancellationToken.None);

        log.Entries.ShouldBe(["A", "C"]);
    }

    [Fact]
    public async Task BuiltinTransformer_CanBeRemoved()
    {
        var (pipeline, services, log) = Build(r =>
        {
            r.AddDocumentTransformer(typeof(DocA));
            r.AddDocumentTransformer(typeof(DocB));
            r.ClearDocumentTransformers();          // drop built-ins
            r.AddDocumentTransformer(typeof(DocC)); // register own
        });
        var doc = TestDocuments.WithPaths("/x");

        await pipeline.RunAsync(doc, TestContexts.ForCluster(doc, services), CancellationToken.None);

        log.Entries.ShouldBe(["C"]);
    }

    [Fact]
    public async Task CustomTransformer_CanMutateDocument()
    {
        var (pipeline, services, _) = Build(r => r.AddDocumentTransformer(typeof(AddPathTransformer)));
        var doc = TestDocuments.WithPaths("/x");

        await pipeline.RunAsync(doc, TestContexts.ForCluster(doc, services), CancellationToken.None);

        doc.Paths.ShouldContainKey("/added");
    }

    [Fact]
    public async Task OperationTransformers_RunAcrossAllOperations()
    {
        var (pipeline, services, _) = Build(r => r.AddOperationTransformer(typeof(TagOperationTransformer)));
        var doc = TestDocuments.WithPaths("/products", "/orders");

        await pipeline.RunAsync(doc, TestContexts.ForCluster(doc, services), CancellationToken.None);

        var products = (OpenApiPathItem)doc.Paths["/products"];
        products.Operations![System.Net.Http.HttpMethod.Get].Description.ShouldBe("tagged");
    }
}
