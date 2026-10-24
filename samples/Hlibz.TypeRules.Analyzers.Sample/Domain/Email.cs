namespace Sample.Domain;

internal sealed record Email : IValueObject
{
    private Email(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Email Create(string value)
    {
        return new Email(value.Trim().ToLowerInvariant());
    }
}
