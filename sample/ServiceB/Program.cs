using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.MapOpenApi();

var orders = new List<Order>
{
    new("o-1", 129.8m),
    new("o-2", 19.9m),
};

var group = app.MapGroup("/orders").WithTags("Orders");
group.MapGet("/", () => orders);
group.MapGet("/{id}", (string id) =>
    orders.FirstOrDefault(o => o.Id == id) is { } order ? Results.Ok(order) : Results.NotFound());

// An internal endpoint that is NOT proxied by the gateway — demonstrates AddOnlyPublishedPaths.
app.MapGet("/internal/health", () => Results.Ok(new { status = "healthy" })).WithTags("Internal");

app.Run();

internal sealed record Order(string Id, decimal Total);
