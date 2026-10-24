using Sample.Domain;
using Sample.Endpoints;
using Sample.Handlers;

GetUserEndpoint endpoint = new();
IGetUserHandler handler = new GetUserHandler();
User user = handler.Handle(new GetUserQuery(Guid.NewGuid()));
Email email = Email.Create(" Ada@Example.com ");

Console.WriteLine($"{endpoint.Route} -> {user.Name} <{email.Value}>");
