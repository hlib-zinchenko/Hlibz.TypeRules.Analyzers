namespace Sample.Handlers;

internal sealed record GetUserQuery(Guid Id);

internal sealed class User : Entity<Guid>
{
    public User(Guid id, string name)
        : base(id)
    {
        Name = name;
    }

    private readonly List<string> _roles = [];

    public string Name { get; private set; }

    public IReadOnlyList<string> Roles => _roles;

    public void Rename(string name)
    {
        Name = name;
    }

    public void Grant(string role)
    {
        _roles.Add(role);
    }
}

internal sealed class GetUserHandler : IGetUserHandler
{
    public User Handle(GetUserQuery request)
    {
        return new User(request.Id, "Ada");
    }
}
