using System.Text.Json.Nodes;

namespace Rulpane.Engine;

/// <summary>How text the runtime renders in its JSON form is put on screen.</summary>
public static class Display
{
    /// <summary>A JSON string without its quotes; anything else as it is.</summary>
    public static string Unquote(string json) =>
        json.Length >= 2 && json[0] == '"' && json[^1] == '"' ? JsonNode.Parse(json)!.GetValue<string>() : json;

    /// <summary>The parts of a tuple as the runtime renders one, <c>e2|e4|-</c>; anything else is one part.</summary>
    public static IReadOnlyList<string> Parts(string rendered) => Unquote(rendered).Split('|');
}
