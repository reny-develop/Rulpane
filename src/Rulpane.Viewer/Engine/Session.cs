using System.Text.Json.Nodes;
using Rulealize;

namespace Rulpane.Viewer.Engine;

/// <summary>One compiled rule set and the position it is at.</summary>
public sealed class Session
{
    // Enough for a full board's worth of candidates; a rule set asking for more is told so.
    public const int ValidationLimit = 4096;
    public const int OutcomeLimit = 1024;

    private Session(RuleContext context, string state)
    {
        Context = context;
        State = state;
    }

    public RuleContext Context { get; }

    /// <summary>The state document.</summary>
    public string State { get; }

    /// <summary>
    /// <paramref name="packedData"/> is the state's <c>data</c> only: the frame around it is
    /// rebuilt from the rule set, which saves its length in every link.
    /// </summary>
    public static Session Open(RuleContext context, string? packedData)
    {
        if (string.IsNullOrEmpty(packedData))
        {
            return new Session(context, context.InitialState);
        }

        var document = new JsonObject
        {
            ["ruleSet"] = context.RuleSet,
            ["data"] = JsonNode.Parse(Fragment.Unpack(packedData)),
        };

        return new Session(context, document.ToJsonString());
    }

    public static string PackData(string stateDocument) =>
        Fragment.Pack(JsonNode.Parse(stateDocument)!["data"]!.ToJsonString());

    public TerminalStatus Terminal => Context.GetTerminalStatus(State);

    public ValidInputSet ValidInputs() => Context.GetValidInputs(State, ValidationLimit);

    public JsonNode? Project(string name) => JsonNode.Parse(Context.Project(name, State));

    public JsonNode? Data => JsonNode.Parse(State)?["data"];

    /// <summary>
    /// Applies a move. Where the rules draw, the outcome is picked here — the runtime only
    /// enumerates them, and the host is the one place anything is rolled.
    /// </summary>
    public Outcome Apply(ValidInput move, IReadOnlyDictionary<string, JsonNode?>? open = null)
    {
        string input = open is null
            ? move.ToInputDocument(Context.RuleSet)
            : move.ToInputDocument(Context.RuleSet, open);

        OutcomeSet outcomes = Context.GetOutcomes(input, State, OutcomeLimit);
        if (outcomes.Count == 1)
        {
            return outcomes[0];
        }

        double roll = Random.Shared.NextDouble() * outcomes.Sum(o => o.Probability);
        foreach (Outcome outcome in outcomes)
        {
            roll -= outcome.Probability;
            if (roll < 0)
            {
                return outcome;
            }
        }

        return outcomes[outcomes.Count - 1];
    }
}
