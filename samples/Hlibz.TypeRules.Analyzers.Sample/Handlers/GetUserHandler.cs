namespace Sample.Handlers;

internal sealed record GetUserQuery(Guid Id);

internal sealed class User : Entity<Guid>
{
    public User(Guid id, string name)
        : base(id)
    {
        Name = name;
    }

    public string Name { get; }
}

internal sealed class GetUserHandler : IGetUserHandler
{
    public User Handle(GetUserQuery request)
    {
        return new User(request.Id, "Ada");
    }
}
