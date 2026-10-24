using System.Text.RegularExpressions;

namespace Rulpane.Viewer.Engine;

/// <summary>Where the rules named by <c>r=</c> come from.</summary>
public sealed partial record RuleSource(string Value, bool IsBundled)
{
    /// <summary>A bare name is a rule set shipped with the viewer; <c>z:</c> is one carried in the link.</summary>
    public static RuleSource Parse(string value)
    {
        if (value.StartsWith(Fragment.PackedPrefix, StringComparison.Ordinal))
        {
            return new RuleSource(value, IsBundled: false);
        }

        if (BundledName().IsMatch(value))
        {
            return new RuleSource(value, IsBundled: true);
        }

        throw new FormatException($"'{value}' is neither the name of a bundled rule set nor a packed one ('{Fragment.PackedPrefix}…').");
    }

    public async Task<string> ReadAsync(HttpClient http) =>
        IsBundled
            ? await http.GetStringAsync($"rulesets/{Value}.json")
            : Fragment.Unpack(Value);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex BundledName();
}
