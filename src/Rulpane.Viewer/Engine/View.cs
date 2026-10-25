using System.Text.Json;
using System.Text.Json.Nodes;
using Rulealize;

namespace Rulpane.Viewer.Engine;

/// <summary>
/// A view document, <c>rulealize/view/v1</c>: which values go where on the screen and how
/// they look. It holds bindings and nothing that computes — a condition is a projection the
/// rules state, which the view only reads — so a screen never gives a second account of the rules.
/// </summary>
public sealed record View(string? Title, IReadOnlyList<Block> Layout)
{
    public const string Schema = "rulealize/view/v1";

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads a view and checks it against the rules it will show: every binding names a
    /// projection or a state field they have, and every input it lists is one they offer.
    /// <paramref name="rules"/> is the rule set's document, which <paramref name="context"/>
    /// was compiled from: a board's size is in its state schema, and nowhere else.
    /// </summary>
    public static View Parse(string json, RuleContext context, string rules)
    {
        JsonObject root = JsonNode.Parse(json, documentOptions: Lenient) as JsonObject
            ?? throw new FormatException("A view is a JSON object.");

        var reader = new Reader(context, JsonNode.Parse(rules, documentOptions: Lenient));
        reader.Keys(root, "", "$schema", "ruleSet", "title", "layout");
        if (Reader.String(root, "", "$schema") != Schema)
        {
            throw new FormatException($"$schema: a view says \"{Schema}\".");
        }

        if (Reader.OptionalString(root, "", "ruleSet") is string ruleSet)
        {
            reader.RuleSet(ruleSet);
        }

        JsonArray layout = Reader.Array(root, "", "layout");
        if (layout.Count == 0)
        {
            throw new FormatException("layout: a view shows at least one thing.");
        }

        return new View(
            Reader.OptionalString(root, "", "title"),
            layout.Select((b, i) => reader.Block(b, $"layout[{i}]")).ToList());
    }

    /// <summary>Takes the view apart and puts each piece through the checks <see cref="Parse"/> promises.</summary>
    private sealed class Reader(RuleContext context, JsonNode? rules)
    {
        private readonly HashSet<string> projections = [.. context.Projections];
        private readonly HashSet<string> inputs = [.. context.Inputs];
        private readonly JsonObject state = JsonNode.Parse(context.InitialState)?["data"] as JsonObject ?? [];

        // "approval", "approval@1" or "approval@1.0.0": the id, then as much of the version as is pinned.
        public void RuleSet(string value)
        {
            string[] parts = value.Split('@', 2);
            bool versionMatches = parts.Length == 1
                || context.Version.Split('.').Take(parts[1].Split('.').Length).SequenceEqual(parts[1].Split('.'));
            if (parts[0] != context.Id || !versionMatches)
            {
                throw new FormatException($"ruleSet: the view is for {value}, and these rules are {context.RuleSet}.");
            }
        }

        public Block Block(JsonNode? node, string at)
        {
            JsonObject block = node as JsonObject ?? throw new FormatException($"{at}: a block is an object.");
            string kind = String(block, at, "c");
            Binding? showWhen = OptionalBinding(block, at, "showWhen");
            switch (kind)
            {
                case "heading":
                    Keys(block, at, "c", "showWhen", "text");
                    return new Heading(String(block, at, "text"), showWhen);
                case "text":
                    Keys(block, at, "c", "showWhen", "text");
                    return new Paragraph(String(block, at, "text"), showWhen);
                case "fields":
                    Keys(block, at, "c", "showWhen", "title", "rows");
                    return new Fields(
                        OptionalString(block, at, "title"),
                        Each(block, at, "rows", ReadRow),
                        showWhen);
                case "stats":
                    Keys(block, at, "c", "showWhen", "items");
                    return new Stats(Each(block, at, "items", ReadStat), showWhen);
                case "table":
                    Keys(block, at, "c", "showWhen", "title", "bind", "columns");
                    return new Table(
                        OptionalString(block, at, "title"),
                        RequiredBinding(block, at, "bind"),
                        block.ContainsKey("columns") ? Each(block, at, "columns", ReadColumn) : null,
                        showWhen);
                case "actions":
                    Keys(block, at, "c", "showWhen", "title", "inputs");
                    return new Actions(OptionalString(block, at, "title"), Inputs(block, at), showWhen);
                case "board":
                    Keys(block, at, "c", "showWhen", "title", "bind", "play", "pieces");
                    Binding board = RequiredBinding(block, at, "bind");
                    return new Board(
                        OptionalString(block, at, "title"),
                        board,
                        Shape(board, $"{at}.bind"),
                        OptionalString(block, at, "play") is string play ? Play(play, $"{at}.play") : null,
                        Pieces(block, at),
                        showWhen);
                default:
                    throw new FormatException($"{at}.c: there is no block called \"{kind}\". A view has heading, text, fields, stats, table, board and actions.");
            }
        }

        private Row ReadRow(JsonNode? node, string at)
        {
            JsonObject row = node as JsonObject ?? throw new FormatException($"{at}: a row is an object.");
            Keys(row, at, "label", "bind", "tone", "showWhen");
            Dictionary<string, Tone>? tones = null;
            if (row["tone"] is JsonNode toneNode)
            {
                JsonObject tone = toneNode as JsonObject ?? throw new FormatException($"{at}.tone: a tone maps values to tones.");
                tones = tone.ToDictionary(t => t.Key, t => ReadTone(t.Value, $"{at}.tone.{t.Key}"));
            }

            return new Row(String(row, at, "label"), RequiredBinding(row, at, "bind"), tones, OptionalBinding(row, at, "showWhen"));
        }

        private Stat ReadStat(JsonNode? node, string at)
        {
            JsonObject stat = node as JsonObject ?? throw new FormatException($"{at}: a stat is an object.");
            Keys(stat, at, "label", "bind");
            return new Stat(String(stat, at, "label"), RequiredBinding(stat, at, "bind"));
        }

        // A column is its key, or the key with a label to head it.
        private Column ReadColumn(JsonNode? node, string at)
        {
            if (node is JsonValue value && value.TryGetValue(out string? key))
            {
                return new Column(key, key);
            }

            JsonObject column = node as JsonObject ?? throw new FormatException($"{at}: a column is a key or an object.");
            Keys(column, at, "key", "label");
            string k = String(column, at, "key");
            return new Column(k, OptionalString(column, at, "label") ?? k);
        }

        // Only a state field can be drawn as a board: the rules give its size nowhere but in its schema.
        private BoardShape Shape(Binding binding, string at) =>
            binding.FromState && binding.Path.Count == 0 && BoardShape.Of(rules?["state"]?["schema"]?[binding.Root]) is BoardShape shape
                ? shape
                : throw new FormatException($"{at}: a board binds a state field the rules declare with grid.board, such as \"$board\".");

        // "place.at": the input played by choosing a square, and its parameter that takes the square.
        private SquareMove Play(string text, string at)
        {
            string[] parts = text.Split('.');
            if (parts.Length != 2 || parts.Any(p => p.Length == 0))
            {
                throw new FormatException($"{at}: \"{text}\" is not an input and a parameter, such as \"place.at\".");
            }

            if (!inputs.Contains(parts[0]))
            {
                throw new FormatException($"{at}: the rules have no input \"{parts[0]}\". They have {string.Join(", ", inputs)}.");
            }

            // An input the rules write themselves names its parameters; one they take from elsewhere is not checked here.
            if (rules?["inputs"]?[parts[0]]?["params"] is JsonObject parameters && !parameters.ContainsKey(parts[1]))
            {
                throw new FormatException($"{at}: the input \"{parts[0]}\" has no parameter \"{parts[1]}\". It has {(parameters.Count == 0 ? "none" : string.Join(", ", parameters.Select(p => p.Key)))}.");
            }

            return new SquareMove(parts[0], parts[1]);
        }

        private static Dictionary<string, Piece>? Pieces(JsonObject block, string at)
        {
            if (block["pieces"] is not JsonNode node)
            {
                return null;
            }

            JsonObject pieces = node as JsonObject ?? throw new FormatException($"{at}.pieces: pieces map values to pieces.");
            return pieces.ToDictionary(p => p.Key, p =>
                p.Value is JsonValue value && value.TryGetValue(out string? text) && Enum.GetNames<Piece>().Contains(text)
                    ? Enum.Parse<Piece>(text)
                    : throw new FormatException($"{at}.pieces.{p.Key}: a piece is dark, light, accent, ok, warn or danger."));
        }

        // "*" is every input; a list names the ones this block offers.
        private IReadOnlyList<string>? Inputs(JsonObject block, string at)
        {
            if (block["inputs"] is JsonValue all && all.TryGetValue(out string? star) && star == "*")
            {
                return null;
            }

            var names = Each(block, at, "inputs", (n, a) =>
                n is JsonValue v && v.TryGetValue(out string? name) ? name : throw new FormatException($"{a}: an input is named by a string."));
            foreach (var (name, i) in names.Select((n, i) => (n, i)))
            {
                if (!inputs.Contains(name))
                {
                    throw new FormatException($"{at}.inputs[{i}]: the rules have no input \"{name}\". They have {string.Join(", ", inputs)}.");
                }
            }

            return names;
        }

        private static Tone ReadTone(JsonNode? node, string at) =>
            node is JsonValue value && value.TryGetValue(out string? text) && Enum.GetNames<Tone>().Contains(text)
                ? Enum.Parse<Tone>(text)
                : throw new FormatException($"{at}: a tone is neutral, accent, ok, warn or danger.");

        private Binding RequiredBinding(JsonObject parent, string at, string key) =>
            OptionalBinding(parent, at, key) ?? throw new FormatException($"{at}.{key}: missing.");

        private Binding? OptionalBinding(JsonObject parent, string at, string key)
        {
            if (OptionalString(parent, at, key) is not string text)
            {
                return null;
            }

            Binding binding = Binding.Parse(text, $"{at}.{key}");
            bool known = binding.FromState ? state.ContainsKey(binding.Root) : projections.Contains(binding.Root);
            if (!known)
            {
                throw new FormatException(binding.FromState
                    ? $"{at}.{key}: the state has no field \"{binding.Root}\"."
                    : $"{at}.{key}: the rules project no \"{binding.Root}\". {(projections.Count == 0 ? "They project nothing; bind to the state with $." : $"They project {string.Join(", ", projections)}.")}");
            }

            return binding;
        }

        public void Keys(JsonObject node, string at, params string[] allowed)
        {
            foreach (string key in node.Select(p => p.Key))
            {
                if (!allowed.Contains(key))
                {
                    throw new FormatException($"{Join(at, key)}: not part of a view here. Allowed: {string.Join(", ", allowed)}.");
                }
            }
        }

        private static List<T> Each<T>(JsonObject parent, string at, string key, Func<JsonNode?, string, T> read) =>
            Array(parent, at, key).Select((n, i) => read(n, $"{Join(at, key)}[{i}]")).ToList();

        public static JsonArray Array(JsonObject parent, string at, string key) =>
            parent[key] as JsonArray ?? throw new FormatException($"{Join(at, key)}: a list is expected.");

        public static string String(JsonObject parent, string at, string key) =>
            OptionalString(parent, at, key) ?? throw new FormatException($"{Join(at, key)}: missing.");

        public static string? OptionalString(JsonObject parent, string at, string key) => parent[key] switch
        {
            null => null,
            JsonValue value when value.TryGetValue(out string? text) => text,
            _ => throw new FormatException($"{Join(at, key)}: a string is expected."),
        };

        private static string Join(string at, string key) => at.Length == 0 ? key : $"{at}.{key}";
    }
}

/// <summary>How a value is marked: the chip colours of the design.</summary>
public enum Tone { neutral, accent, ok, warn, danger }

/// <summary>One piece of a layout. Shown only while <see cref="ShowWhen"/>, if it has one, reads <c>true</c>.</summary>
public abstract record Block(Binding? ShowWhen);

public sealed record Heading(string Text, Binding? ShowWhen) : Block(ShowWhen);

public sealed record Paragraph(string Body, Binding? ShowWhen) : Block(ShowWhen);

/// <summary>A card of labelled values. A row with a tone shows its value as a chip of that tone.</summary>
public sealed record Fields(string? Title, IReadOnlyList<Row> Rows, Binding? ShowWhen) : Block(ShowWhen);

public sealed record Row(string Label, Binding Bind, IReadOnlyDictionary<string, Tone>? Tones, Binding? ShowWhen);

/// <summary>Values set large, side by side.</summary>
public sealed record Stats(IReadOnlyList<Stat> Items, Binding? ShowWhen) : Block(ShowWhen);

public sealed record Stat(string Label, Binding Bind);

/// <summary>A list of records, one per line. Without columns, every key any of them has.</summary>
public sealed record Table(string? Title, Binding Bind, IReadOnlyList<Column>? Columns, Binding? ShowWhen) : Block(ShowWhen);

public sealed record Column(string Key, string Label);

/// <summary>
/// A Grid board, square by square. With <see cref="Play"/>, a square that input can be played
/// on is a button; a value in <see cref="Pieces"/> is drawn as that piece, and any other as text.
/// </summary>
public sealed record Board(
    string? Title,
    Binding Bind,
    BoardShape Shape,
    SquareMove? Play,
    IReadOnlyDictionary<string, Piece>? Pieces,
    Binding? ShowWhen) : Block(ShowWhen);

/// <summary>The input a square plays, and the parameter the square goes in.</summary>
public sealed record SquareMove(string Input, string Parameter);

/// <summary>How a value on a board is drawn: a dark or a light disc, or one in a tone's colour.</summary>
public enum Piece { dark, light, accent, ok, warn, danger }

/// <summary>The moves of the inputs named, or of every input when <see cref="Inputs"/> is null.</summary>
public sealed record Actions(string? Title, IReadOnlyList<string>? Inputs, Binding? ShowWhen) : Block(ShowWhen);

/// <summary>
/// Where a value comes from: <c>summary.stage</c> is the field <c>stage</c> of the projection
/// <c>summary</c>, and <c>$total</c> the state field <c>total</c> — the way the rules themselves
/// write a state read. A number steps into a list.
/// </summary>
public sealed record Binding(bool FromState, string Root, IReadOnlyList<string> Path)
{
    public static Binding Parse(string text, string at)
    {
        bool fromState = text.StartsWith('$');
        string[] parts = (fromState ? text[1..] : text).Split('.');
        if (parts.Any(p => p.Length == 0))
        {
            throw new FormatException($"{at}: \"{text}\" is not a binding. A binding is a projection or a $state field, then .names to step in.");
        }

        return new Binding(fromState, parts[0], parts[1..]);
    }

    /// <summary>The value at this binding; null where the path leads nowhere.</summary>
    public JsonNode? Read(Func<string, JsonNode?> projection, JsonNode? data)
    {
        JsonNode? node = FromState ? data?[Root] : projection(Root);
        foreach (string step in Path)
        {
            node = node switch
            {
                JsonObject record => record[step],
                JsonArray list when int.TryParse(step, out int i) && i >= 0 && i < list.Count => list[i],
                _ => null,
            };
        }

        return node;
    }

    public override string ToString() => (FromState ? "$" : "") + string.Join('.', [Root, .. Path]);
}
