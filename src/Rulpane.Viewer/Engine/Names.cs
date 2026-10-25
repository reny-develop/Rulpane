using System.Text.Json.Nodes;
using Rulealize;

namespace Rulpane.Viewer.Engine;

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

    public string Input(string name) => Inputs.GetValueOrDefault(name, name);

    public string Param(string name) => Params.GetValueOrDefault(name, name);

    /// <summary>What a refusal says; null when the view leaves the code as the rules wrote it.</summary>
    public string? Code(string code) => Codes.GetValueOrDefault(code);

    /// <summary>A value's name, looked up by the text the screen would otherwise show.</summary>
    public string Value(string value) => Values.GetValueOrDefault(value, value);

    /// <summary>An argument, a draw or a result as the runtime renders it, quoted or not, put on screen.</summary>
    public string Rendered(string text) => Value(Display.Unquote(text));

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
