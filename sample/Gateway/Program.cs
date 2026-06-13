var builder = WebApplication.CreateBuilder(args);

// Service discovery lets the gateway resolve logical names (e.g. https://products-service) under
// .NET Aspire. For the static localhost configuration it is a harmless pass-through.
builder.Services.AddServiceDiscovery();

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddOpenApiForYarp();

var app = builder.Build();

app.MapReverseProxy();

// /openapi/{cluster}.json + /openapi/all.json
app.MapOpenApiForYarp();

// Scalar UI at /scalar with one tab per downstream service + the merged document
app.MapScalarForYarp();

app.MapGet("/", () => Results.Redirect("/scalar"));

app.Run();
