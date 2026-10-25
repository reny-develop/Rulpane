using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rulpane.Viewer.Engine;

/// <summary>What kind of value an expectation is about.</summary>
public enum Kind { Any, String, Number, Bool, Board, List, Record }

/// <summary>
/// The shape a template expects a projection to have, written as a short type:
/// <c>string</c>, <c>number</c>, <c>bool</c>, <c>any</c>, <c>board</c>, <c>list&lt;T&gt;</c>, or a record
/// <c>{a, b: T}</c> whose keys without a type may hold anything; <c>?</c> after any of them lets it be null.
/// A template checks its bindings against these before any rules are seen, and the rules' values
/// are checked against them at every position.
/// </summary>
public sealed record Expectation(Kind Kind, bool Nullable, Expectation? Element, IReadOnlyDictionary<string, Expectation>? Fields)
{
    public static readonly Expectation Anything = new(Kind.Any, true, null, null);

    /// <summary>Reads a type. <c>board</c> is allowed only as the whole of one: a board is a whole state field, never a part of a value.</summary>
    public static Expectation Parse(string text, string at)
    {
        var parser = new Parser(text, at);
        Expectation type = parser.Type(top: true);
        parser.End();
        return type;
    }

    /// <summary>Whether a value the rules gave is one this expects.</summary>
    public bool Matches(JsonNode? node) => node switch
    {
        null => Nullable || Kind == Kind.Any,
        _ when Kind == Kind.Any => true,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => Kind == Kind.String,
            JsonValueKind.Number => Kind == Kind.Number,
            JsonValueKind.True or JsonValueKind.False => Kind == Kind.Bool,
            _ => false,
        },
        JsonArray list => Kind == Kind.List && list.All(Element!.Matches),
        // A key the rules leave out reads as null, as a binding through it would.
        JsonObject record => Kind == Kind.Board || Kind == Kind.Record && Fields!.All(f => f.Value.Matches(record[f.Key])),
        _ => false,
    };

    /// <summary>
    /// The type one step of a binding's path leads to; null where this type has nothing there,
    /// so a template cannot bind a key it never said its projection has.
    /// </summary>
    public Expectation? Step(string step) => Kind switch
    {
        Kind.Any => Anything,
        Kind.Record => Fields!.GetValueOrDefault(step),
        Kind.List when int.TryParse(step, out int i) && i >= 0 => Element,
        _ => null,
    };

    public override string ToString()
    {
        string text = Kind switch
        {
            Kind.List => $"list<{Element}>",
            Kind.Record => "{" + string.Join(", ", Fields!.Select(f => f.Value.Kind == Kind.Any ? f.Key : $"{f.Key}: {f.Value}")) + "}",
            _ => Kind.ToString().ToLowerInvariant(),
        };
        return Nullable && Kind != Kind.Any ? text + "?" : text;
    }

    private sealed class Parser(string text, string at)
    {
        private int position;

        public Expectation Type(bool top)
        {
            Expectation type;
            if (Accept('{'))
            {
                var fields = new Dictionary<string, Expectation>(StringComparer.Ordinal);
                do
                {
                    string key = Name();
                    if (!fields.TryAdd(key, Accept(':') ? Type(top: false) : Anything))
                    {
                        throw Fail($"the key \"{key}\" is there twice");
                    }
                }
                while (Accept(','));
                Expect('}');
                type = new Expectation(Kind.Record, false, null, fields);
            }
            else
            {
                string name = Name();
                type = name switch
                {
                    "string" => new Expectation(Kind.String, false, null, null),
                    "number" => new Expectation(Kind.Number, false, null, null),
                    "bool" => new Expectation(Kind.Bool, false, null, null),
                    "any" => Anything,
                    "board" when top => new Expectation(Kind.Board, false, null, null),
                    "board" => throw Fail("a board is a whole projection, never a part of one"),
                    "list" => List(),
                    _ => throw Fail($"there is no type \"{name}\". A type is string, number, bool, any, board, list<T> or {{a, b: T}}"),
                };
            }

            return Accept('?') ? type with { Nullable = true } : type;
        }

        private Expectation List()
        {
            Expect('<');
            Expectation element = Type(top: false);
            Expect('>');
            return new Expectation(Kind.List, false, element, null);
        }

        public void End()
        {
            Skip();
            if (position < text.Length)
            {
                throw Fail($"\"{text[position..]}\" is left over");
            }
        }

        private string Name()
        {
            Skip();
            var name = new StringBuilder();
            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_' or '-'))
            {
                name.Append(text[position++]);
            }

            return name.Length > 0 ? name.ToString() : throw Fail("a name is expected");
        }

        private bool Accept(char c)
        {
            Skip();
            if (position < text.Length && text[position] == c)
            {
                position++;
                return true;
            }

            return false;
        }

        private void Expect(char c)
        {
            if (!Accept(c))
            {
                throw Fail($"'{c}' is expected");
            }
        }

        private void Skip()
        {
            while (position < text.Length && text[position] == ' ')
            {
                position++;
            }
        }

        private FormatException Fail(string why) => new($"{at}: \"{text}\" is not a type: {why}.");
    }
}
