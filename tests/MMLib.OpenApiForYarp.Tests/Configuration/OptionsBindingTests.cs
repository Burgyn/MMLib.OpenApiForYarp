using Microsoft.Extensions.Configuration;
using MMLib.OpenApiForYarp.Configuration;

namespace MMLib.OpenApiForYarp.Tests.Configuration;

public class OptionsBindingTests
{
    private static IConfiguration BuildConfig(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    [Fact]
    public void Binds_Clusters_And_Flags()
    {
        const string json = """
            {
              "YarpOpenApi": {
                "MergeDocuments": true,
                "MergedDocument": { "Title": "My Gateway", "Version": "2.0" },
                "Clusters": {
                  "products-cluster": { "Title": "Products API", "OpenApiPath": "/openapi/v1.json" },
                  "orders-cluster": { "Title": "Orders API", "AddOnlyPublishedPaths": true }
                }
              }
            }
            """;

        var options = BuildConfig(json).GetSection(YarpOpenApiOptions.SectionName).Get<YarpOpenApiOptions>()!;

        options.MergeDocuments.ShouldBeTrue();
        options.MergedDocument.Title.ShouldBe("My Gateway");
        options.Clusters.ShouldContainKey("products-cluster");
        options.Clusters["orders-cluster"].AddOnlyPublishedPaths.ShouldBeTrue();
        options.Clusters["products-cluster"].AddOnlyPublishedPaths.ShouldBeFalse();
    }

    [Fact]
    public void Applies_Defaults_When_Unset()
    {
        var options = new YarpOpenApiOptions();

        options.CacheDuration.ShouldBe(TimeSpan.FromSeconds(60));
        options.FetchTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.MergeDocuments.ShouldBeFalse();
        new YarpOpenApiClusterOptions().OpenApiPath.ShouldBe("/openapi/v1.json");
    }

    [Fact]
    public void GetDocumentRoute_Substitutes_ClusterId()
        => new YarpOpenApiOptions().GetDocumentRoute("products-cluster")
            .ShouldBe("/openapi/products-cluster.json");

    [Fact]
    public void Cluster_Lookup_Is_CaseInsensitive()
    {
        var options = new YarpOpenApiOptions();
        options.Clusters["Products"] = new YarpOpenApiClusterOptions { Title = "P" };

        options.Clusters.ContainsKey("products").ShouldBeTrue();
    }
}
