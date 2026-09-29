using Microsoft.CodeAnalysis;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>
/// Every diagnostic the package reports, grouped in blocks: TR0xx configuration, TR1xx the type's
/// declaration, TR2xx construction and equality, TR3xx stored state. IDs are public contract:
/// never renumber or reuse one.
/// </summary>
internal static class Descriptors
{
    public const string InvalidConfigurationId = "TR000";

    public const string AccessibilityExceedsMaximumId = "TR101";
    public const string TypeMustBeSealedId = "TR102";
    public const string WrongNamespaceId = "TR103";
    public const string CompanionInterfaceMissingId = "TR104";

    public const string ConstructorExceedsMaximumId = "TR201";
    public const string WrongEqualityId = "TR202";

    public const string SetterExceedsMaximumId = "TR301";
    public const string MutableCollectionExposedId = "TR302";
    public const string TypeMustBeImmutableId = "TR303";
    public const string ForbiddenMemberTypeId = "TR304";

    public static readonly DiagnosticDescriptor InvalidConfiguration = new(
        InvalidConfigurationId,
        title: "TypeRules configuration is invalid",
        messageFormat: "Invalid TypeRules configuration: {0}",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A typerules.* key in .editorconfig or .globalconfig could not be applied. "
            + "An invalid rule set is ignored entirely, so it doesn't half-apply.",
        helpLinkUri: HelpLink(InvalidConfigurationId));

    // TR1xx: the type's declaration.

    public static readonly DiagnosticDescriptor AccessibilityExceedsMaximum = new(
        AccessibilityExceedsMaximumId,
        title: "Type is more accessible than its rule set allows",
        messageFormat: "'{0}' is {1}, but rule set {2} allows at most {3}",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Types that inherit from or implement a rule set's matched types must not be "
            + "accessible from more places than the rule set's max_accessibility allows.",
        helpLinkUri: HelpLink(AccessibilityExceedsMaximumId));

    public static readonly DiagnosticDescriptor TypeMustBeSealed = new(
        TypeMustBeSealedId,
        title: "Type must be sealed",
        messageFormat: "'{0}' must be sealed, as required by rule set {1}",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Non-abstract classes that inherit from or implement a rule set's matched "
            + "types must be sealed when the rule set sets require_sealed = true.",
        helpLinkUri: HelpLink(TypeMustBeSealedId));

    public static readonly DiagnosticDescriptor WrongNamespace = new(
        WrongNamespaceId,
        title: "Type is declared in the wrong namespace",
        messageFormat: "'{0}' is declared in {1}, but rule set '{2}' requires a namespace "
            + "matching {3}",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Types that inherit from or implement a rule set's matched types must be "
            + "declared in a namespace matching one of the rule set's namespace_pattern "
            + "alternatives.",
        helpLinkUri: HelpLink(WrongNamespaceId));

    public static readonly DiagnosticDescriptor CompanionInterfaceMissing = new(
        CompanionInterfaceMissingId,
        title: "Type must implement its companion interface",
        messageFormat: "'{0}' must implement a companion interface '{1}' that extends {2}, as "
            + "required by rule set {3}",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Non-abstract types that implement a rule set's matched interfaces must also "
            + "implement an interface named after them (Foo implements IFoo), declared next to "
            + "them, that itself extends a matched interface, when the rule set sets "
            + "companion_interface = true. Consumers then depend on IFoo, which is what "
            + "reflection-based dependency injection registers.",
        helpLinkUri: HelpLink(CompanionInterfaceMissingId));

    // TR2xx: how instances are created and compared.

    public static readonly DiagnosticDescriptor ConstructorExceedsMaximum = new(
        ConstructorExceedsMaximumId,
        title: "Constructor is more accessible than its rule set allows",
        messageFormat: "Constructor '{0}' is {1}, but rule set {2} allows at most {3}",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Constructors of non-abstract types that inherit from or implement a rule "
            + "set's matched types, including the implicit default constructor and primary "
            + "constructors, must not be accessible from more places than the rule set's "
            + "max_constructor_accessibility allows. Create instances through factory methods "
            + "that enforce invariants instead.",
        helpLinkUri: HelpLink(ConstructorExceedsMaximumId));

    public static readonly DiagnosticDescriptor WrongEquality = new(
        WrongEqualityId,
        title: "Type has the wrong equality semantics",
        messageFormat: "'{0}' {1}, but rule set {2} requires {3} equality",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Types that inherit from or implement a rule set's matched types must compare "
            + "the way the rule set's equality option says. With equality = value (value "
            + "objects), they must be records or structs, or override Equals(object) themselves "
            + "or through a base class. With equality = identity (entities), they must be classes "
            + "that aren't records, since a record compares all its state rather than its id.",
        helpLinkUri: HelpLink(WrongEqualityId));

    // TR3xx: the state a type stores, and who can change it.

    public static readonly DiagnosticDescriptor SetterExceedsMaximum = new(
        SetterExceedsMaximumId,
        title: "Setter is more accessible than its rule set allows",
        messageFormat: "The {0} accessor of '{1}' is {2}, but rule set {3} allows at most {4}",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Properties declared by types that inherit from or implement a rule set's "
            + "matched types must not have set (or, unless allow_init is set, init) accessors "
            + "accessible from more places than the rule set's max_setter_accessibility allows.",
        helpLinkUri: HelpLink(SetterExceedsMaximumId));

    public static readonly DiagnosticDescriptor MutableCollectionExposed = new(
        MutableCollectionExposedId,
        title: "Mutable collection is exposed",
        messageFormat: "'{0}' exposes the mutable collection type '{1}', but rule set {2} "
            + "requires read-only collections",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Properties and fields visible outside types that inherit from or implement "
            + "a rule set's matched types must not have a mutable collection type (arrays, "
            + "List<T>, IList<T>, ICollection<T>, dictionaries, sets, ...) when the rule set sets "
            + "readonly_collections = true. Expose IReadOnlyList<T>, IReadOnlyCollection<T> or "
            + "IReadOnlyDictionary<TKey, TValue> over a private collection instead.",
        helpLinkUri: HelpLink(MutableCollectionExposedId));

    public static readonly DiagnosticDescriptor TypeMustBeImmutable = new(
        TypeMustBeImmutableId,
        title: "Type must be immutable",
        messageFormat: "'{0}' {1}, but rule set {2} requires immutable types",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Types that inherit from or implement a rule set's matched types can't change "
            + "after construction when the rule set sets require_immutable = true: instance "
            + "fields must be readonly, properties can't have set accessors (init is fine), "
            + "stored state can't be a mutable collection, and structs must be readonly structs.",
        helpLinkUri: HelpLink(TypeMustBeImmutableId));

    public static readonly DiagnosticDescriptor ForbiddenMemberType = new(
        ForbiddenMemberTypeId,
        title: "Type holds a reference to a forbidden type",
        messageFormat: "'{0}' holds '{1}', a type of rule set '{2}', which rule set '{3}' forbids",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Fields and auto-properties of types that inherit from or implement a rule "
            + "set's matched types must not hold types of the rule sets named in its "
            + "forbid_member_types, directly or inside collections and other generic types. "
            + "For aggregate roots referencing each other, store the other one's id instead.",
        helpLinkUri: HelpLink(ForbiddenMemberTypeId));

    private static string HelpLink(string id)
    {
        return "https://github.com/hlib-zinchenko/Hlibz.TypeRules.Analyzers/blob/main/docs/rules/"
            + id + ".md";
    }
}
