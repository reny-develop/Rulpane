using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rulpane.Viewer.Engine;

/// <summary>
/// A theme document, <c>rulealize/theme/v1</c>: the colours, corners, typeface and spacing of
/// the screen under the bar. Every value is one of a fixed set or a colour checked for contrast,
/// so a theme can change how a screen looks and never what can be read on it.
/// </summary>
public sealed partial record Theme(
    IReadOnlyDictionary<string, string> Light,
    IReadOnlyDictionary<string, string> Dark,
    string Radius,
    string Font,
    string Density)
{
    public const string Schema = "rulealize/theme/v1";

    // The mode/light and mode/dark token sets, as app.css's :root and the Penpot file state them.
    private static readonly Dictionary<string, string> DefaultLight = new()
    {
        ["bg"] = "#f2f3ef", ["surface"] = "#ffffff", ["sunken"] = "#e9ebe5", ["text"] = "#14161a",
        ["muted"] = "#5e636b", ["line"] = "#daddd5", ["line-strong"] = "#b9bdb3", ["accent"] = "#3b3fd8",
        ["accent-hover"] = "#2f32b8", ["accent-text"] = "#ffffff", ["accent-soft"] = "#e7e8fc", ["mark"] = "#d7f36b",
        ["mark-text"] = "#1e2600", ["ok"] = "#16794a", ["ok-soft"] = "#dff2e7", ["danger"] = "#c1352a",
        ["danger-soft"] = "#fae5e2", ["warn"] = "#7a5300", ["warn-soft"] = "#fdf0c8",
    };

    private static readonly Dictionary<string, string> DefaultDark = new()
    {
        ["bg"] = "#111214", ["surface"] = "#1a1b1f", ["sunken"] = "#232429", ["text"] = "#ecedef",
        ["muted"] = "#9a9ea6", ["line"] = "#2d2f35", ["line-strong"] = "#43464e", ["accent"] = "#8f92ff",
        ["accent-hover"] = "#a9abff", ["accent-text"] = "#0d0e1f", ["accent-soft"] = "#25264a", ["mark"] = "#c6e85a",
        ["mark-text"] = "#1a2100", ["ok"] = "#55c98f", ["ok-soft"] = "#163325", ["danger"] = "#ff8174",
        ["danger-soft"] = "#3d1c19", ["warn"] = "#f1c75b", ["warn-soft"] = "#3a2f12",
    };

    /// <summary>Every pair the screen sets text of the first colour on the second, which must read at 4.5:1 (WCAG AA).</summary>
    private static readonly (string Text, string On)[] Pairs =
    [
        ("text", "bg"), ("text", "surface"), ("text", "sunken"), ("text", "accent-soft"),
        ("muted", "bg"), ("muted", "surface"), ("muted", "sunken"),
        ("accent-text", "accent"), ("accent-text", "accent-hover"),
        ("accent", "bg"), ("accent", "surface"), ("accent", "accent-soft"),
        ("mark-text", "mark"),
        ("ok", "surface"), ("ok", "ok-soft"),
        ("danger", "surface"), ("danger", "danger-soft"),
        ("warn", "surface"), ("warn", "warn-soft"),
    ];

    private const double MinimumContrast = 4.5;

    // sm, md, lg, pill.
    private static readonly Dictionary<string, int[]> Radii = new()
    {
        ["sharp"] = [2, 3, 4, 4],
        ["default"] = [4, 8, 12, 999],
        ["round"] = [6, 12, 20, 999],
    };

    // Only typefaces the page already has or the device does: the CSP lets no font in from elsewhere.
    private static readonly Dictionary<string, string> Fonts = new()
    {
        ["inter"] = "\"Inter\", system-ui, -apple-system, \"Hiragino Sans\", \"Noto Sans JP\", \"Yu Gothic UI\", sans-serif",
        ["system"] = "system-ui, -apple-system, \"Segoe UI\", \"Hiragino Sans\", \"Noto Sans JP\", \"Yu Gothic UI\", sans-serif",
        ["serif"] = "\"Iowan Old Style\", \"Palatino Linotype\", Georgia, \"Hiragino Mincho ProN\", \"Yu Mincho\", \"Noto Serif JP\", serif",
        ["rounded"] = "ui-rounded, \"SF Pro Rounded\", \"Hiragino Maru Gothic ProN\", \"Arial Rounded MT Bold\", system-ui, sans-serif",
    };

    private static readonly string[] Densities = ["comfortable", "compact"];

    /// <summary>The colour names a theme may set, the same in either mode.</summary>
    public static IEnumerable<string> ColorNames => DefaultLight.Keys;

    public bool IsCompact => Density == "compact";

    /// <summary>Reads a theme and checks every colour pair it leaves on screen for contrast.</summary>
    public static Theme Parse(string json)
    {
        JsonObject root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new FormatException("A theme is a JSON object.");

        Keys(root, "", "$schema", "colors", "radius", "font", "density");
        if (String(root, "$schema") != Schema)
        {
            throw new FormatException($"$schema: a theme says \"{Schema}\".");
        }

        Dictionary<string, string> light = [], dark = [];
        if (root["colors"] is JsonNode colorsNode)
        {
            JsonObject colors = colorsNode as JsonObject ?? throw new FormatException("colors: an object of light and dark.");
            Keys(colors, "colors", "light", "dark");
            light = Colors(colors, "light");
            dark = Colors(colors, "dark");
        }

        CheckContrast(light, DefaultLight, "light");
        CheckContrast(dark, DefaultDark, "dark");

        return new Theme(
            light,
            dark,
            OneOf(root, "radius", Radii.Keys) ?? "default",
            OneOf(root, "font", Fonts.Keys) ?? "inter",
            OneOf(root, "density", Densities) ?? "comfortable");
    }

    /// <summary>
    /// The style sheet that puts the theme on <c>main</c>, and nowhere else: the bar keeps
    /// Rulpane's own look, so nothing a link brings can pass for it. Every value in it was
    /// checked by <see cref="Parse"/>, so none can close the rule it sits in.
    /// </summary>
    public string Css()
    {
        var css = new StringBuilder();
        int[] radius = Radii[Radius];
        css.Append("main.themed{")
            .Append($"--radius-sm:{radius[0]}px;--radius-md:{radius[1]}px;--radius-lg:{radius[2]}px;--radius-pill:{radius[3]}px;")
            .Append($"--font-ui:{Fonts[Font]};")
            // body read these from :root already; main reads them again from its own.
            .Append("font-family:var(--font-ui);color:var(--color-text);}");
        Mode(css, "light", Light);
        Mode(css, "dark", Dark);
        return css.ToString();
    }

    // A mode the theme leaves out keeps Rulpane's colours for it. The page behind main is painted too.
    private static void Mode(StringBuilder css, string mode, IReadOnlyDictionary<string, string> colors)
    {
        if (colors.Count == 0)
        {
            return;
        }

        css.Append($"@media (prefers-color-scheme:{mode}){{main.themed{{");
        foreach (var (name, value) in colors)
        {
            css.Append($"--color-{name}:{value};");
        }

        css.Append('}');
        if (colors.TryGetValue("bg", out string? bg))
        {
            css.Append($"body:has(main.themed){{background:{bg}}}");
        }

        css.Append('}');
    }

    private static Dictionary<string, string> Colors(JsonObject colors, string mode)
    {
        if (colors[mode] is not JsonNode node)
        {
            return [];
        }

        JsonObject set = node as JsonObject ?? throw new FormatException($"colors.{mode}: an object of colours by name.");
        Keys(set, $"colors.{mode}", [.. ColorNames]);
        return set.ToDictionary(p => p.Key, p =>
            p.Value is JsonValue v && v.TryGetValue(out string? text) && HexColor().IsMatch(text)
                ? text.ToLowerInvariant()
                : throw new FormatException($"colors.{mode}.{p.Key}: a colour is written #rrggbb."));
    }

    private static void CheckContrast(Dictionary<string, string> set, Dictionary<string, string> defaults, string mode)
    {
        if (set.Count == 0)
        {
            return;
        }

        string Color(string name) => set.GetValueOrDefault(name) ?? defaults[name];
        foreach (var (text, on) in Pairs)
        {
            double ratio = Contrast(Color(text), Color(on));
            if (ratio < MinimumContrast)
            {
                throw new FormatException(
                    $"colors.{mode}: {text} on {on} is {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1, and text needs {MinimumContrast.ToString(CultureInfo.InvariantCulture)}:1 to be read. ({Color(text)} on {Color(on)})");
            }
        }
    }

    // WCAG 2's contrast ratio of two sRGB colours.
    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int i)
        {
            double c = int.Parse(hex.AsSpan(1 + 2 * i, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
    }

    private static string? OneOf(JsonObject root, string key, IEnumerable<string> allowed)
    {
        if (root[key] is null)
        {
            return null;
        }

        string? value = root[key] is JsonValue v && v.TryGetValue(out string? text) ? text : null;
        return value is not null && allowed.Contains(value)
            ? value
            : throw new FormatException($"{key}: one of {string.Join(", ", allowed)}.");
    }

    private static string String(JsonObject parent, string key) =>
        parent[key] is JsonValue v && v.TryGetValue(out string? text) ? text : throw new FormatException($"{key}: a string is expected.");

    private static void Keys(JsonObject node, string at, params string[] allowed)
    {
        foreach (string key in node.Select(p => p.Key))
        {
            if (!allowed.Contains(key))
            {
                throw new FormatException($"{(at.Length == 0 ? key : $"{at}.{key}")}: not part of a theme here. Allowed: {string.Join(", ", allowed)}.");
            }
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
