using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rulpane.Engine;

/// <summary>
/// A paneset, <c>rulpane/paneset/v1</c>: a screen as one document, to be put in a link. Its keys
/// are the link's own — <c>r</c> the rules, <c>v</c> the view, <c>t</c> the theme, <c>s</c> the
/// state to start from — and each holds what that key of a link holds: a bundled name, a name
/// the Registry or the Gallery publishes, or the document written in place. <c>b</c> matches the
/// view to the rules where the two call a value, an input, or the values a value takes by different names. A link is a paneset written out
/// key by key, and one that names a paneset in <c>p</c> takes it as the ground its own keys go over.
/// </summary>
/// <remarks>
/// Each part is kept here as a link would carry it, a name or <c>z:</c>, so whatever reads a part
/// reads it the one way, however it arrived.
/// </remarks>
public sealed record Paneset(string? Rules, string? View, string? Theme, string? State, IReadOnlyDictionary<string, Bound> Bind)
{
    public const string Schema = "rulpane/paneset/v1";

    /// <summary>Nothing: what a link without <c>p</c> starts from.</summary>
    public static readonly Paneset Empty = new(null, null, null, null, new Dictionary<string, Bound>());

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Paneset Parse(string json)
    {
        JsonObject root = JsonNode.Parse(json, documentOptions: Lenient) as JsonObject
            ?? throw new FormatException("A paneset is a JSON object.");
        foreach (string key in root.Select(p => p.Key).Where(k => k is not ("$schema" or "r" or "v" or "t" or "s" or "b")))
        {
            throw new FormatException($"{key}: not part of a paneset. Allowed: $schema, r, v, t, s, b.");
        }

        if (root["$schema"] is not JsonValue schema || !schema.TryGetValue(out string? said) || said != Schema)
        {
            throw new FormatException($"$schema: a paneset says \"{Schema}\".");
        }

        return new Paneset(
            Part(root, "r", Source.Rules),
            Part(root, "v", Source.View),
            Part(root, "t", Source.Theme),
            root["s"] switch
            {
                null => null,
                JsonObject data => Fragment.Pack(data.ToJsonString()),
                JsonValue packed when packed.TryGetValue(out string? text) && text.StartsWith(Fragment.PackedPrefix, StringComparison.Ordinal) => text,
                _ => throw new FormatException("s: the state's data, as an object."),
            },
            root["b"] is JsonNode bind ? ReadBind(bind, "b") : new Dictionary<string, Bound>());
    }

    /// <summary>What a link's own keys say: <c>r</c>, <c>v</c>, <c>t</c> and <c>s</c> as they are, and <c>b</c> packed.</summary>
    public static Paneset FromLink(IReadOnlyDictionary<string, string> fragment)
    {
        var bind = new Dictionary<string, Bound>();
        if (fragment.TryGetValue("b", out string? packed))
        {
            if (!packed.StartsWith(Fragment.PackedPrefix, StringComparison.Ordinal))
            {
                throw new FormatException($"b: the bindings are packed, as '{Fragment.PackedPrefix}…'.");
            }

            bind = ReadBind(JsonNode.Parse(Fragment.Unpack(packed)), "b");
        }

        return new Paneset(
            fragment.GetValueOrDefault("r"),
            fragment.GetValueOrDefault("v"),
            fragment.GetValueOrDefault("t"),
            fragment.GetValueOrDefault("s"),
            bind);
    }

    /// <summary>This paneset with each part <paramref name="top"/> gives put in place of its own.</summary>
    public Paneset Over(Paneset top) => new(
        top.Rules ?? Rules,
        top.View ?? View,
        top.Theme ?? Theme,
        top.State ?? State,
        top.Bind.Count > 0 ? top.Bind : Bind);

    /// <summary><c>b</c> as a link carries it.</summary>
    public static string PackBind(IReadOnlyDictionary<string, Bound> bind) =>
        Fragment.Pack(new JsonObject([.. bind.Select(b => KeyValuePair.Create(b.Key, b.Value.Values.Count == 0
            ? (JsonNode?)b.Value.At
            : new JsonObject
            {
                ["at"] = b.Value.At,
                ["values"] = new JsonObject([.. b.Value.Values.Select(v => KeyValuePair.Create(v.Key, (JsonNode?)v.Value))]),
            }))]).ToJsonString());

    // A name, checked as the link's key would check it, or the document itself, packed as a link would carry it.
    private static string? Part(JsonObject root, string key, Func<string, Source> source) => root[key] switch
    {
        null => null,
        JsonObject document => Fragment.Pack(document.ToJsonString()),
        JsonValue value when value.TryGetValue(out string? text) => source(text).Value,
        _ => throw new FormatException($"{key}: a name, or the document itself."),
    };

    private static Dictionary<string, Bound> ReadBind(JsonNode? node, string at)
    {
        JsonObject bind = node as JsonObject ?? throw new FormatException($"{at}: a map of the names the view reads, each to where these rules keep it.");
        return bind.ToDictionary(p => p.Key, p => p.Value switch
        {
            JsonValue value when value.TryGetValue(out string? text) => new Bound(text, new Dictionary<string, string>()),
            JsonObject entry => ReadEntry(entry, $"{at}.{p.Key}"),
            _ => throw new FormatException($"{at}.{p.Key}: a binding is a string, such as \"summary.stage\", \"$board\" or an input's name, or an object of at and values."),
        });
    }

    // { "at": "$board", "values": { "x": "black" } }: where the value is, and what the view calls each of the rules' values.
    private static Bound ReadEntry(JsonObject entry, string at)
    {
        foreach (string key in entry.Select(p => p.Key).Where(k => k is not ("at" or "values")))
        {
            throw new FormatException($"{at}.{key}: not part of a binding. Allowed: at, values.");
        }

        string where = entry["at"] is JsonValue value && value.TryGetValue(out string? text)
            ? text
            : throw new FormatException($"{at}.at: where these rules keep the value, such as \"summary.stage\" or \"$board\".");
        JsonObject values = entry["values"] as JsonObject
            ?? throw new FormatException($"{at}.values: a map of the rules' values, each to the one the view gives a look to.");
        return new Bound(where, values.ToDictionary(v => v.Key, v =>
            v.Value is JsonValue own && own.TryGetValue(out string? name)
                ? name
                : throw new FormatException($"{at}.values.{v.Key}: the view's value is a string.")));
    }
}

/// <summary>
/// What a paneset's <c>b</c> says of one name the view reads: <see cref="At"/>, where these rules
/// keep it — a projection, a path into one, a state field, or one of their inputs — and for a
/// value, <see cref="Values"/>: each of the rules' values, as the value the view gives a look to
/// (a tone or a piece). The screen still shows the rules' value; only its look is the view's.
/// </summary>
public sealed record Bound(string At, IReadOnlyDictionary<string, string> Values);
