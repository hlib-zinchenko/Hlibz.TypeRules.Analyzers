namespace Sample.Violations;

// TR001 and TR002: public, and not sealed.
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
}

// TR003: a handler with no IListOrdersHandler companion interface.
internal sealed class ListOrdersHandler : IRequestHandler<int, Order>
{
    public Order Handle(int request)
    {
        return new Order(request);
    }
}
