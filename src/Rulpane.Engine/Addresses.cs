namespace Rulpane.Engine;

/// <summary>
/// Where Rulpane reads what it does not ship, each from the place that publishes it: the
/// Rulealize Registry says which rule sets exist and which versions of them are withheld,
/// nuget.org holds their packages, and the Gallery serves its own releases. A link is read
/// against these as it is opened, so a release or a version is there as soon as they publish it,
/// and gone as soon as they withhold it.
/// </summary>
/// <remarks>
/// A tool that reads samples from a checkout, or a viewer served beside its own copy of the
/// Gallery, sets these before anything is read; each ends in <c>/</c>.
/// </remarks>
public static class Addresses
{
    /// <summary>The Rulealize Registry's site, whose <c>index.json</c> and <c>ruleset/&lt;id&gt;.json</c> are read.</summary>
    public static Uri Registry { get; set; } = new("https://reny-develop.github.io/Rulealize.Registry/");

    /// <summary>The flat container of the feed the Registry indexes, where each package is.</summary>
    public static Uri Feed { get; set; } = new("https://api.nuget.org/v3-flatcontainer/");

    /// <summary>The Gallery's site, which serves each release as <c>release/&lt;owner&gt;/&lt;name&gt;/&lt;version&gt;.json</c>.</summary>
    public static Uri Gallery { get; set; } = new("https://reny-develop.github.io/Rulpane.Gallery/");
}
