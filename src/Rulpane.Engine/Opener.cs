using Rulealize;

namespace Rulpane.Engine;

/// <summary>
/// What a link opens: the paneset it names with its own keys put over it, the rules compiled,
/// the view read against them, the theme, and the position, every value checked against the view.
/// </summary>
/// <param name="Linked">What the link says of itself.</param>
/// <param name="PaneSource">The paneset its <c>p</c> names, if it names one.</param>
/// <param name="Rules">Where the rules come from.</param>
/// <param name="ViewSource">Where the view comes from, if there is one.</param>
/// <param name="ThemeSource">Where the theme comes from, if there is one.</param>
/// <param name="View">The view, read against the rules and the bindings.</param>
/// <param name="Theme">The theme.</param>
/// <param name="Session">The rules at the position the link gives, or their start.</param>
/// <param name="Held">What the rules hold through <c>uses</c>.</param>
/// <param name="Gallery">The releases from the Gallery the screen is made with.</param>
/// <param name="Texts">Each document as its author wrote it.</param>
public sealed record Opened(
    Paneset Linked,
    Source? PaneSource,
    Source Rules,
    Source? ViewSource,
    Source? ThemeSource,
    View? View,
    Theme? Theme,
    Session Session,
    IReadOnlyList<Document> Held,
    IReadOnlyList<Published> Gallery,
    IReadOnlyDictionary<Source, string> Texts);

/// <summary>
/// Opens links, keeping what it has read and compiled: a rule set does not change while its
/// state does, so the next position of the same link costs only the position.
/// </summary>
public sealed class Opener(HttpClient http, RuleRuntime runtime)
{
    private readonly Dictionary<string, RuleContext> compiled = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Rules, string View, string Bind), View> views = [];
    private readonly Dictionary<string, Theme> themes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Paneset> panesets = new(StringComparer.Ordinal);
    private readonly Dictionary<Source, string> texts = [];
    private readonly Dictionary<string, IReadOnlyList<Document>> holds = new(StringComparer.Ordinal);

    /// <summary>Opens the screen the keys of a link give.</summary>
    /// <exception cref="FormatException">A key does not read, or names nothing that exists.</exception>
    public async Task<Opened> OpenAsync(IReadOnlyDictionary<string, string> link)
    {
        Paneset linked = Paneset.FromLink(link);
        var gallery = new List<Published>();
        Paneset ground = Paneset.Empty;
        Source? paneSource = null;
        if (link.TryGetValue("p", out string? named))
        {
            paneSource = Source.Paneset(named);
            if (!panesets.TryGetValue(named, out ground!))
            {
                texts[paneSource] = await paneSource.ReadAsync(http);
                ground = Paneset.Parse(texts[paneSource]);
                panesets[named] = ground;
            }

            if (paneSource.IsGallery)
            {
                gallery.Add(await paneSource.PublishedAsync(http));
            }
        }

        Paneset screen = ground.Over(linked);
        string rules = screen.Rules ?? throw new FormatException("r: the paneset names no rules, and the link gives none.");
        Source source = Source.Rules(rules);
        if (!compiled.TryGetValue(rules, out RuleContext? context))
        {
            texts[source] = await source.ReadAsync(http);
            holds[rules] = await source.HoldsAsync(http, texts[source]);
            context = Registry.Compile(runtime, texts[source], holds[rules]);
            compiled[rules] = context;
        }

        Source? viewSource = null;
        View? view = null;
        if (screen.View is string layout)
        {
            viewSource = Source.View(layout);
            string bind = screen.Bind.Count == 0 ? "" : Paneset.PackBind(screen.Bind);
            if (!views.TryGetValue((rules, layout, bind), out view))
            {
                texts[viewSource] = await viewSource.ReadAsync(http);
                view = View.Parse(texts[viewSource], context, texts[source], screen.Bind);
                views[(rules, layout, bind)] = view;
            }

            if (viewSource.IsGallery)
            {
                gallery.Add(await viewSource.PublishedAsync(http));
            }
        }
        else if (screen.Bind.Count > 0)
        {
            throw new FormatException("b: there is no view for it to bind; v names none.");
        }

        Source? themeSource = null;
        Theme? theme = null;
        if (screen.Theme is string look)
        {
            themeSource = Source.Theme(look);
            if (!themes.TryGetValue(look, out theme))
            {
                texts[themeSource] = await themeSource.ReadAsync(http);
                theme = Theme.Parse(texts[themeSource]);
                themes[look] = theme;
            }

            if (themeSource.IsGallery)
            {
                gallery.Add(await themeSource.PublishedAsync(http));
            }
        }

        Session session = Session.Open(context, screen.State);
        Dictionary<string, System.Text.Json.Nodes.JsonNode?> projections = session.Context.Projections.ToDictionary(name => name, session.Project);
        view?.Check(name => projections.GetValueOrDefault(name), session.Data);
        return new Opened(linked, paneSource, source, viewSource, themeSource, view, theme, session, holds[rules], gallery, texts);
    }
}
