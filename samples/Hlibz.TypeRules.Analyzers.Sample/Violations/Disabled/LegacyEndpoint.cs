// Fine: the 'endpoints' rule set is switched off in this folder (enabled = false), so this public,
// unsealed endpoint outside an Endpoints namespace reports nothing.
namespace Sample.Violations.Disabled;

public class LegacyEndpoint : IEndpoint
{
    public string Route => "legacy";
}
