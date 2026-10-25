using System.Text.RegularExpressions;

namespace Rulpane.Viewer.Engine;

/// <summary>Where a document named in the fragment comes from: the rules of <c>r=</c>, the view of <c>v=</c> or the theme of <c>t=</c>.</summary>
public sealed partial record Source(string Value, bool IsBundled, string Folder)
{
    /// <summary>Whether the value names a rule set published to the Registry, as <c>&lt;id&gt;@&lt;version&gt;</c>.</summary>
    public bool IsRegistered => !IsBundled && !Value.StartsWith(Fragment.PackedPrefix, StringComparison.Ordinal);

    public static Source Rules(string value)
    {
        if (Registered().IsMatch(value))
        {
            return new Source(value, IsBundled: false, "rulesets");
        }

        if (value.Contains('@'))
        {
            throw new FormatException($"'{value}' is not a rule set of the Registry: name one as <id>@<version>, with all three numbers of the version.");
        }

        return Parse(value, "rulesets", "rule set");
    }

    public static Source View(string value) => Parse(value, "views", "view");

    public static Source Theme(string value) => Parse(value, "themes", "theme");

    /// <summary>A bare name is a document shipped with the viewer; <c>z:</c> is one carried in the link.</summary>
    private static Source Parse(string value, string folder, string noun)
    {
        if (value.StartsWith(Fragment.PackedPrefix, StringComparison.Ordinal))
        {
            return new Source(value, IsBundled: false, folder);
        }

        if (BundledName().IsMatch(value))
        {
            return new Source(value, IsBundled: true, folder);
        }

        throw new FormatException($"'{value}' is neither the name of a bundled {noun} nor a packed one ('{Fragment.PackedPrefix}…').");
    }

    /// <summary>The identifier and version a registered source names.</summary>
    public (string Id, string Version) Reference => (Value[..Value.LastIndexOf('@')], Value[(Value.LastIndexOf('@') + 1)..]);

    public async Task<string> ReadAsync(HttpClient http) =>
        IsRegistered ? (await Registry.PackageAsync(http, Reference.Id, Reference.Version))[0].Text
        : IsBundled ? await http.GetStringAsync($"{Folder}/{Value}.json")
        : Fragment.Unpack(Value);

    /// <summary>What the rules hold through <c>uses</c>, preferring the parts a registered rule set's package ships.</summary>
    public async Task<IReadOnlyList<Document>> HoldsAsync(HttpClient http, string text) =>
        await Registry.HoldsAsync(http, text, IsRegistered ? (await Registry.PackageAsync(http, Reference.Id, Reference.Version))[1..] : null);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex BundledName();

    // A version is pinned whole: the link says which document it ran, not which it would accept.
    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]*@\d+\.\d+\.\d+$")]
    private static partial Regex Registered();
}
