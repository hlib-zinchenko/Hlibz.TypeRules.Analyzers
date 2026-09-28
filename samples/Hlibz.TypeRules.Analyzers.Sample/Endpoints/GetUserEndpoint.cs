namespace Sample.Endpoints;

internal sealed class GetUserEndpoint : IEndpoint
{
    public string Route => "users/{id}";
}
