namespace Sample;

public interface IEndpoint
{
    string Route { get; }
}

public interface IRequestHandler<TRequest, TResponse>
{
    TResponse Handle(TRequest request);
}

public abstract class Entity<TId>
{
    protected Entity(TId id)
    {
        Id = id;
    }

    public TId Id { get; }
}

public interface IValueObject;

public interface IAggregateRoot;
