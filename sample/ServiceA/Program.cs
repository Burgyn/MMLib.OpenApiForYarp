using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    // Advertise a Bearer security scheme so the gateway can propagate it.
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

app.MapOpenApi(); // serves /openapi/v1.json

var products = new List<Product>
{
    new(1, "Keyboard", 49.9m),
    new(2, "Mouse", 19.9m),
    new(3, "Monitor", 199.0m),
};

var group = app.MapGroup("/products").WithTags("Products");

group.MapGet("/", () => products);
group.MapGet("/{id:int}", (int id) =>
    products.FirstOrDefault(p => p.Id == id) is { } product ? Results.Ok(product) : Results.NotFound());
group.MapPost("/", (Product product) =>
{
    products.Add(product);
    return Results.Created($"/products/{product.Id}", product);
});

app.Run();

internal sealed record Product(int Id, string Name, decimal Price);
