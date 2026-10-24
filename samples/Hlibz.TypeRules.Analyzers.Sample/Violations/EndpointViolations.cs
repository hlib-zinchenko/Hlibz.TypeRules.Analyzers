// Violations of the 'endpoints' rule set: internal, sealed, in an Endpoints namespace.
// This namespace isn't one, so every endpoint here also breaks TR103.
namespace Sample.Violations;

// TR101: public. TR102: not sealed. TR103: wrong namespace.
public class ListUsersEndpoint : IEndpoint
{
    public string Route => "users";
}

// TR101: abstract types are checked for accessibility too. TR103: wrong namespace.
// No TR102: an abstract class can't be sealed.
public abstract class EndpointBase : IEndpoint
{
    public abstract string Route { get; }
}

// No TR101: 'public' inside an 'internal' class is effectively internal.
// TR102: not sealed. TR103: wrong namespace.
internal static class Admin
{
    public class DeleteUserEndpoint : IEndpoint
    {
        public string Route => "admin/users/{id}";
    }
}
