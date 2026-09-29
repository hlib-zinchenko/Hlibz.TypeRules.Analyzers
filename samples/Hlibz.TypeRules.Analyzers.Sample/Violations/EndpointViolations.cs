// Violations of the 'endpoints' rule set: internal, sealed, in an Endpoints namespace.
// This namespace isn't one, so every endpoint here also breaks TR006.
namespace Sample.Violations;

// TR001: public. TR002: not sealed. TR006: wrong namespace.
public class ListUsersEndpoint : IEndpoint
{
    public string Route => "users";
}

// TR001: abstract types are checked for accessibility too. TR006: wrong namespace.
// No TR002: an abstract class can't be sealed.
public abstract class EndpointBase : IEndpoint
{
    public abstract string Route { get; }
}

// No TR001: 'public' inside an 'internal' class is effectively internal.
// TR002: not sealed. TR006: wrong namespace.
internal static class Admin
{
    public class DeleteUserEndpoint : IEndpoint
    {
        public string Route => "admin/users/{id}";
    }
}
