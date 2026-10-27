using System.Text.Json.Nodes;
using Rulealize;

namespace Rulpane.Engine;

/// <summary>
/// What a view calls the names the rules use, in the one language its author writes in. Each
/// kind is its own map, so the input <c>approve</c> and the value <c>approve</c> never meet;
/// a name the view leaves out is shown as the rules spell it.
/// </summary>
public sealed record Names(
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyDictionary<string, string> Codes,
    IReadOnlyDictionary<string, string> Values)
{
    /// <summary>Every name as the rules spell it: the screen without a view, or a view that names nothing.</summary>
    public static readonly Names None = new(
        new Dictionary<string, string>(), new Dictionary<string, string>(),
        new Dictionary<string, string>(), new Dictionary<string, string>());

    /// <summary>These names, with each that <paramref name="other"/> gives replaced by its: a view's own over its template's.</summary>
    public Names Over(Names other) => new(
        Merge(Inputs, other.Inputs), Merge(Params, other.Params), Merge(Codes, other.Codes), Merge(Values, other.Values));

    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> under, IReadOnlyDictionary<string, string> over) =>
        under.Concat(over).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value);

    public string Input(string name) => Inputs.GetValueOrDefault(name, name);

    public string Param(string name) => Params.GetValueOrDefault(name, name);

    /// <summary>What a refusal says; null when the view leaves the code as the rules wrote it.</summary>
    public string? Code(string code) => Codes.GetValueOrDefault(code);

    /// <summary>A value's name, looked up by the text the screen would otherwise show.</summary>
    public string Value(string value) => Values.GetValueOrDefault(value, value);

    /// <summary>
    /// An argument, a draw or a result as the runtime renders it, quoted or not, put on screen. A
    /// tuple is its parts in order, each named, those named empty left out: a move <c>e2|e4|-</c>
    /// whose <c>-</c> the view calls nothing reads <c>e2 → e4</c>.
    /// </summary>
    public string Rendered(string text) => Display.Parts(text) is { Count: > 1 } parts
        ? string.Join(" → ", parts.Select(Value).Where(v => v.Length > 0))
        : Value(Display.Unquote(text));

    /// <summary>
    /// A move as the last-move line and a square's tooltip give it: <c>reject(reason: scope)</c>,
    /// with the values typed into its open parameters when it was played with them.
    /// </summary>
    public string Describe(ValidInput move, IReadOnlyDictionary<string, JsonNode?>? open = null)
    {
        var arguments = move.Arguments.Select(a => $"{Param(a.Key)}: {Rendered(a.Value)}")
            .Concat((open ?? new Dictionary<string, JsonNode?>()).Select(o => $"{Param(o.Key)}: {(o.Value is null ? "null" : Rendered(o.Value.ToJsonString()))}"))
            .ToList();
        return arguments.Count == 0 ? Input(move.Input) : $"{Input(move.Input)}({string.Join(", ", arguments)})";
    }
}
