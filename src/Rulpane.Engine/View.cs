using System.Text.Json;
using System.Text.Json.Nodes;
using Rulealize;

namespace Rulpane.Engine;

/// <summary>
/// A view document, <c>rulealize/view/v1</c>: which values go where on the screen and how
/// they look. It holds bindings and nothing that computes — a condition is a projection the
/// rules state, which the view only reads — so a screen never gives a second account of the rules.
/// </summary>
/// <remarks>
/// Every view reads names: the projections and state fields it binds, and the inputs it offers.
/// One may say in <c>expects</c> which projections and inputs it reads, with a type for each;
/// such a view reads nothing else, names no rule set, and has its values checked against those
/// types at every position. Rules that keep what a view reads under other names are matched to
/// it by the paneset's <c>b</c>, which this is handed as a map, never by the view itself.
/// </remarks>
public sealed record View(string? Title, IReadOnlyList<Block> Layout, Names Names, IReadOnlyList<Expected> Expected)
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
    /// <paramref name="bind"/> is the paneset's <c>b</c>: for a name the view reads, where these
    /// rules keep it — a projection, a path into one, or a state field. Each must be one the view reads.
    /// </summary>
    public static View Parse(string json, RuleContext context, string rules, IReadOnlyDictionary<string, string>? bind = null)
    {
        JsonObject root = JsonNode.Parse(json, documentOptions: Lenient) as JsonObject
            ?? throw new FormatException("A view is a JSON object.");

        var reader = new Reader(context, JsonNode.Parse(rules, documentOptions: Lenient), bind ?? new Dictionary<string, string>());
        View view;
        if (root.ContainsKey("expects"))
        {
            // What it reads is all in expects, so it names no rule set and reads no state; its
            // names are for whatever rules it is used with, and are checked against none of them.
            Reader.Keys(root, "", "$schema", "title", "expects", "layout", "names");
            Header(root, reader);
            reader.Expect(root["expects"]);
            view = new View(
                Reader.OptionalString(root, "", "title"),
                ReadLayout(root, reader),
                root["names"] is JsonNode names ? Reader.ReadUncheckedNames(names) : Names.None,
                reader.Expected);
        }
        else
        {
            Reader.Keys(root, "", "$schema", "ruleSet", "title", "layout", "names");
            Header(root, reader);
            view = new View(
                Reader.OptionalString(root, "", "title"),
                ReadLayout(root, reader),
                root["names"] is JsonNode names ? reader.ReadNames(names) : Names.None,
                []);
        }

        reader.AllBound();
        return view;
    }

    private static void Header(JsonObject root, Reader reader)
    {
        if (Reader.String(root, "", "$schema") != Schema)
        {
            throw new FormatException($"$schema: a view says \"{Schema}\".");
        }

        if (Reader.OptionalString(root, "", "ruleSet") is string ruleSet)
        {
            reader.RuleSet(ruleSet);
        }
    }

    private static List<Block> ReadLayout(JsonObject root, Reader reader)
    {
        JsonArray layout = Reader.Array(root, "", "layout");
        if (layout.Count == 0)
        {
            throw new FormatException("layout: a view shows at least one thing.");
        }

        return layout.Select((b, i) => reader.Block(b, $"layout[{i}]")).ToList();
    }

    /// <summary>
    /// Throws when a value the rules give at this position is not the kind the view said it
    /// expects: a view that bound a list and was handed a number would show nonsense instead.
    /// </summary>
    public void Check(Func<string, JsonNode?> projection, JsonNode? data)
    {
        foreach (Expected expected in Expected)
        {
            JsonNode? value = expected.Bind.Read(projection, data);
            if (!expected.Type.Matches(value))
            {
                string shown = value?.ToJsonString() ?? "null";
                throw new FormatException($"expects.projections.{expected.Name}: the view expects {expected.Type}, and the rules give {(shown.Length > 80 ? shown[..80] + "…" : shown)} (at {expected.Bind}).");
            }
        }
    }

    /// <summary>Takes the view apart and puts each piece through the checks <see cref="Parse"/> promises.</summary>
    private sealed class Reader(RuleContext context, JsonNode? rules, IReadOnlyDictionary<string, string> bind)
    {
        private readonly HashSet<string> projections = [.. context.Projections];
        private readonly HashSet<string> inputs = [.. context.Inputs];
        private readonly JsonObject state = JsonNode.Parse(context.InitialState)?["data"] as JsonObject ?? [];

        // Set while a view with expects is read: what it expects, and where these rules keep each of those.
        private Dictionary<string, Expectation>? expectedProjections;
        private Dictionary<string, List<string>>? expectedInputs;
        private readonly Dictionary<string, Binding> map = new(StringComparer.Ordinal);

        // The paneset's b, each held to the rules, and which of them the view has read.
        private Dictionary<string, Binding>? given;
        private readonly HashSet<string> used = new(StringComparer.Ordinal);

        private Dictionary<string, Binding> Given => given ??= bind.ToDictionary(
            b => b.Key,
            b => b.Key.Length == 0 || b.Key.Contains('.') || b.Key.StartsWith('$')
                ? throw new FormatException($"b.{b.Key}: what is bound is a name the view reads, such as \"stage\".")
                : RuleBinding(b.Value, $"b.{b.Key}"),
            StringComparer.Ordinal);

        /// <summary>Throws for a name in <c>b</c> the view never read: it would bind nothing, and was most likely misspelt.</summary>
        public void AllBound()
        {
            foreach (string name in Given.Keys.Where(n => !used.Contains(n)))
            {
                throw new FormatException(expectedProjections is null
                    ? $"b.{name}: the view reads no \"{name}\"."
                    : $"b.{name}: the view expects no \"{name}\". It expects {(expectedProjections.Count == 0 ? "no projections" : string.Join(", ", expectedProjections.Keys))}.");
            }
        }

        public List<Expected> Expected { get; } = [];

        /// <summary>
        /// Reads <c>expects</c> and holds it against the rules: each projection is one they have,
        /// or one the paneset's <c>b</c> finds for it elsewhere, and each input is one of theirs.
        /// </summary>
        public void Expect(JsonNode? node)
        {
            JsonObject expects = node as JsonObject ?? throw new FormatException("expects: an object of the projections and inputs the view reads.");
            Keys(expects, "expects", "projections", "inputs");

            JsonObject wanted = expects["projections"] as JsonObject ?? (expects.ContainsKey("projections")
                ? throw new FormatException("expects.projections: a map of projections, each to its type.")
                : []);
            expectedProjections = wanted.ToDictionary(p => p.Key, p => Expectation.Parse(
                OptionalString(wanted, "expects.projections", p.Key) ?? throw new FormatException($"expects.projections.{p.Key}: a type is a string."),
                $"expects.projections.{p.Key}"), StringComparer.Ordinal);

            JsonObject moves = expects["inputs"] as JsonObject ?? (expects.ContainsKey("inputs")
                ? throw new FormatException("expects.inputs: a map of inputs, each to the parameters the view uses.")
                : []);
            expectedInputs = moves.ToDictionary(i => i.Key, i => Each(moves, "expects.inputs", i.Key, (n, a) =>
                n is JsonValue v && v.TryGetValue(out string? name) ? name : throw new FormatException($"{a}: a parameter is named by a string.")), StringComparer.Ordinal);

            foreach (var (name, type) in expectedProjections)
            {
                if (Given.TryGetValue(name, out Binding? found))
                {
                    used.Add(name);
                }
                else if (projections.Contains(name))
                {
                    found = new Binding(false, name, []);
                }
                else
                {
                    throw new FormatException($"expects.projections.{name}: the rules project no \"{name}\". {(projections.Count == 0 ? "They project nothing" : $"They project {string.Join(", ", projections)}")}; the paneset's b can bind it to what they have.");
                }

                map[name] = found;
                if (type.Kind == Kind.Board && BoardShapeOf(found) is null)
                {
                    throw new FormatException($"expects.projections.{name}: a board is a state field the rules declare with grid.board, which no projection can be; the paneset's b binds it to one, such as \"$board\".");
                }

                Expected.Add(new Expected(name, found, type));
            }

            foreach (var (input, parameters) in expectedInputs)
            {
                if (!inputs.Contains(input))
                {
                    throw new FormatException($"expects.inputs.{input}: the rules have no input \"{input}\". They have {string.Join(", ", inputs)}.");
                }

                foreach (string parameter in parameters)
                {
                    CheckParameter(input, parameter, $"expects.inputs.{input}");
                }
            }
        }

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

        // Inputs and parameters are the rules' own and are checked against them; the codes come
        // from the plugins and the values from the state, so neither can be listed in advance.
        public Names ReadNames(JsonNode node)
        {
            JsonObject names = node as JsonObject ?? throw new FormatException("names: names is an object of inputs, params, codes and values.");
            Keys(names, "names", "inputs", "params", "codes", "values");
            var given = Map(names, "inputs");
            foreach (string input in given.Keys.Where(i => !inputs.Contains(i)))
            {
                throw new FormatException($"names.inputs.{input}: the rules have no input \"{input}\". They have {string.Join(", ", inputs)}.");
            }

            var parameters = Map(names, "params");
            // A parameter can be checked only when every input is one the rules write themselves.
            if (rules?["inputs"] is JsonObject written && inputs.All(written.ContainsKey))
            {
                var declared = written.SelectMany(i => i.Value?["params"] as JsonObject ?? []).Select(p => p.Key).ToHashSet();
                foreach (string parameter in parameters.Keys.Where(p => !declared.Contains(p)))
                {
                    throw new FormatException($"names.params.{parameter}: no input of the rules has a parameter \"{parameter}\". They have {(declared.Count == 0 ? "none" : string.Join(", ", declared))}.");
                }
            }

            return new Names(given, parameters, Map(names, "codes"), Map(names, "values"));
        }

        public static Names ReadUncheckedNames(JsonNode node)
        {
            JsonObject names = node as JsonObject ?? throw new FormatException("names: names is an object of inputs, params, codes and values.");
            Keys(names, "names", "inputs", "params", "codes", "values");
            return new Names(Map(names, "inputs"), Map(names, "params"), Map(names, "codes"), Map(names, "values"));
        }

        private static Dictionary<string, string> Map(JsonObject names, string key)
        {
            if (names[key] is not JsonNode node)
            {
                return [];
            }

            JsonObject map = node as JsonObject ?? throw new FormatException($"names.{key}: a map of names, each to what the screen calls it.");
            return map.ToDictionary(p => p.Key, p => OptionalString(map, $"names.{key}", p.Key)
                ?? throw new FormatException($"names.{key}.{p.Key}: a string is expected."));
        }

        public Block Block(JsonNode? node, string at)
        {
            JsonObject block = node as JsonObject ?? throw new FormatException($"{at}: a block is an object.");
            string kind = String(block, at, "c");
            Binding? showWhen = OptionalBinding(block, at, "showWhen", Kind.Bool);
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
                case "list":
                    Keys(block, at, "c", "showWhen", "title", "bind");
                    return new ValueList(OptionalString(block, at, "title"), RequiredBinding(block, at, "bind", Kind.List), showWhen);
                case "table":
                    Keys(block, at, "c", "showWhen", "title", "bind", "columns");
                    return new Table(
                        OptionalString(block, at, "title"),
                        RequiredBinding(block, at, "bind", Kind.List),
                        block.ContainsKey("columns") ? Each(block, at, "columns", ReadColumn) : null,
                        showWhen);
                case "actions":
                    Keys(block, at, "c", "showWhen", "title", "inputs", "as");
                    return new Actions(OptionalString(block, at, "title"), Inputs(block, at), OptionalString(block, at, "as") switch
                    {
                        null or "buttons" => false,
                        "select" => true,
                        string other => throw new FormatException($"{at}.as: \"{other}\" is not a way to offer moves. It is buttons or select."),
                    }, showWhen);
                case "board":
                    Keys(block, at, "c", "showWhen", "title", "bind", "play", "pieces");
                    Binding board = RequiredBinding(block, at, "bind", Kind.Board);
                    return new Board(
                        OptionalString(block, at, "title"),
                        board,
                        BoardShapeOf(board) ?? throw new FormatException($"{at}.bind: a board binds a state field the rules declare with grid.board, such as \"$board\"."),
                        OptionalString(block, at, "play") is string play ? Play(play, $"{at}.play") : null,
                        Pieces(block, at),
                        showWhen);
                default:
                    throw new FormatException($"{at}.c: there is no block called \"{kind}\". A view has heading, text, fields, stats, list, table, board and actions.");
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

            return new Row(String(row, at, "label"), RequiredBinding(row, at, "bind"), tones, OptionalBinding(row, at, "showWhen", Kind.Bool));
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

        // Only a state field can be drawn as a board: the rules give its size nowhere but in its
        // schema, and a board has no form a projection could give it in.
        private BoardShape? BoardShapeOf(Binding binding) =>
            binding.FromState && binding.Path.Count == 0 ? BoardShape.Of(rules?["state"]?["schema"]?[binding.Root]) : null;

        // "place.at": the input played by choosing a square, and its parameter that takes the square.
        private SquareMove Play(string text, string at)
        {
            string[] parts = text.Split('.');
            if (parts.Length != 2 || parts.Any(p => p.Length == 0))
            {
                throw new FormatException($"{at}: \"{text}\" is not an input and a parameter, such as \"place.at\".");
            }

            Input(parts[0], at);
            if (expectedInputs is not null && !expectedInputs[parts[0]].Contains(parts[1]))
            {
                throw new FormatException($"{at}: the view expects no parameter \"{parts[1]}\" of \"{parts[0]}\". Add it to expects.inputs.{parts[0]}.");
            }

            CheckParameter(parts[0], parts[1], at);
            return new SquareMove(parts[0], parts[1]);
        }

        // An input a view with expects names is one it expects, and every input named is one the rules have.
        private void Input(string name, string at)
        {
            if (expectedInputs is not null && !expectedInputs.ContainsKey(name))
            {
                throw new FormatException($"{at}: the view expects no input \"{name}\". {(expectedInputs.Count == 0 ? "It expects none" : $"It expects {string.Join(", ", expectedInputs.Keys)}")}; name it in expects.inputs.");
            }

            if (!inputs.Contains(name))
            {
                throw new FormatException($"{at}: the rules have no input \"{name}\". They have {string.Join(", ", inputs)}.");
            }
        }

        // An input the rules write themselves names its parameters; one they take from elsewhere is not checked here.
        private void CheckParameter(string input, string parameter, string at)
        {
            if (rules?["inputs"]?[input]?["params"] is JsonObject parameters && !parameters.ContainsKey(parameter))
            {
                throw new FormatException($"{at}: the input \"{input}\" has no parameter \"{parameter}\". It has {(parameters.Count == 0 ? "none" : string.Join(", ", parameters.Select(p => p.Key)))}.");
            }
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
                Input(name, $"{at}.inputs[{i}]");
            }

            return names;
        }

        private static Tone ReadTone(JsonNode? node, string at) =>
            node is JsonValue value && value.TryGetValue(out string? text) && Enum.GetNames<Tone>().Contains(text)
                ? Enum.Parse<Tone>(text)
                : throw new FormatException($"{at}: a tone is neutral, accent, ok, warn or danger.");

        private Binding RequiredBinding(JsonObject parent, string at, string key, params Kind[] wants) =>
            OptionalBinding(parent, at, key, wants) ?? throw new FormatException($"{at}.{key}: missing.");

        /// <summary>
        /// A binding of the view, as the rules will be read through it. In a view with expects it names a
        /// projection it expects, of a kind <paramref name="wants"/> allows when it lists
        /// any, and is then read wherever these rules keep that projection.
        /// </summary>
        private Binding? OptionalBinding(JsonObject parent, string at, string key, params Kind[] wants)
        {
            if (OptionalString(parent, at, key) is not string text)
            {
                return null;
            }

            return expectedProjections is null
                ? RuleBinding(parent, at, key)
                : Resolve(Binding.Parse(text, $"{at}.{key}"), $"{at}.{key}", wants);
        }

        private Binding Resolve(Binding binding, string at, Kind[] wants)
        {
            if (binding.FromState)
            {
                throw new FormatException($"{at}: a view with expects reads no state, only the projections it expects. Expect one for \"{binding.Root}\" instead.");
            }

            if (!expectedProjections!.TryGetValue(binding.Root, out Expectation? type))
            {
                throw new FormatException($"{at}: the view expects no \"{binding.Root}\". {(expectedProjections.Count == 0 ? "It expects no projections" : $"It expects {string.Join(", ", expectedProjections.Keys)}")}; add it to expects.projections.");
            }

            foreach (string step in binding.Path)
            {
                type = type.Step(step)
                    ?? throw new FormatException($"{at}: \"{binding}\" steps to \"{step}\", and the view expects {binding.Root} to be {expectedProjections[binding.Root]}.");
            }

            // A board's size is in the rules' schema, so "any" is not enough for one; for anything else it is.
            bool fits = wants.Length == 0 || wants.Contains(type.Kind) || type.Kind == Kind.Any && !wants.Contains(Kind.Board);
            if (!fits)
            {
                throw new FormatException($"{at}: \"{binding}\" is {type} in what the view expects, and this needs {string.Join(" or ", wants.Select(w => w.ToString().ToLowerInvariant()))}.");
            }

            Binding found = map[binding.Root];
            return found with { Path = [.. found.Path, .. binding.Path] };
        }

        // A binding of the view's own, in a view without expects: a name the paneset's b binds is
        // read where b says, and any other is the rules' own projection or state field.
        private Binding RuleBinding(JsonObject parent, string at, string key)
        {
            string text = OptionalString(parent, at, key) ?? throw new FormatException($"{Join(at, key)}: missing.");
            Binding binding = Binding.Parse(text, Join(at, key));
            if (!binding.FromState && Given.TryGetValue(binding.Root, out Binding? bound))
            {
                used.Add(binding.Root);
                return bound with { Path = [.. bound.Path, .. binding.Path] };
            }

            return Known(binding, Join(at, key));
        }

        // A binding of these rules, as b gives one.
        private Binding RuleBinding(string text, string at) => Known(Binding.Parse(text, at), at);

        // Its first name is a projection or a state field these rules have.
        private Binding Known(Binding binding, string at)
        {
            bool known = binding.FromState ? state.ContainsKey(binding.Root) : projections.Contains(binding.Root);
            if (!known)
            {
                throw new FormatException(binding.FromState
                    ? $"{at}: the state has no field \"{binding.Root}\"."
                    : $"{at}: the rules project no \"{binding.Root}\". {(projections.Count == 0 ? "They project nothing; bind to the state with $." : $"They project {string.Join(", ", projections)}.")}");
            }

            return binding;
        }

        public static void Keys(JsonObject node, string at, params string[] allowed)
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

/// <summary>A projection a view expects, where these rules keep it, and the type it was promised to have.</summary>
public sealed record Expected(string Name, Binding Bind, Expectation Type);

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

/// <summary>The values of a list, side by side: what a table would show with no columns to put them in.</summary>
public sealed record ValueList(string? Title, Binding Bind, Binding? ShowWhen) : Block(ShowWhen);

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
    Binding? ShowWhen) : Block(ShowWhen)
{
    /// <summary>
    /// The moves played by choosing a square, by square. A square two moves share would need a
    /// choice the board cannot offer, so it is left out, and those moves stay with the actions.
    /// </summary>
    public IReadOnlyDictionary<string, ValidInput> SquareMoves(IEnumerable<ValidInput>? moves)
    {
        if (Play is not SquareMove play || moves is null)
        {
            return new Dictionary<string, ValidInput>();
        }

        return moves
            .Select(m => (Move: m, At: m.Input == play.Input && m.IsComplete && m.Arguments.TryGetValue(play.Parameter, out string? at) ? Display.Unquote(at) : null))
            .Where(m => m.At is not null)
            .GroupBy(m => m.At!)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Move);
    }
}

/// <summary>The input a square plays, and the parameter the square goes in.</summary>
public sealed record SquareMove(string Input, string Parameter);

/// <summary>How a value on a board is drawn: a dark or a light disc, or one in a tone's colour.</summary>
public enum Piece { dark, light, accent, ok, warn, danger }

/// <summary>
/// The moves of the inputs named, or of every input when <see cref="Inputs"/> is null. With
/// <see cref="Select"/>, the choices of each input are a list to pick from rather than a button each.
/// </summary>
public sealed record Actions(string? Title, IReadOnlyList<string>? Inputs, bool Select, Binding? ShowWhen) : Block(ShowWhen);

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
