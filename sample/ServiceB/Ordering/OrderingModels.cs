using System.ComponentModel.DataAnnotations;

namespace ServiceB.Ordering;

/// <summary>Lifecycle state of an order.</summary>
public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Delivered,
    Cancelled,
}

/// <summary>Currency an order total is expressed in.</summary>
public enum Currency
{
    Eur,
    Usd,
    Gbp,
}

/// <summary>A customer order with its line items and fulfillment status.</summary>
public sealed record Order(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    IReadOnlyList<OrderItem> Items,
    decimal Total,
    Currency Currency,
    DateTimeOffset PlacedAt);

/// <summary>A single line item within an order.</summary>
public sealed record OrderItem(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

/// <summary>Request body to place a new order.</summary>
public sealed record CreateOrderRequest
{
    [Required]
    public Guid CustomerId { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public IReadOnlyList<CreateOrderItemRequest> Items { get; init; } = [];
}

/// <summary>Request body for a single line item when placing an order.</summary>
public sealed record CreateOrderItemRequest
{
    [Required]
    public Guid ProductId { get; init; }

    [Required]
    [StringLength(120, MinimumLength = 1)]
    public string ProductName { get; init; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; init; }

    [Range(0.01, 1_000_000)]
    public decimal UnitPrice { get; init; }
}

/// <summary>Query parameters for listing orders.</summary>
public readonly record struct OrderQuery(
    int? Page,
    int? PageSize,
    OrderStatus? Status,
    Guid? CustomerId,
    string? Sort);

/// <summary>Request body to attach a free-text note to an order.</summary>
public sealed record AddNoteRequest
{
    /// <summary>The note text to record against the order.</summary>
    [Required]
    [StringLength(500)]
    public string Text { get; init; } = string.Empty;
}
