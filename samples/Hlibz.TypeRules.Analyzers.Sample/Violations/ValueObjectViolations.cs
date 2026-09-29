// Violations of the 'value-objects' rule set: sealed, immutable, value equality, and created
// through factory methods. Plus the 'aggregates' rule set: identity equality.
namespace Sample.Violations.ValueObjects;

// TR202: a class without Equals compares by reference. TR201: the implicit constructor is internal.
internal sealed class Money : IValueObject
{
    // TR303: a field that isn't readonly.
    private decimal _amount;

    // TR303: a set accessor, even a private one: the instance can still change.
    public string Currency { get; private set; } = "EUR";

    public decimal Amount => _amount;

    public void Add(decimal amount)
    {
        _amount += amount;
    }
}

// TR303: a struct that isn't a readonly struct. TR201: an internal constructor.
internal struct Percent : IValueObject
{
    public Percent(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }
}

// TR303: a mutable collection is mutable state, even a private readonly one.
internal sealed record Address : IValueObject
{
    private readonly List<string> _lines;

    private Address(List<string> lines)
    {
        _lines = lines;
    }

    public IReadOnlyList<string> Lines => _lines;

    public static Address Create(params string[] lines)
    {
        return new Address([.. lines]);
    }
}

// Fine: a readonly record struct with a private constructor and a factory method.
internal readonly record struct Quantity : IValueObject
{
    private Quantity(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static Quantity Of(int value)
    {
        return new Quantity(value);
    }
}

// TR202: an aggregate root that's a record compares all its state rather than its identity.
internal sealed record Cart(Guid Id) : IAggregateRoot;
