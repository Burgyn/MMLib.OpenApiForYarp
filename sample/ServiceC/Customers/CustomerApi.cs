using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sample.Common;

namespace ServiceC.Customers;

/// <summary>In-memory customer backing store, seeded with sample customers.</summary>
internal sealed class CustomerStore
{
    private readonly ConcurrentDictionary<Guid, Customer> _customers = new();

    public CustomerStore()
    {
        foreach (Customer customer in Seed())
        {
            _customers[customer.Id] = customer;
        }
    }

    public IEnumerable<Customer> All => _customers.Values;

    public Customer? Get(Guid id) => _customers.TryGetValue(id, out Customer? c) ? c : null;

    public Customer Add(Customer customer)
    {
        _customers[customer.Id] = customer;
        return customer;
    }

    public bool TryReplace(Guid id, Func<Customer, Customer> update, out Customer? updated)
    {
        if (_customers.TryGetValue(id, out Customer? existing))
        {
            updated = update(existing);
            _customers[id] = updated;
            return true;
        }

        updated = null;
        return false;
    }

    public bool Remove(Guid id) => _customers.TryRemove(id, out _);

    private static IEnumerable<Customer> Seed()
    {
        var now = DateTimeOffset.UtcNow;
        yield return new Customer(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Ada Lovelace", "ada.lovelace@example.com", "+44 20 7946 0991",
            LoyaltyTier.Platinum,
            [
                new Address(Guid.Parse("a1111111-1111-1111-1111-111111111111"), "12 Baker Street", "Flat 3", "London", "W1U 6TU", "GB", true),
            ],
            now.AddDays(-220));
        yield return new Customer(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Alan Turing", "alan.turing@example.com", "+44 161 850 2000",
            LoyaltyTier.Gold,
            [
                new Address(Guid.Parse("b1111111-1111-1111-1111-111111111111"), "78 High Street", null, "Manchester", "M4 1HN", "GB", true),
                new Address(Guid.Parse("b2222222-2222-2222-2222-222222222222"), "5 Park Avenue", "Suite 200", "Manchester", "M2 3AA", "GB", false),
            ],
            now.AddDays(-150));
        yield return new Customer(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "Grace Hopper", "grace.hopper@example.com", null,
            LoyaltyTier.Silver,
            [],
            now.AddDays(-60));
        yield return new Customer(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "Katherine Johnson", "katherine.johnson@example.com", "+1 757 555 0182",
            LoyaltyTier.Bronze,
            [
                new Address(Guid.Parse("d1111111-1111-1111-1111-111111111111"), "100 NASA Drive", null, "Hampton", "23666", "US", true),
            ],
            now.AddDays(-15));
    }
}

/// <summary>Maps the customer and address endpoints.</summary>
internal static class CustomerApi
{
    public static IEndpointRouteBuilder MapCustomers(this IEndpointRouteBuilder app)
    {
        var store = new CustomerStore();

        RouteGroupBuilder customers = app.MapGroup("/customers").WithTags("Customers");

        customers.MapGet("/", (
                [AsParameters] CustomerQuery query,
                [FromHeader(Name = "X-Tenant-Id")] string? tenantId,
                [FromQuery] LoyaltyTier[]? tiers) =>
            {
                IEnumerable<Customer> result = store.All;

                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    result = result.Where(c =>
                        c.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                        || c.Email.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
                }

                if (query.Tier is { } tier)
                {
                    result = result.Where(c => c.Tier == tier);
                }

                if (tiers is { Length: > 0 })
                {
                    result = result.Where(c => tiers.Contains(c.Tier));
                }

                result = query.Sort?.ToLowerInvariant() switch
                {
                    "createdat" => result.OrderBy(c => c.CreatedAt),
                    "-createdat" => result.OrderByDescending(c => c.CreatedAt),
                    _ => result.OrderBy(c => c.Name),
                };

                return TypedResults.Ok(result.ToPagedResult(query.Page, query.PageSize));
            })
            .WithName("ListCustomers")
            .WithSummary("List customers")
            .WithDescription("Returns a paged list of customers with optional search (name/email), single- and multi-tier loyalty filters and sorting. Supports multi-tenancy via the X-Tenant-Id header.");

        customers.MapGet("/{id:guid}", Results<Ok<Customer>, NotFound<ProblemDetails>> (Guid id) =>
                store.Get(id) is { } customer
                    ? TypedResults.Ok(customer)
                    : TypedResults.NotFound(Problems.NotFound("Customer", id)))
            .WithName("GetCustomer")
            .WithSummary("Get a customer by id");

        customers.MapPost("/", Results<Created<Customer>, ValidationProblem> (CreateCustomerRequest request) =>
            {
                if (!Validation.TryValidate(request, out var errors))
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var customer = new Customer(
                    Guid.NewGuid(), request.Name, request.Email, request.Phone, request.Tier, [], DateTimeOffset.UtcNow);
                store.Add(customer);
                return TypedResults.Created($"/customers/{customer.Id}", customer);
            })
            .WithName("CreateCustomer")
            .WithSummary("Create a customer");

        customers.MapPut("/{id:guid}", Results<Ok<Customer>, NotFound<ProblemDetails>, ValidationProblem> (Guid id, UpdateCustomerRequest request) =>
            {
                if (!Validation.TryValidate(request, out var errors))
                {
                    return TypedResults.ValidationProblem(errors);
                }

                return store.TryReplace(id, existing => existing with
                {
                    Name = request.Name,
                    Email = request.Email,
                    Phone = request.Phone,
                    Tier = request.Tier,
                }, out Customer? updated)
                    ? TypedResults.Ok(updated!)
                    : TypedResults.NotFound(Problems.NotFound("Customer", id));
            })
            .WithName("UpdateCustomer")
            .WithSummary("Replace a customer");

        customers.MapDelete("/{id:guid}", Results<NoContent, NotFound<ProblemDetails>> (Guid id) =>
                store.Remove(id)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound(Problems.NotFound("Customer", id)))
            .WithName("DeleteCustomer")
            .WithSummary("Delete a customer");

        customers.MapGet("/{id:guid}/addresses", Results<Ok<IReadOnlyList<Address>>, NotFound<ProblemDetails>> (Guid id) =>
                store.Get(id) is { } customer
                    ? TypedResults.Ok(customer.Addresses)
                    : TypedResults.NotFound(Problems.NotFound("Customer", id)))
            .WithName("ListCustomerAddresses")
            .WithSummary("List a customer's addresses");

        customers.MapPost("/{id:guid}/addresses", Results<Created<Address>, NotFound<ProblemDetails>, ValidationProblem> (Guid id, CreateAddressRequest request) =>
            {
                if (!Validation.TryValidate(request, out var errors))
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var address = new Address(
                    Guid.NewGuid(), request.Line1, request.Line2, request.City, request.PostalCode, request.Country, request.IsPrimary);

                return store.TryReplace(id, existing => existing with
                {
                    Addresses = [.. existing.Addresses, address],
                }, out _)
                    ? TypedResults.Created($"/customers/{id}/addresses/{address.Id}", address)
                    : TypedResults.NotFound(Problems.NotFound("Customer", id));
            })
            .WithName("AddCustomerAddress")
            .WithSummary("Add an address to a customer");

        return app;
    }
}
