using System.Text.Json.Nodes;

namespace Rulpane.Engine;

/// <summary>
/// The size and coordinate notation of a Grid board. A board's state holds only the squares
/// that have something on them, so its shape comes from the <c>grid.board</c> schema the rules
/// give the field.
/// </summary>
public sealed record BoardShape(int Width, int Height, bool Algebraic)
{
    /// <summary>The shape a <c>grid.board</c> schema node states; null for any other node.</summary>
    public static BoardShape? Of(JsonNode? schema)
    {
        if (schema is not JsonObject node || node["op"]?.GetValue<string>() != "grid.board")
        {
            return null;
        }

        return new BoardShape(
            node["width"]!.GetValue<int>(),
            node["height"]!.GetValue<int>(),
            node["coord"]?.GetValue<string>() == "algebraic");
    }

    // Grid's own conventions: algebraic puts a1 bottom-left, index puts 0,0 top-left.
    // Rows here count from the top, the order they are drawn in.

    public string Coord(int x, int row) => File(x) + (Algebraic ? "" : ",") + Rank(row);

    public string File(int x) => Algebraic ? ((char)('a' + x)).ToString() : x.ToString();

    public string Rank(int row) => (Algebraic ? Height - row : row).ToString();
}
