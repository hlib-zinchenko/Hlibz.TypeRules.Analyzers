using Microsoft.CodeAnalysis;

namespace Hlibz.TypeRules.Analyzers;

/// <summary>
/// Every diagnostic the package reports. IDs are public contract: never renumber or reuse one.
/// </summary>
internal static class Descriptors
{
    public const string InvalidConfigurationId = "TR000";
    public const string AccessibilityExceedsMaximumId = "TR001";
    public const string TypeMustBeSealedId = "TR002";

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

    private static string HelpLink(string id)
    {
        return "https://github.com/hlib-zinchenko/Hlibz.TypeRules.Analyzers/blob/main/docs/rules/"
            + id + ".md";
    }
}
