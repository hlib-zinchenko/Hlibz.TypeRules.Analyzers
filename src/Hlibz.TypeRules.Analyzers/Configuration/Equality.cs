namespace Hlibz.TypeRules.Analyzers.Configuration;

/// <summary>The equality semantics a rule set's <c>equality</c> option requires (TR202).</summary>
internal enum Equality
{
    /// <summary>
    /// Equal when their values are: a record, a struct, or a class that overrides
    /// <c>Equals(object)</c> (itself or through a base class). Value objects.
    /// </summary>
    Value,

    /// <summary>
    /// Equal when they're the same thing: a class that isn't a record. Entities, whose base class
    /// may still override <c>Equals</c> to compare ids.
    /// </summary>
    Identity,
}
