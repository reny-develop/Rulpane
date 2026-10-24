using Rulealize;
using Rulealize.Plugin.Arithmetic;
using Rulealize.Plugin.Binding;
using Rulealize.Plugin.Branch;
using Rulealize.Plugin.Chance;
using Rulealize.Plugin.Comparison;
using Rulealize.Plugin.Deck;
using Rulealize.Plugin.Definition;
using Rulealize.Plugin.Graph;
using Rulealize.Plugin.Grid;
using Rulealize.Plugin.Logic;
using Rulealize.Plugin.Record;
using Rulealize.Plugin.Secret;
using Rulealize.Plugin.Sequence;
using Rulealize.Plugin.State;
using Rulealize.Plugin.Text;
using Rulealize.Plugin.Tuple;
using Rulealize.Plugin.TypeSchema;

namespace Rulpane.Viewer.Engine;

/// <summary>
/// The vocabulary a URL may draw on. Plugins are added by instance, never loaded from anything
/// a URL names: a rule set that arrives in a link is data, and this list is the whole of what
/// that data can reach.
/// </summary>
public static class CuratedRuntime
{
    public static RuleRuntime Create() => new RuleRuntime()
        .AddPlugin(new ArithmeticPlugin())
        .AddPlugin(new BindingPlugin())
        .AddPlugin(new BranchPlugin())
        .AddPlugin(new ChancePlugin())
        .AddPlugin(new ComparisonPlugin())
        .AddPlugin(new DeckPlugin())
        .AddPlugin(new DefinitionPlugin())
        .AddPlugin(new GraphPlugin())
        .AddPlugin(new GridPlugin())
        .AddPlugin(new LogicPlugin())
        .AddPlugin(new RecordPlugin())
        .AddPlugin(new SecretPlugin())
        .AddPlugin(new SequencePlugin())
        .AddPlugin(new StatePlugin())
        .AddPlugin(new TextPlugin())
        .AddPlugin(new TuplePlugin())
        .AddPlugin(new TypeSchemaPlugin());
}
