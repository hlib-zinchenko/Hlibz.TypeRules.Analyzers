using Sample.Endpoints;
using Sample.Handlers;

GetUserEndpoint endpoint = new();
User user = new GetUserHandler().Handle(new GetUserQuery(Guid.NewGuid()));

Console.WriteLine($"{endpoint.Route} -> {user.Name}");
