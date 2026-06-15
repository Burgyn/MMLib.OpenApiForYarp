using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sample.Common;

namespace ServiceB.Ordering;

/// <summary>In-memory ordering backing store, seeded with sample orders.</summary>
internal sealed class OrderingStore
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public OrderingStore()
    {
        foreach (Order order in Seed())
        {
            _orders[order.Id] = order;
        }
    }

    public IEnumerable<Order> All => _orders.Values;

    public Order? Get(Guid id) => _orders.TryGetValue(id, out Order? o) ? o : null;

    public Order Add(Order order)
    {
        _orders[order.Id] = order;
        return order;
    }

    public bool TryReplace(Guid id, Func<Order, Order> update, out Order? updated)
    {
        if (_orders.TryGetValue(id, out Order? existing))
        {
            updated = update(existing);
            _orders[id] = updated;
            return true;
        }

        updated = null;
        return false;
    }

    private static IEnumerable<Order> Seed()
    {
        var now = DateTimeOffset.UtcNow;

        var alice = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bob = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var keyboard = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var mouse = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var monitor = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var hub = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var order1Items = new List<OrderItem>
        {
            new(keyboard, "Mechanical Keyboard", 1, 89.90m),
            new(mouse, "Wireless Mouse", 2, 39.50m),
        };
        yield return new Order(
            Guid.Parse("d1111111-1111-1111-1111-111111111111"), alice, OrderStatus.Delivered,
            order1Items, Sum(order1Items), Currency.Eur, now.AddDays(-21));

        var order2Items = new List<OrderItem>
        {
            new(monitor, "27\" 4K Monitor", 1, 379.00m),
        };
        yield return new Order(
            Guid.Parse("d2222222-2222-2222-2222-222222222222"), alice, OrderStatus.Shipped,
            order2Items, Sum(order2Items), Currency.Eur, now.AddDays(-5));

        var order3Items = new List<OrderItem>
        {
            new(hub, "USB-C Hub", 3, 49.00m),
            new(mouse, "Wireless Mouse", 1, 39.50m),
        };
        yield return new Order(
            Guid.Parse("d3333333-3333-3333-3333-333333333333"), bob, OrderStatus.Paid,
            order3Items, Sum(order3Items), Currency.Eur, now.AddDays(-2));

        var order4Items = new List<OrderItem>
        {
            new(keyboard, "Mechanical Keyboard", 1, 89.90m),
        };
        yield return new Order(
            Guid.Parse("d4444444-4444-4444-4444-444444444444"), bob, OrderStatus.Pending,
            order4Items, Sum(order4Items), Currency.Eur, now.AddHours(-3));
    }

    private static decimal Sum(IEnumerable<OrderItem> items)
        => items.Sum(i => i.Quantity * i.UnitPrice);
}

/// <summary>Maps the order and line-item endpoints.</summary>
internal static class OrderingApi
{
    public static IEndpointRouteBuilder MapOrders(this IEndpointRouteBuilder app)
    {
        var store = new OrderingStore();

        RouteGroupBuilder orders = app.MapGroup("/orders").WithTags("Orders");

        orders.MapGet("/", (
                [AsParameters] OrderQuery query,
                [FromHeader(Name = "X-Tenant-Id")] string? tenantId,
                [FromQuery] OrderStatus[]? statuses,
                [FromQuery] DateTimeOffset? placedAfter,
                [FromQuery] DateTimeOffset? placedBefore) =>
            {
                IEnumerable<Order> result = store.All;

                if (query.Status is { } status)
                {
                    result = result.Where(o => o.Status == status);
                }

                if (statuses is { Length: > 0 })
                {
                    result = result.Where(o => statuses.Contains(o.Status));
                }

                if (query.CustomerId is { } customerId)
                {
                    result = result.Where(o => o.CustomerId == customerId);
                }

                if (placedAfter is { } after)
                {
                    result = result.Where(o => o.PlacedAt >= after);
                }

                if (placedBefore is { } before)
                {
                    result = result.Where(o => o.PlacedAt <= before);
                }

                result = query.Sort?.ToLowerInvariant() switch
                {
                    "placedat" => result.OrderBy(o => o.PlacedAt),
                    "-placedat" => result.OrderByDescending(o => o.PlacedAt),
                    "total" => result.OrderByDescending(o => o.Total),
                    _ => result.OrderByDescending(o => o.PlacedAt),
                };

                return TypedResults.Ok(result.ToPagedResult(query.Page, query.PageSize));
            })
            .WithName("ListOrders")
            .WithSummary("List orders")
            .WithDescription("Returns a paged list of orders with optional status, multi-status, customer and placed-date-range filters and sorting. Supports multi-tenancy via the X-Tenant-Id header.");

        orders.MapGet("/{id:guid}", Results<Ok<Order>, NotFound<ProblemDetails>> (Guid id) =>
                store.Get(id) is { } order
                    ? TypedResults.Ok(order)
                    : TypedResults.NotFound(Problems.NotFound("Order", id)))
            .WithName("GetOrder")
            .WithSummary("Get an order by id");

        orders.MapPost("/", Results<Created<Order>, ValidationProblem> (CreateOrderRequest request) =>
            {
                if (!Validation.TryValidate(request, out var errors))
                {
                    return TypedResults.ValidationProblem(errors);
                }

                IReadOnlyList<OrderItem> items =
                [
                    .. request.Items.Select(i => new OrderItem(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice)),
                ];
                decimal total = items.Sum(i => i.Quantity * i.UnitPrice);

                var order = new Order(
                    Guid.NewGuid(), request.CustomerId, OrderStatus.Pending,
                    items, total, Currency.Eur, DateTimeOffset.UtcNow);
                store.Add(order);
                return TypedResults.Created($"/orders/{order.Id}", order);
            })
            .WithName("CreateOrder")
            .WithSummary("Place an order");

        orders.MapPost("/{id:guid}/cancel", Results<Ok<Order>, NotFound<ProblemDetails>, Conflict<ProblemDetails>> (Guid id) =>
            {
                if (store.Get(id) is not { } order)
                {
                    return TypedResults.NotFound(Problems.NotFound("Order", id));
                }

                if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
                {
                    return TypedResults.Conflict(
                        Problems.Conflict($"Order '{id}' cannot be cancelled because its status is '{order.Status}'."));
                }

                store.TryReplace(id, existing => existing with { Status = OrderStatus.Cancelled }, out Order? updated);
                return TypedResults.Ok(updated!);
            })
            .WithName("CancelOrder")
            .WithSummary("Cancel an order");

        orders.MapPost("/{id:guid}/notes", Results<Ok<object>, NotFound<ProblemDetails>, ValidationProblem> (Guid id, AddNoteRequest request) =>
            {
                if (!Validation.TryValidate(request, out var errors))
                {
                    return TypedResults.ValidationProblem(errors);
                }

                return store.Get(id) is { } order
                    ? TypedResults.Ok<object>(new { orderId = order.Id, note = request.Text, addedAt = DateTimeOffset.UtcNow })
                    : TypedResults.NotFound(Problems.NotFound("Order", id));
            })
            .WithName("AddOrderNote")
            .WithSummary("Add a note to an order")
            .WithDescription("Attaches a short free-text note to an order and echoes it back.");

        orders.MapGet("/{orderId:guid}/items", Results<Ok<IReadOnlyList<OrderItem>>, NotFound<ProblemDetails>> (Guid orderId) =>
                store.Get(orderId) is { } order
                    ? TypedResults.Ok(order.Items)
                    : TypedResults.NotFound(Problems.NotFound("Order", orderId)))
            .WithName("ListOrderItems")
            .WithSummary("List items of an order");

        orders.MapGet("/{orderId:guid}/items/{itemId:guid}", Results<Ok<OrderItem>, NotFound<ProblemDetails>> (Guid orderId, Guid itemId) =>
            {
                if (store.Get(orderId) is not { } order)
                {
                    return TypedResults.NotFound(Problems.NotFound("Order", orderId));
                }

                return order.Items.FirstOrDefault(i => i.ProductId == itemId) is { } item
                    ? TypedResults.Ok(item)
                    : TypedResults.NotFound(Problems.NotFound("Order item", itemId));
            })
            .WithName("GetOrderItem")
            .WithSummary("Get a single order item");

        return app;
    }
}
