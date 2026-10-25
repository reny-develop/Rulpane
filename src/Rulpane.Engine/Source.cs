using System.Text.RegularExpressions;

namespace Rulpane.Engine;

/// <summary>
/// Where a document a paneset or a link names comes from: the rules of <c>r</c>, the view of
/// <c>v</c>, the theme of <c>t</c> or the paneset of <c>p</c>.
/// </summary>
public sealed partial record Source(string Value, bool IsBundled, string Folder)
{
    /// <summary>Whether the document is carried in the value itself, as <c>z:</c>, rather than named by it.</summary>
    public bool IsPacked => Value.StartsWith(Fragment.PackedPrefix, StringComparison.Ordinal);

    /// <summary>Whether the value names a rule set published to the Registry, as <c>&lt;id&gt;@&lt;version&gt;</c>.</summary>
    public bool IsRegistered => Folder == "rulesets" && !IsBundled && !IsPacked;

    /// <summary>Whether the value names a release published to the Gallery, as <c>&lt;owner&gt;/&lt;name&gt;@&lt;version&gt;</c>.</summary>
    public bool IsGallery => Folder != "rulesets" && !IsBundled && !IsPacked;

    /// <summary>What the Gallery calls this kind of document: view, theme or paneset.</summary>
    public string Kind => Folder switch
    {
        "rulesets" => "rule set",
        "views" => "view",
        "themes" => "theme",
        _ => "paneset",
    };

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

    public static Source View(string value) => Published(value, "views", "view");

    public static Source Theme(string value) => Published(value, "themes", "theme");

    public static Source Paneset(string value) => Published(value, "panesets", "paneset");

    // A bundled name, one from the Gallery, or one carried in the value.
    private static Source Published(string value, string folder, string noun)
    {
        if (InGallery().IsMatch(value))
        {
            return new Source(value, IsBundled: false, folder);
        }

        if (value.Contains('/') || value.Contains('@'))
        {
            throw new FormatException($"'{value}' is not a {noun} of the Gallery: name one as <owner>/<name>@<version>, in lowercase, with the version a whole number.");
        }

        return Parse(value, folder, noun);
    }

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

    /// <summary>The identifier and version a registered or Gallery source names.</summary>
    public (string Id, string Version) Reference => (Value[..Value.LastIndexOf('@')], Value[(Value.LastIndexOf('@') + 1)..]);

    /// <summary>The Gallery's release this names; only for a source that <see cref="IsGallery"/>.</summary>
    public Task<Published> PublishedAsync(HttpClient http) => Gallery.ReadAsync(http, Reference.Id, Reference.Version, Kind);

    public async Task<string> ReadAsync(HttpClient http) =>
        IsRegistered ? (await Registry.PackageAsync(http, Reference.Id, Reference.Version))[0].Text
        : IsGallery ? (await PublishedAsync(http)).Text
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

    // A GitHub account, as the Gallery records it, then the name, and the release the Gallery numbered.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,37}[a-z0-9])?/[a-z0-9][a-z0-9-]*@[1-9][0-9]*$")]
    private static partial Regex InGallery();
}
