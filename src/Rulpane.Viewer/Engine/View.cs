using System.Text.Json;
using System.Text.Json.Nodes;
using Rulealize;

namespace Rulpane.Viewer.Engine;

/// <summary>
/// A view document, <c>rulealize/view/v1</c>: which values go where on the screen and how
/// they look. It holds bindings and nothing that computes — a condition is a projection the
/// rules state, which the view only reads — so a screen never gives a second account of the rules.
/// </summary>
/// <remarks>
/// A view comes in three forms. One for a rule set has a <c>layout</c> and binds what those
/// rules have. A template has a <c>layout</c> and <c>expects</c>: the projections and inputs it
/// needs, and nothing else of any rules, so it is written once for every rule set that has them.
/// A view that applies a template names it in <c>template</c> and says, in <c>bind</c>, where
/// these rules keep each projection it expects under another name.
/// </remarks>
public sealed record View(string? Title, IReadOnlyList<Block> Layout, Names Names, IReadOnlyList<Expected> Expected)
{
    public const string Schema = "rulealize/view/v1";

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The bundled template a view applies, which has to be read before it can be parsed; null for any other view.</summary>
    public static string? TemplateOf(string json)
    {
        JsonObject root = JsonNode.Parse(json, documentOptions: Lenient) as JsonObject
            ?? throw new FormatException("A view is a JSON object.");
        return Reader.OptionalString(root, "", "template") is string name
            ? Source.View(name) is { IsBundled: true } ? name : throw new FormatException("template: a template is named by one bundled with Rulpane.")
            : null;
    }

    /// <summary>
    /// Reads a view and checks it against the rules it will show: every binding names a
    /// projection or a state field they have, and every input it lists is one they offer.
    /// <paramref name="rules"/> is the rule set's document, which <paramref name="context"/>
    /// was compiled from: a board's size is in its state schema, and nowhere else.
    /// <paramref name="template"/> is the document of the template the view names in
    /// <c>template</c>, as <see cref="TemplateOf"/> gives it.
    /// </summary>
    public static View Parse(string json, RuleContext context, string rules, string? template = null)
    {
        JsonObject root = JsonNode.Parse(json, documentOptions: Lenient) as JsonObject
            ?? throw new FormatException("A view is a JSON object.");

        var reader = new Reader(context, JsonNode.Parse(rules, documentOptions: Lenient));
        if (root.ContainsKey("template"))
        {
            Reader.Keys(root, "", "$schema", "ruleSet", "template", "bind", "title", "names");
            Header(root, reader);
            JsonObject templateRoot = JsonNode.Parse(template ?? throw new ArgumentNullException(nameof(template)), documentOptions: Lenient) as JsonObject
                ?? throw new FormatException("template: a template is a JSON object.");
            if (!templateRoot.ContainsKey("expects"))
            {
                throw new FormatException($"template: \"{Reader.String(root, "", "template")}\" is not a template: it expects nothing.");
            }

            View applied = Template(templateRoot, reader, root["bind"]);
            return applied with
            {
                Title = Reader.OptionalString(root, "", "title") ?? applied.Title,
                Names = root["names"] is JsonNode own ? applied.Names.Over(reader.ReadNames(own)) : applied.Names,
            };
        }

        if (root.ContainsKey("expects"))
        {
            return Template(root, reader, null);
        }

        Reader.Keys(root, "", "$schema", "ruleSet", "title", "layout", "names");
        Header(root, reader);
        return new View(
            Reader.OptionalString(root, "", "title"),
            ReadLayout(root, reader),
            root["names"] is JsonNode names ? reader.ReadNames(names) : Names.None,
            []);
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

    // A template knows no rules, so it names no rule set and reads no state; its names are for
    // whatever rules it is applied to, and are checked against none of them.
    private static View Template(JsonObject root, Reader reader, JsonNode? bind)
    {
        Reader.Keys(root, "", "$schema", "title", "expects", "layout", "names");
        Header(root, reader);
        reader.Expect(root["expects"], bind);
        return new View(
            Reader.OptionalString(root, "", "title"),
            ReadLayout(root, reader),
            root["names"] is JsonNode names ? Reader.ReadTemplateNames(names) : Names.None,
            reader.Expected);
    }

    /// <summary>
    /// Throws when a value the rules give at this position is not the kind the template said it
    /// expects: a template that bound a list and was handed a number would show nonsense instead.
    /// </summary>
    public void Check(Func<string, JsonNode?> projection, JsonNode? data)
    {
        foreach (Expected expected in Expected)
        {
            JsonNode? value = expected.Bind.Read(projection, data);
            if (!expected.Type.Matches(value))
            {
                string shown = value?.ToJsonString() ?? "null";
                throw new FormatException($"expects.projections.{expected.Name}: the template expects {expected.Type}, and the rules give {(shown.Length > 80 ? shown[..80] + "…" : shown)} (at {expected.Bind}).");
            }
        }
    }

    /// <summary>Takes the view apart and puts each piece through the checks <see cref="Parse"/> promises.</summary>
    private sealed class Reader(RuleContext context, JsonNode? rules)
    {
        private readonly HashSet<string> projections = [.. context.Projections];
        private readonly HashSet<string> inputs = [.. context.Inputs];
        private readonly JsonObject state = JsonNode.Parse(context.InitialState)?["data"] as JsonObject ?? [];

        // Set while a template is read: what it expects, and where these rules keep each of those.
        private Dictionary<string, Expectation>? expectedProjections;
        private Dictionary<string, List<string>>? expectedInputs;
        private readonly Dictionary<string, Binding> map = new(StringComparer.Ordinal);

        public List<Expected> Expected { get; } = [];

        /// <summary>
        /// Reads <c>expects</c> and holds it against the rules: each projection is one they have,
        /// or one <paramref name="bind"/> finds for it elsewhere, and each input is one of theirs.
        /// </summary>
        public void Expect(JsonNode? node, JsonNode? bind)
        {
            JsonObject expects = node as JsonObject ?? throw new FormatException("expects: an object of the projections and inputs the template needs.");
            Keys(expects, "expects", "projections", "inputs");

            JsonObject wanted = expects["projections"] as JsonObject ?? (expects.ContainsKey("projections")
                ? throw new FormatException("expects.projections: a map of projections, each to its type.")
                : []);
            expectedProjections = wanted.ToDictionary(p => p.Key, p => Expectation.Parse(
                OptionalString(wanted, "expects.projections", p.Key) ?? throw new FormatException($"expects.projections.{p.Key}: a type is a string."),
                $"expects.projections.{p.Key}"), StringComparer.Ordinal);

            JsonObject moves = expects["inputs"] as JsonObject ?? (expects.ContainsKey("inputs")
                ? throw new FormatException("expects.inputs: a map of inputs, each to the parameters the template uses.")
                : []);
            expectedInputs = moves.ToDictionary(i => i.Key, i => Each(moves, "expects.inputs", i.Key, (n, a) =>
                n is JsonValue v && v.TryGetValue(out string? name) ? name : throw new FormatException($"{a}: a parameter is named by a string.")), StringComparer.Ordinal);

            Bind(bind);

            foreach (var (name, type) in expectedProjections)
            {
                if (!map.TryGetValue(name, out Binding? found))
                {
                    if (!projections.Contains(name))
                    {
                        throw new FormatException($"expects.projections.{name}: the rules project no \"{name}\". {(projections.Count == 0 ? "They project nothing" : $"They project {string.Join(", ", projections)}")}; a view that applies the template can bind it to what they have.");
                    }

                    found = new Binding(false, name, []);
                    map[name] = found;
                }

                if (type.Kind == Kind.Board && BoardShapeOf(found) is null)
                {
                    throw new FormatException($"expects.projections.{name}: a board is a state field the rules declare with grid.board, which no projection can be; a view that applies the template binds it to one, such as \"$board\".");
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

        // Where these rules keep a projection the template expects: any binding of theirs, $ included.
        private void Bind(JsonNode? node)
        {
            if (node is null)
            {
                return;
            }

            JsonObject bind = node as JsonObject ?? throw new FormatException("bind: an object of projections.");
            Keys(bind, "bind", "projections");
            if (bind["projections"] is not JsonNode given)
            {
                return;
            }

            JsonObject wanted = given as JsonObject ?? throw new FormatException("bind.projections: a map of the projections the template expects, each to a binding of these rules.");
            foreach (string name in wanted.Select(p => p.Key))
            {
                if (!expectedProjections!.ContainsKey(name))
                {
                    throw new FormatException($"bind.projections.{name}: the template expects no \"{name}\". It expects {(expectedProjections.Count == 0 ? "no projections" : string.Join(", ", expectedProjections.Keys))}.");
                }

                map[name] = RuleBinding(wanted, "bind.projections", name)!;
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

        public static Names ReadTemplateNames(JsonNode node)
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
                throw new FormatException($"{at}: the template expects no parameter \"{parts[1]}\" of \"{parts[0]}\". Add it to expects.inputs.{parts[0]}.");
            }

            CheckParameter(parts[0], parts[1], at);
            return new SquareMove(parts[0], parts[1]);
        }

        // An input a template names is one it expects, and every input named is one the rules have.
        private void Input(string name, string at)
        {
            if (expectedInputs is not null && !expectedInputs.ContainsKey(name))
            {
                throw new FormatException($"{at}: the template expects no input \"{name}\". {(expectedInputs.Count == 0 ? "It expects none" : $"It expects {string.Join(", ", expectedInputs.Keys)}")}; name it in expects.inputs.");
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
        /// A binding of the view, as the rules will be read through it. In a template it names a
        /// projection the template expects, of a kind <paramref name="wants"/> allows when it lists
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
                throw new FormatException($"{at}: a template reads no state, only the projections it expects. Expect one for \"{binding.Root}\" instead.");
            }

            if (!expectedProjections!.TryGetValue(binding.Root, out Expectation? type))
            {
                throw new FormatException($"{at}: the template expects no \"{binding.Root}\". {(expectedProjections.Count == 0 ? "It expects no projections" : $"It expects {string.Join(", ", expectedProjections.Keys)}")}; add it to expects.projections.");
            }

            foreach (string step in binding.Path)
            {
                type = type.Step(step)
                    ?? throw new FormatException($"{at}: \"{binding}\" steps to \"{step}\", and the template expects {binding.Root} to be {expectedProjections[binding.Root]}.");
            }

            // A board's size is in the rules' schema, so "any" is not enough for one; for anything else it is.
            bool fits = wants.Length == 0 || wants.Contains(type.Kind) || type.Kind == Kind.Any && !wants.Contains(Kind.Board);
            if (!fits)
            {
                throw new FormatException($"{at}: \"{binding}\" is {type} in what the template expects, and this needs {string.Join(" or ", wants.Select(w => w.ToString().ToLowerInvariant()))}.");
            }

            Binding found = map[binding.Root];
            return found with { Path = [.. found.Path, .. binding.Path] };
        }

        // A binding of these rules themselves: its first name is a projection or a state field they have.
        private Binding RuleBinding(JsonObject parent, string at, string key)
        {
            string text = OptionalString(parent, at, key) ?? throw new FormatException($"{Join(at, key)}: missing.");
            Binding binding = Binding.Parse(text, $"{Join(at, key)}");
            bool known = binding.FromState ? state.ContainsKey(binding.Root) : projections.Contains(binding.Root);
            if (!known)
            {
                throw new FormatException(binding.FromState
                    ? $"{at}.{key}: the state has no field \"{binding.Root}\"."
                    : $"{at}.{key}: the rules project no \"{binding.Root}\". {(projections.Count == 0 ? "They project nothing; bind to the state with $." : $"They project {string.Join(", ", projections)}.")}");
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

/// <summary>A projection a template expects, where these rules keep it, and the type it was promised to have.</summary>
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
