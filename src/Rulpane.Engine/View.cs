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
    /// rules keep it — a projection, a path into one, a state field, or for a name the view reads
    /// as an input, one of their inputs. Each must be one the view reads. A tone or a piece the view
    /// gives one of its own values is given to each value of the rules' that <c>b</c> calls by it.
    /// </summary>
    public static View Parse(string json, RuleContext context, string rules, IReadOnlyDictionary<string, Bound>? bind = null)
    {
        JsonObject root = JsonNode.Parse(json, documentOptions: Lenient) as JsonObject
            ?? throw new FormatException("A view is a JSON object.");

        var reader = new Reader(context, JsonNode.Parse(rules, documentOptions: Lenient), bind ?? new Dictionary<string, Bound>());
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
                root["names"] is JsonNode names ? reader.ReadUncheckedNames(names) : Names.None,
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
    private sealed class Reader(RuleContext context, JsonNode? rules, IReadOnlyDictionary<string, Bound> b)
    {
        private readonly HashSet<string> projections = [.. context.Projections];
        private readonly HashSet<string> inputs = [.. context.Inputs];
        private readonly JsonObject state = JsonNode.Parse(context.InitialState)?["data"] as JsonObject ?? [];

        // Set while a view with expects is read: what it expects, and where these rules keep each of those.
        private Dictionary<string, Expectation>? expectedProjections;
        private Dictionary<string, List<string>>? expectedInputs;
        private readonly Dictionary<string, Binding> map = new(StringComparer.Ordinal);

        // The paneset's b. Whether a name binds a value or an input is how the view reads it, so
        // each is held to the rules when first read, and remembered as the one or the other.
        private readonly IReadOnlyDictionary<string, Bound> bind = b.Keys.FirstOrDefault(k => k.Length == 0 || k.Contains('.') || k.StartsWith('$')) is string wrong
            ? throw new FormatException($"b.{wrong}: what is bound is a name the view reads, such as \"stage\" or \"place\".")
            : b;
        private readonly Dictionary<string, Binding> boundValues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> boundInputs = new(StringComparer.Ordinal);

        // Each of b's values the view gave a look to, by the name it was read through.
        private readonly HashSet<(string Name, string Value)> looked = [];

        /// <summary>
        /// Throws for a name in <c>b</c> the view never read, and for a value of b's the view gives no
        /// look to: either would change nothing, and was most likely misspelt.
        /// </summary>
        public void AllBound()
        {
            foreach (string name in bind.Keys.Where(n => !boundValues.ContainsKey(n) && !boundInputs.ContainsKey(n)))
            {
                throw new FormatException(expectedProjections is null
                    ? $"b.{name}: the view reads no \"{name}\"."
                    : $"b.{name}: the view expects no \"{name}\". It expects {(expectedProjections.Count + expectedInputs!.Count == 0 ? "nothing" : string.Join(", ", expectedProjections.Keys.Concat(expectedInputs.Keys)))}.");
            }

            foreach (var (name, bound) in bind)
            {
                foreach (var (value, own) in bound.Values.Where(v => !looked.Contains((name, v.Value))))
                {
                    throw new FormatException($"b.{name}.values.{value}: the view gives \"{own}\" no tone or piece where it reads \"{name}\", so calling a value by it changes nothing.");
                }
            }
        }

        // Where b keeps a name the view reads as a value; null when b does not bind it.
        private Binding? BoundValue(string name)
        {
            if (!bind.TryGetValue(name, out Bound? bound))
            {
                return null;
            }

            if (boundInputs.ContainsKey(name))
            {
                throw new FormatException($"b.{name}: the view reads \"{name}\" both as a value and as an input, so b cannot bind it to one place.");
            }

            if (!boundValues.TryGetValue(name, out Binding? binding))
            {
                binding = RuleBinding(bound.At, bound.Values.Count == 0 ? $"b.{name}" : $"b.{name}.at");
                boundValues[name] = binding;
            }

            return binding;
        }

        // The rules' input b gives for a name the view reads as an input; null when b does not bind it.
        private string? BoundInput(string name)
        {
            if (!bind.TryGetValue(name, out Bound? bound))
            {
                return null;
            }

            if (boundValues.ContainsKey(name))
            {
                throw new FormatException($"b.{name}: the view reads \"{name}\" both as a value and as an input, so b cannot bind it to one place.");
            }

            if (bound.Values.Count > 0)
            {
                throw new FormatException($"b.{name}: the view reads \"{name}\" as an input, which has no values to call by other names; bind it to the input alone, as a string.");
            }

            if (!inputs.Contains(bound.At))
            {
                throw new FormatException($"b.{name}: the view reads \"{name}\" as an input, and the rules have no input \"{bound.At}\". They have {string.Join(", ", inputs)}.");
            }

            boundInputs[name] = bound.At;
            return bound.At;
        }

        /// <summary>
        /// <paramref name="looks"/>, keyed by the view's own values, with each value of the rules'
        /// that b calls by one of them given its look as well. <paramref name="text"/> is the binding
        /// as the view wrote it: only a name b binds has values it calls by other names.
        /// </summary>
        private Dictionary<string, T> Looks<T>(Dictionary<string, T> looks, string text)
        {
            string name = text.Split('.')[0];
            if (text.StartsWith('$') || !bind.TryGetValue(name, out Bound? bound))
            {
                return looks;
            }

            var all = new Dictionary<string, T>(looks, StringComparer.Ordinal);
            foreach (var (value, own) in bound.Values)
            {
                if (looks.TryGetValue(own, out T? look))
                {
                    all[value] = look;
                    looked.Add((name, own));
                }
            }

            return all;
        }

        public List<Expected> Expected { get; } = [];

        /// <summary>
        /// Reads <c>expects</c> and holds it against the rules: each projection is one they have,
        /// or one the paneset's <c>b</c> finds for it elsewhere, and each input is one of theirs,
        /// under its own name or the one <c>b</c> gives.
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
                Binding found = BoundValue(name)
                    ?? (projections.Contains(name)
                        ? new Binding(false, name, [])
                        : throw new FormatException($"expects.projections.{name}: the rules project no \"{name}\". {(projections.Count == 0 ? "They project nothing" : $"They project {string.Join(", ", projections)}")}; the paneset's b can bind it to what they have."));

                map[name] = found;
                if (type.Kind == Kind.Board && BoardShapeOf(found) is null)
                {
                    throw new FormatException($"expects.projections.{name}: a board is a state field the rules declare with grid.board, which no projection can be; the paneset's b binds it to one, such as \"$board\".");
                }

                Expected.Add(new Expected(name, found, type));
            }

            foreach (var (input, parameters) in expectedInputs)
            {
                string theirs = BoundInput(input) ?? input;
                if (!inputs.Contains(theirs))
                {
                    throw new FormatException($"expects.inputs.{input}: the rules have no input \"{input}\". They have {string.Join(", ", inputs)}; the paneset's b can bind it to one of them.");
                }

                foreach (string parameter in parameters)
                {
                    CheckParameter(theirs, parameter, $"expects.inputs.{input}");
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
        // An input is named as the view reads it, and the name goes to the one b binds it to.
        public Names ReadNames(JsonNode node)
        {
            JsonObject names = node as JsonObject ?? throw new FormatException("names: names is an object of inputs, params, codes and values.");
            Keys(names, "names", "inputs", "params", "codes", "values");
            var given = Map(names, "inputs").ToDictionary(i => Input(i.Key, $"names.inputs.{i.Key}"), i => i.Value);

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

        // A view with expects names an input it expects as it reads it, and the name goes to the one b binds it to.
        public Names ReadUncheckedNames(JsonNode node)
        {
            JsonObject names = node as JsonObject ?? throw new FormatException("names: names is an object of inputs, params, codes and values.");
            Keys(names, "names", "inputs", "params", "codes", "values");
            var given = Map(names, "inputs").ToDictionary(i => boundInputs.GetValueOrDefault(i.Key, i.Key), i => i.Value);
            return new Names(given, Map(names, "params"), Map(names, "codes"), Map(names, "values"));
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
                    Keys(block, at, "c", "showWhen", "text", "format");
                    return new Heading(String(block, at, "text"), showWhen) { Parts = Parts(String(block, at, "text"), $"{at}.text"), Format = ReadFormat(block, at) };
                case "text":
                    Keys(block, at, "c", "showWhen", "text", "format");
                    return new Paragraph(String(block, at, "text"), showWhen) { Parts = Parts(String(block, at, "text"), $"{at}.text"), Format = ReadFormat(block, at) };
                case "fields":
                    Keys(block, at, "c", "showWhen", "title", "rows");
                    return new Fields(
                        OptionalString(block, at, "title"),
                        Each(block, at, "rows", ReadRow),
                        showWhen);
                case "chart":
                    Keys(block, at, "c", "showWhen", "title", "bind", "x", "bars", "lines", "value", "format", "as");
                    ChartKind chartKind = OptionalString(block, at, "as") switch
                    {
                        null or "bars" => ChartKind.Bars,
                        "lines" => ChartKind.Lines,
                        "pie" => ChartKind.Pie,
                        string other => throw new FormatException($"{at}.as: \"{other}\" is not a kind of chart. It is bars, lines or pie."),
                    };
                    // What each kind is drawn from: bars stacked, lines, or one value cut into slices.
                    string series = chartKind == ChartKind.Lines ? "lines" : "bars";
                    foreach (string wrong in new[] { "bars", "lines", "value" }.Where(k => block.ContainsKey(k) && k != (chartKind == ChartKind.Pie ? "value" : series)))
                    {
                        throw new FormatException($"{at}.{wrong}: a chart drawn as {chartKind.ToString().ToLowerInvariant()} takes {(chartKind == ChartKind.Pie ? "value, the key of each record's share" : series)}, not {wrong}.");
                    }

                    return new Chart(
                        OptionalString(block, at, "title"),
                        RequiredBinding(block, at, "bind", Kind.List),
                        ReadAxis(block["x"], $"{at}.x"),
                        chartKind == ChartKind.Pie ? [] : Each(block, at, series, ReadBar),
                        ReadFormat(block, at),
                        showWhen)
                    {
                        As = chartKind,
                        Value = chartKind == ChartKind.Pie ? String(block, at, "value") : null,
                    };
                case "columns":
                    Keys(block, at, "c", "showWhen", "blocks", "widths");
                    List<Block> blocks = Each(block, at, "blocks", (n, a) => n is JsonObject { } child && child["c"] is JsonValue c && c.TryGetValue(out string? inner) && inner == "columns"
                        ? throw new FormatException($"{a}: columns do not go inside columns; put the blocks side by side in one.")
                        : Block(n, a));
                    if (blocks.Count is < 2 or > 4)
                    {
                        throw new FormatException($"{at}.blocks: columns put 2 to 4 blocks side by side, and these are {blocks.Count}.");
                    }

                    List<decimal>? widths = block.ContainsKey("widths")
                        ? Each(block, at, "widths", (n, a) => n is JsonValue v && v.TryGetValue(out decimal w) && w > 0 ? w : throw new FormatException($"{a}: a width is a number above 0, the share of the row the block takes."))
                        : null;
                    if (widths is not null && widths.Count != blocks.Count)
                    {
                        throw new FormatException($"{at}.widths: one width for each block, and there are {blocks.Count} blocks and {widths.Count} widths.");
                    }

                    return new Columns(blocks, widths, showWhen);
                case "stats":
                    Keys(block, at, "c", "showWhen", "items");
                    return new Stats(Each(block, at, "items", ReadStat), showWhen);
                case "progress":
                    Keys(block, at, "c", "showWhen", "title", "items");
                    return new Progress(OptionalString(block, at, "title"), Each(block, at, "items", ReadProgress), showWhen);
                case "list":
                    Keys(block, at, "c", "showWhen", "title", "bind");
                    return new ValueList(OptionalString(block, at, "title"), RequiredBinding(block, at, "bind", Kind.List), showWhen);
                case "table":
                    Keys(block, at, "c", "showWhen", "title", "bind", "columns", "rowActions");
                    return new Table(
                        OptionalString(block, at, "title"),
                        RequiredBinding(block, at, "bind", Kind.List),
                        block.ContainsKey("columns") ? Each(block, at, "columns", (n, a) => ReadColumn(n, a, String(block, at, "bind"))) : null,
                        showWhen)
                    {
                        RowActions = block.ContainsKey("rowActions") ? Each(block, at, "rowActions", ReadRowAction) : null,
                    };
                case "actions":
                    Keys(block, at, "c", "showWhen", "title", "inputs", "as", "fill", "controls");
                    IReadOnlyList<string>? offered = Inputs(block, at);
                    return new Actions(OptionalString(block, at, "title"), offered, OptionalString(block, at, "as") switch
                    {
                        null or "buttons" => false,
                        "select" => true,
                        string other => throw new FormatException($"{at}.as: \"{other}\" is not a way to offer moves. It is buttons or select."),
                    }, showWhen)
                    {
                        Fill = block["fill"] switch
                        {
                            null => null,
                            JsonObject fill => fill.ToDictionary(f => f.Key, f => RequiredBinding(fill, $"{at}.fill", f.Key)),
                            _ => throw new FormatException($"{at}.fill: a map of parameters, each to the binding its field starts with."),
                        },
                        Controls = block["controls"] is JsonNode controls ? ReadControls(controls, $"{at}.controls", offered) : null,
                    };
                case "board":
                    Keys(block, at, "c", "showWhen", "title", "bind", "shape", "play", "move", "pieces", "checkered", "as", "tones", "display");
                    Binding board = RequiredBinding(block, at, "bind", Kind.Board);
                    BoardShape? shape = BoardShapeOf(board);
                    if (OptionalString(block, at, "shape") is string sized)
                    {
                        // A projection can work out what a board shows, but its size is only ever in a schema.
                        Binding of = Binding.Parse(sized, $"{at}.shape");
                        shape = expectedProjections is not null
                            ? throw new FormatException($"{at}.shape: a view with expects reads no state, and a board's size is a state field's.")
                            : BoardShapeOf(of) ?? throw new FormatException($"{at}.shape: \"{sized}\" is not a state field the rules declare with grid.board, such as \"$board\".");
                    }

                    if (block.ContainsKey("play") && block.ContainsKey("move"))
                    {
                        throw new FormatException($"{at}: a board is played by one square (play) or by two (move), not both.");
                    }

                    // A board of keys is pressed one key at a time, and its look is the keys' own.
                    bool keys = OptionalString(block, at, "as") switch
                    {
                        null or "board" => false,
                        "keys" => true,
                        string other => throw new FormatException($"{at}.as: \"{other}\" is not a way to draw a board. It is board or keys."),
                    };
                    if (!keys)
                    {
                        foreach (string only in new[] { "tones", "display" }.Where(block.ContainsKey))
                        {
                            throw new FormatException($"{at}.{only}: only a board drawn as keys has {only}; add \"as\": \"keys\".");
                        }
                    }
                    else if (!block.ContainsKey("play") || block.ContainsKey("checkered"))
                    {
                        throw new FormatException($"{at}: a board drawn as keys is pressed one key at a time, so it has play, and is not checkered.");
                    }

                    Dictionary<string, Tone>? tones = null;
                    if (block["tones"] is JsonNode toneNode)
                    {
                        JsonObject tone = toneNode as JsonObject ?? throw new FormatException($"{at}.tones: tones map values to tones.");
                        tones = Looks(tone.ToDictionary(t => t.Key, t => ReadTone(t.Value, $"{at}.tones.{t.Key}")), String(block, at, "bind"));
                    }

                    return new Board(
                        OptionalString(block, at, "title"),
                        board,
                        shape ?? throw new FormatException($"{at}.bind: a board binds a state field the rules declare with grid.board, such as \"$board\", or a projection with shape naming one."),
                        OptionalString(block, at, "play") is string play ? Play(play, $"{at}.play") : null,
                        Pieces(block, at) is { } pieces ? Looks(pieces, String(block, at, "bind")) : null,
                        showWhen)
                    {
                        Move = OptionalString(block, at, "move") is string move ? Play(move, $"{at}.move") : null,
                        Checkered = block["checkered"] switch
                        {
                            null => false,
                            JsonValue v when v.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => v.GetValue<bool>(),
                            _ => throw new FormatException($"{at}.checkered: true or false."),
                        },
                        Keys = keys,
                        Tones = tones,
                        Readout = block["display"] is JsonNode display ? ReadDisplay(display, $"{at}.display") : null,
                    };
                default:
                    throw new FormatException($"{at}.c: there is no block called \"{kind}\". A view has heading, text, fields, stats, progress, list, table, chart, board, actions and columns.");
            }
        }

        private Row ReadRow(JsonNode? node, string at)
        {
            JsonObject row = node as JsonObject ?? throw new FormatException($"{at}: a row is an object.");
            Keys(row, at, "label", "bind", "tone", "showWhen", "format");
            Dictionary<string, Tone>? tones = null;
            if (row["tone"] is JsonNode toneNode)
            {
                JsonObject tone = toneNode as JsonObject ?? throw new FormatException($"{at}.tone: a tone maps values to tones.");
                tones = tone.ToDictionary(t => t.Key, t => ReadTone(t.Value, $"{at}.tone.{t.Key}"));
            }

            return new Row(
                String(row, at, "label"),
                RequiredBinding(row, at, "bind"),
                tones is null ? null : Looks(tones, String(row, at, "bind")),
                OptionalBinding(row, at, "showWhen", Kind.Bool))
            {
                Format = ReadFormat(row, at),
            };
        }

        // A readout over a board of keys: { "bind", "format", "above" }.
        private KeyDisplay ReadDisplay(JsonNode node, string at)
        {
            JsonObject display = node as JsonObject ?? throw new FormatException($"{at}: a display is an object with bind, and optionally format and above.");
            Keys(display, at, "bind", "format", "above");
            return new KeyDisplay(RequiredBinding(display, at, "bind"), ReadFormat(display, at), OptionalBinding(display, at, "above"));
        }

        private Stat ReadStat(JsonNode? node, string at)
        {
            JsonObject stat = node as JsonObject ?? throw new FormatException($"{at}: a stat is an object.");
            Keys(stat, at, "label", "bind", "format");
            return new Stat(String(stat, at, "label"), RequiredBinding(stat, at, "bind")) { Format = ReadFormat(stat, at) };
        }

        // { "input": "leave", "args": { "name": "player" } }: the input, and for each of its
        // parameters given by the row, the row's key that gives it.
        private RowAction ReadRowAction(JsonNode? node, string at)
        {
            JsonObject action = node as JsonObject ?? throw new FormatException($"{at}: a row's action is an object with an input and args.");
            Keys(action, at, "input", "args");
            string input = Input(String(action, at, "input"), $"{at}.input");
            JsonObject args = action["args"] as JsonObject ?? throw new FormatException($"{at}.args: a map of the input's parameters, each to the row's key that gives it.");
            var given = args.ToDictionary(a => a.Key, a => OptionalString(args, $"{at}.args", a.Key) ?? throw new FormatException($"{at}.args.{a.Key}: a key of the row is a string."));
            foreach (string parameter in given.Keys)
            {
                CheckParameter(input, parameter, $"{at}.args");
            }

            return new RowAction(input, given);
        }

        // { "years": "slider", "due": "date" }: how a parameter left open is asked for. Where the
        // rules write the input themselves, its schema must suit: a slider a number between a min
        // and a max, a date text.
        private Dictionary<string, Control> ReadControls(JsonNode node, string at, IReadOnlyList<string>? offered)
        {
            JsonObject controls = node as JsonObject ?? throw new FormatException($"{at}: a map of parameters, each to slider or date.");
            var read = new Dictionary<string, Control>(StringComparer.Ordinal);
            foreach (var (parameter, value) in controls)
            {
                Control control = OptionalString(controls, at, parameter) switch
                {
                    "slider" => Control.Slider,
                    "date" => Control.Date,
                    _ => throw new FormatException($"{at}.{parameter}: a control is slider or date."),
                };
                if (rules?["inputs"] is JsonObject written)
                {
                    var schemas = written
                        .Where(i => offered is null || offered.Contains(i.Key))
                        .Select(i => i.Value?["params"]?[parameter])
                        .Where(p => p is not null)
                        .ToList();
                    if (schemas.Count == 0 && (offered is null || offered.All(written.ContainsKey)))
                    {
                        throw new FormatException($"{at}.{parameter}: no input this block offers has a parameter \"{parameter}\".");
                    }

                    foreach (JsonNode? schema in schemas.Select(p => p!["open"]))
                    {
                        string? kind = schema?["op"]?.GetValue<string>();
                        bool suits = control == Control.Slider
                            ? kind is "type.int" or "type.decimal" && schema!["min"] is not null && schema["max"] is not null
                            : kind == "type.string";
                        if (!suits)
                        {
                            throw new FormatException(control == Control.Slider
                                ? $"{at}.{parameter}: a slider asks for a number between a min and a max, and the rules leave \"{parameter}\" open as something else."
                                : $"{at}.{parameter}: a date is written as text, 2026-11-03, and the rules leave \"{parameter}\" open as something else.");
                        }
                    }
                }

                read[parameter] = control;
            }

            return read;
        }

        // A column is its key, or the key with a label to head it, and how its values read.
        private Column ReadColumn(JsonNode? node, string at, string table)
        {
            if (node is JsonValue value && value.TryGetValue(out string? key))
            {
                return new Column(key, key);
            }

            JsonObject column = node as JsonObject ?? throw new FormatException($"{at}: a column is a key or an object.");
            Keys(column, at, "key", "label", "format", "tone");
            string k = String(column, at, "key");
            Dictionary<string, Tone>? tones = null;
            if (column["tone"] is JsonNode toneNode)
            {
                JsonObject tone = toneNode as JsonObject ?? throw new FormatException($"{at}.tone: a tone maps values to tones.");
                tones = Looks(tone.ToDictionary(t => t.Key, t => ReadTone(t.Value, $"{at}.tone.{t.Key}")), table);
            }

            return new Column(k, OptionalString(column, at, "label") ?? k) { Format = ReadFormat(column, at), Tones = tones };
        }

        // { "label": "Packed", "bind": "summary.done", "of": "summary.all" }
        private ProgressItem ReadProgress(JsonNode? node, string at)
        {
            JsonObject item = node as JsonObject ?? throw new FormatException($"{at}: a bar is an object with a label, bind and of.");
            Keys(item, at, "label", "bind", "of");
            return new ProgressItem(String(item, at, "label"), RequiredBinding(item, at, "bind"), RequiredBinding(item, at, "of"));
        }

        // "format": { "digits": 0, "divide": 10000, "prefix": "", "suffix": "万" }, every key optional.
        private static Format? ReadFormat(JsonObject parent, string at)
        {
            if (parent["format"] is not JsonNode node)
            {
                return null;
            }

            at += ".format";
            JsonObject format = node as JsonObject ?? throw new FormatException($"{at}: a format is an object, such as {{ \"suffix\": \" yen\" }}.");
            Keys(format, at, "digits", "divide", "prefix", "suffix");
            int? digits = format["digits"] switch
            {
                null => null,
                JsonValue v when v.TryGetValue(out int d) && d is >= 0 and <= 10 => d,
                _ => throw new FormatException($"{at}.digits: a whole number of places, 0 to 10."),
            };
            decimal divide = format["divide"] switch
            {
                null => 1,
                JsonValue v when v.TryGetValue(out decimal d) && d > 0 => d,
                _ => throw new FormatException($"{at}.divide: a number above 0."),
            };
            return new Format(digits, divide, OptionalString(format, at, "prefix") ?? "", OptionalString(format, at, "suffix") ?? "");
        }

        // The key along the bottom: a key, or { "key", "format" }.
        private static ChartAxis ReadAxis(JsonNode? node, string at)
        {
            if (node is JsonValue value && value.TryGetValue(out string? key))
            {
                return new ChartAxis(key, null);
            }

            JsonObject axis = node as JsonObject ?? throw new FormatException($"{at}: the key each bar is labelled by, or an object with key and format.");
            Keys(axis, at, "key", "format");
            return new ChartAxis(String(axis, at, "key"), ReadFormat(axis, at));
        }

        private ChartBar ReadBar(JsonNode? node, string at)
        {
            JsonObject bar = node as JsonObject ?? throw new FormatException($"{at}: a bar is an object with a key and a label.");
            Keys(bar, at, "key", "label", "tone");
            return new ChartBar(String(bar, at, "key"), String(bar, at, "label"), bar["tone"] is null ? Tone.accent : ReadTone(bar["tone"], $"{at}.tone"));
        }

        // Only a state field can be drawn as a board: the rules give its size nowhere but in its
        // schema, and a board has no form a projection could give it in.
        private BoardShape? BoardShapeOf(Binding binding) =>
            binding.FromState && binding.Path.Count == 0 ? BoardShape.Of(rules?["state"]?["schema"]?[binding.Root]) : null;

        // "place.at": the input played by choosing a square, and its parameter that takes the square;
        // for move, the parameter that takes a tuple whose first two parts are the squares from and to.
        private SquareMove Play(string text, string at)
        {
            string[] parts = text.Split('.');
            if (parts.Length != 2 || parts.Any(p => p.Length == 0))
            {
                throw new FormatException($"{at}: \"{text}\" is not an input and a parameter, such as \"place.at\".");
            }

            string input = Input(parts[0], at);
            if (expectedInputs is not null && !expectedInputs[parts[0]].Contains(parts[1]))
            {
                throw new FormatException($"{at}: the view expects no parameter \"{parts[1]}\" of \"{parts[0]}\". Add it to expects.inputs.{parts[0]}.");
            }

            CheckParameter(input, parts[1], at);
            return new SquareMove(input, parts[1]);
        }

        // An input a view with expects names is one it expects, and every input named is one the
        // rules have, under its own name or the one b binds it to. Returns the rules' name for it.
        private string Input(string name, string at)
        {
            if (expectedInputs is not null && !expectedInputs.ContainsKey(name))
            {
                throw new FormatException($"{at}: the view expects no input \"{name}\". {(expectedInputs.Count == 0 ? "It expects none" : $"It expects {string.Join(", ", expectedInputs.Keys)}")}; name it in expects.inputs.");
            }

            string theirs = BoundInput(name) ?? name;
            if (!inputs.Contains(theirs))
            {
                throw new FormatException($"{at}: the rules have no input \"{name}\". They have {string.Join(", ", inputs)}; the paneset's b can bind it to one of them.");
            }

            return theirs;
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
                    : throw new FormatException($"{at}.pieces.{p.Key}: a piece is dark, light, accent, ok, warn, danger or tile."));
        }

        // "*" is every input; a list names the ones this block offers, as the rules call them.
        private IReadOnlyList<string>? Inputs(JsonObject block, string at)
        {
            if (block["inputs"] is JsonValue all && all.TryGetValue(out string? star) && star == "*")
            {
                return null;
            }

            return Each(block, at, "inputs", (n, a) =>
                n is JsonValue v && v.TryGetValue(out string? name) ? Input(name, a) : throw new FormatException($"{a}: an input is named by a string."));
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
            if (!binding.FromState && BoundValue(binding.Root) is Binding bound)
            {
                return bound with { Path = [.. bound.Path, .. binding.Path] };
            }

            return Known(binding, Join(at, key));
        }

        // "Total: {total} yen": the words, and a binding in each pair of braces. {{ and }} are a brace.
        private List<TextPart> Parts(string text, string at)
        {
            var parts = new List<TextPart>();
            var words = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
                {
                    words.Append(c);
                    i++;
                }
                else if (c == '{')
                {
                    int end = text.IndexOf('}', i + 1);
                    if (end < 0)
                    {
                        throw new FormatException($"{at}: a {{ with no }} after it. A value goes in braces, {{total}}, and a brace itself is written {{{{.");
                    }

                    if (words.Length > 0)
                    {
                        parts.Add(new TextPart(words.ToString(), null));
                        words.Clear();
                    }

                    parts.Add(new TextPart(null, ViewBinding(text[(i + 1)..end], at)));
                    i = end;
                }
                else if (c == '}')
                {
                    throw new FormatException($"{at}: a }} with no {{ before it. A brace itself is written }}}}.");
                }
                else
                {
                    words.Append(c);
                }
            }

            if (words.Length > 0)
            {
                parts.Add(new TextPart(words.ToString(), null));
            }

            return parts;
        }

        // A binding of the view's written in its text, read as one in its own key would be.
        private Binding ViewBinding(string text, string at)
        {
            Binding binding = Binding.Parse(text, at);
            if (expectedProjections is not null)
            {
                return Resolve(binding, at, []);
            }

            return !binding.FromState && BoundValue(binding.Root) is Binding bound
                ? bound with { Path = [.. bound.Path, .. binding.Path] }
                : Known(binding, at);
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

/// <summary>A heading; values the view writes into it in braces are read as it is shown.</summary>
public sealed record Heading(string Text, Binding? ShowWhen) : Block(ShowWhen)
{
    public IReadOnlyList<TextPart> Parts { get; init; } = [new(Text, null)];

    /// <summary>How a number written into it reads.</summary>
    public Format? Format { get; init; }
}

/// <summary>A paragraph; values the view writes into it in braces are read as it is shown.</summary>
public sealed record Paragraph(string Body, Binding? ShowWhen) : Block(ShowWhen)
{
    public IReadOnlyList<TextPart> Parts { get; init; } = [new(Body, null)];

    /// <summary>How a number written into it reads.</summary>
    public Format? Format { get; init; }
}

/// <summary>A piece of a text: words as they are, or the value at a binding.</summary>
public sealed record TextPart(string? Words, Binding? Bind);

/// <summary>A card of labelled values. A row with a tone shows its value as a chip of that tone.</summary>
public sealed record Fields(string? Title, IReadOnlyList<Row> Rows, Binding? ShowWhen) : Block(ShowWhen);

public sealed record Row(string Label, Binding Bind, IReadOnlyDictionary<string, Tone>? Tones, Binding? ShowWhen)
{
    /// <summary>How the value is written, when it is a number.</summary>
    public Format? Format { get; init; }
}

/// <summary>
/// Blocks side by side, each taking its share of <see cref="Widths"/> of the row, or an equal one;
/// on a narrow screen they are one above the other, in order.
/// </summary>
public sealed record Columns(IReadOnlyList<Block> Blocks, IReadOnlyList<decimal>? Widths, Binding? ShowWhen) : Block(ShowWhen);

/// <summary>Values set large, side by side.</summary>
public sealed record Stats(IReadOnlyList<Stat> Items, Binding? ShowWhen) : Block(ShowWhen);

public sealed record Stat(string Label, Binding Bind)
{
    /// <summary>How the value is written, when it is a number.</summary>
    public Format? Format { get; init; }
}

/// <summary>
/// How a number is written on screen: grouped by thousands always, divided first by
/// <see cref="Divide"/>, rounded to <see cref="Digits"/> places when given, and set between
/// <see cref="Prefix"/> and <see cref="Suffix"/> — <c>12,382,389 円</c>, <c>1,238万</c>.
/// The value the rules hold is not changed; only how it reads.
/// </summary>
public sealed record Format(int? Digits, decimal Divide, string Prefix, string Suffix)
{
    /// <summary>The number written this way; null for a value that is not a number, which is shown as it is.</summary>
    public string? Write(JsonNode? value)
    {
        if (value is not JsonValue number || number.GetValueKind() != System.Text.Json.JsonValueKind.Number)
        {
            return null;
        }

        decimal shown = number.GetValue<decimal>() / Divide;
        string digits = Digits is int places
            ? Math.Round(shown, places, MidpointRounding.AwayFromZero).ToString($"N{places}", System.Globalization.CultureInfo.InvariantCulture)
            : shown.ToString("#,0.############################", System.Globalization.CultureInfo.InvariantCulture);
        return Prefix + digits + Suffix;
    }
}

/// <summary>
/// A list of records as bars, one per record along <see cref="X"/>, each made of the
/// <see cref="Bars"/> stacked from the bottom: a balance as what was paid in and what it earned.
/// </summary>
public sealed record Chart(string? Title, Binding Bind, ChartAxis X, IReadOnlyList<ChartBar> Bars, Format? Format, Binding? ShowWhen) : Block(ShowWhen)
{
    /// <summary>
    /// How the records are drawn: as bars, as lines (each of <see cref="Bars"/> a line through
    /// every record), or as a pie of <see cref="Value"/>, a slice per record named by <see cref="X"/>.
    /// </summary>
    public ChartKind As { get; init; }

    /// <summary>The key of each record's share, for a pie.</summary>
    public string? Value { get; init; }
}

/// <summary>The kinds of chart: bars stacked from the floor, lines over time, or a pie of shares.</summary>
public enum ChartKind { Bars, Lines, Pie }

/// <summary>The key each record is labelled by along the bottom, and how that label is written.</summary>
public sealed record ChartAxis(string Key, Format? Format);

/// <summary>One part of every bar: the record's key it measures, what the legend calls it, and its colour.</summary>
public sealed record ChartBar(string Key, string Label, Tone Tone);

/// <summary>A list of records, one per line. Without columns, every key any of them has.</summary>
public sealed record Table(string? Title, Binding Bind, IReadOnlyList<Column>? Columns, Binding? ShowWhen) : Block(ShowWhen)
{
    /// <summary>The moves at the end of each line, each played with what that line holds.</summary>
    public IReadOnlyList<RowAction>? RowActions { get; init; }
}

/// <summary>
/// A move offered on each line of a table: the rules' input, and for each parameter the line
/// gives, the key of the line that gives it. Parameters it leaves out are asked for when it is pressed.
/// </summary>
public sealed record RowAction(string Input, IReadOnlyDictionary<string, string> Args)
{
    /// <summary>The moves of the input whose arguments are what this line holds.</summary>
    public IReadOnlyList<ValidInput> For(JsonNode? row, IEnumerable<ValidInput>? moves) =>
        moves is null || row is not JsonObject record
            ? []
            : [.. moves.Where(m => m.Input == Input && Args.All(a =>
                m.Arguments.TryGetValue(a.Key, out string? given) && record[a.Value] is JsonNode held
                && Display.Unquote(given) == (held is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : held.ToJsonString())))];
}

public sealed record Column(string Key, string Label)
{
    /// <summary>How each value in the column is written, when it is a number; such a column is set to the right.</summary>
    public Format? Format { get; init; }

    /// <summary>The tone of a value in the column, shown as a chip of that colour.</summary>
    public IReadOnlyDictionary<string, Tone>? Tones { get; init; }
}

/// <summary>Bars of how far each thing has got: the value at a binding out of the value at another.</summary>
public sealed record Progress(string? Title, IReadOnlyList<ProgressItem> Items, Binding? ShowWhen) : Block(ShowWhen);

/// <summary>One bar: what it is called, how many are done, and out of how many.</summary>
public sealed record ProgressItem(string Label, Binding Bind, Binding Of);

/// <summary>The values of a list, side by side: what a table would show with no columns to put them in.</summary>
public sealed record ValueList(string? Title, Binding Bind, Binding? ShowWhen) : Block(ShowWhen);

/// <summary>
/// A Grid board, square by square. With <see cref="Play"/>, a square that input can be played
/// on is a button; with <see cref="Move"/>, a square a move starts from is, and once chosen, the
/// squares it can go to. A value in <see cref="Pieces"/> is drawn as that piece, and any other as text.
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

    /// <summary>The input a move from one square to another plays, and its parameter whose first two parts are those squares.</summary>
    public SquareMove? Move { get; init; }

    /// <summary>Whether the squares alternate light and dark, as a chessboard's do.</summary>
    public bool Checkered { get; init; }

    /// <summary>
    /// Whether the board is drawn as keys, as a calculator's are: no coordinates, and a key the
    /// rules do not offer now is shown, and cannot be pressed.
    /// </summary>
    public bool Keys { get; init; }

    /// <summary>The colour of each key holding one of these values; a board of keys only.</summary>
    public IReadOnlyDictionary<string, Tone>? Tones { get; init; }

    /// <summary>The readout over the keys; a board of keys only.</summary>
    public KeyDisplay? Readout { get; init; }

    /// <summary>
    /// The moves played by choosing two squares, each with the squares it goes from and to. Two
    /// moves can share both squares — a pawn promoted to one piece or another — and the board
    /// asks which once both are chosen.
    /// </summary>
    public IReadOnlyList<(string From, string To, ValidInput Move)> SquarePairs(IEnumerable<ValidInput>? moves)
    {
        if (Move is not SquareMove move || moves is null)
        {
            return [];
        }

        return [.. moves
            .Where(m => m.Input == move.Input && m.IsComplete && m.Arguments.TryGetValue(move.Parameter, out _))
            .Select(m => (Parts: Display.Parts(m.Arguments[move.Parameter]), Move: m))
            .Where(m => m.Parts.Count >= 2)
            .Select(m => (m.Parts[0], m.Parts[1], m.Move))];
    }

    /// <summary>Every move this board offers, by one square or by two: what the actions leave to it.</summary>
    public IEnumerable<ValidInput> Offered(IEnumerable<ValidInput>? moves) =>
        SquareMoves(moves).Values.Concat(SquarePairs(moves).Select(p => p.Move));
}

/// <summary>
/// The readout over a board of keys: the value at <see cref="Bind"/> set large, and over it, small,
/// the value or the list of values at <see cref="Above"/> — what a calculator shows of the sum so far.
/// A number in either is written to <see cref="Format"/>.
/// </summary>
public sealed record KeyDisplay(Binding Bind, Format? Format, Binding? Above);

/// <summary>The input a square plays, and the parameter the square goes in.</summary>
public sealed record SquareMove(string Input, string Parameter);

/// <summary>How a value on a board is drawn: a dark or a light disc, or one in a tone's colour.</summary>
public enum Piece { dark, light, accent, ok, warn, danger, tile }

/// <summary>
/// The moves of the inputs named, or of every input when <see cref="Inputs"/> is null. With
/// <see cref="Select"/>, the choices of each input are a list to pick from rather than a button each.
/// </summary>
public sealed record Actions(string? Title, IReadOnlyList<string>? Inputs, bool Select, Binding? ShowWhen) : Block(ShowWhen)
{
    /// <summary>
    /// What a field for a parameter of that name starts with, read through a binding: a form
    /// that changes a setting opens on the setting as it is.
    /// </summary>
    public IReadOnlyDictionary<string, Binding>? Fill { get; init; }

    /// <summary>How a parameter left open is asked for, where a field to type in is not the best way.</summary>
    public IReadOnlyDictionary<string, Control>? Controls { get; init; }
}

/// <summary>
/// A way to ask for a value other than a field to type in: a slider between the bounds of a
/// number, or a calendar giving a date as text, <c>2026-11-03</c>. A yes or no is always a checkbox.
/// </summary>
public enum Control { Slider, Date }

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
