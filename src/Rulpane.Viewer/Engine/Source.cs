using System.Text.RegularExpressions;

namespace Rulpane.Viewer.Engine;

/// <summary>Where a document named in the fragment comes from: the rules of <c>r=</c>, the view of <c>v=</c> or the theme of <c>t=</c>.</summary>
public sealed partial record Source(string Value, bool IsBundled, string Folder)
{
    public static Source Rules(string value) => Parse(value, "rulesets", "rule set");

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

    public async Task<string> ReadAsync(HttpClient http) =>
        IsBundled
            ? await http.GetStringAsync($"{Folder}/{Value}.json")
            : Fragment.Unpack(Value);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex BundledName();
}
