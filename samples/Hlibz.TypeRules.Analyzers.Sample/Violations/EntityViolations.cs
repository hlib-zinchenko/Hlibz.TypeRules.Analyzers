// Violations of the 'entities' rule set: sealed, private setters, read-only collections, and
// other entities referenced by id.
using Sample.Handlers;

namespace Sample.Violations.Entities;

// TR102: not sealed.
internal class Order : Entity<int>
{
    private readonly List<int> _lines = [];

    public Order(int id)
        : base(id)
    {
    }

    // TR301: a public setter.
    public string Status { get; set; } = "New";

    // TR302: a mutable collection exposed.
    public List<int> Lines => _lines;

    // TR304: another entity held instead of its id.
    public User? Owner { get; private set; }

    // Fine: a private backing collection, and a reference to another entity by id.
    public Guid OwnerId { get; private set; }
}

internal sealed class Invoice : Entity<int>
{
    // TR304: entities inside a collection count too, private ones included.
    private readonly List<Order> _orders = [];

    public Invoice(int id)
        : base(id)
    {
    }

    // TR301: init accessors count as setters unless allow_init = true.
    public decimal Total { get; init; }

    // TR302: arrays are mutable collections, and internal is visible outside the type.
    internal string[] Tags = [];

    // Fine: a read-only view.
    public IReadOnlyList<Order> Orders => _orders;
}

internal sealed class Category : Entity<int>
{
    public Category(int id)
        : base(id)
    {
    }

    // Fine: allow_self_references defaults to true, so a category may hold its parent.
    public Category? Parent { get; private set; }
}
