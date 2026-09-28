namespace Sample.Violations;

// TR001 and TR002: public, and not sealed. TR006: not in an Endpoints namespace.
public class ListUsersEndpoint : IEndpoint
{
    public string Route => "users";
}

// TR002: an entity that isn't sealed.
internal class Order : Entity<int>
{
    public Order(int id)
        : base(id)
    {
    }

    // TR004: a public setter on an entity.
    public string Status { get; set; } = "New";

    // TR005: a mutable collection exposed by an entity.
    public List<int> Lines { get; } = [];

    // TR007: an entity holding another entity instead of its id.
    public Handlers.User? Owner { get; private set; }
}

// TR003: a handler with no IListOrdersHandler companion interface.
internal sealed class ListOrdersHandler : IRequestHandler<int, Order>
{
    public Order Handle(int request)
    {
        return new Order(request);
    }
}
