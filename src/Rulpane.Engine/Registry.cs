using System.Text.Json.Nodes;
using Rulealize;

namespace Rulpane.Engine;

/// <summary>One rule set document, under the identity it declares.</summary>
public sealed record Document(string Id, string Version, string Text);

/// <summary>A rule set the Rulealize Registry indexes, and the versions of it mirrored here.</summary>
public sealed record Registered(string Id, string? Description, IReadOnlyList<string> Versions)
{
    public string Latest => Versions[^1];
}

/// <summary>
/// The rule sets published to the Rulealize Registry, as <c>tool/Mirror</c> copied them beside
/// the viewer: nothing outside the page's own origin is ever fetched. What a rule set holds
/// through <c>uses</c> is read from here too, whichever way the rule set itself arrived.
/// </summary>
public static class Registry
{
    private const string Folder = "registry";

    private static IReadOnlyList<Registered>? index;
    private static readonly Dictionary<(string, string), Document[]> packages = [];

    public static async Task<IReadOnlyList<Registered>> IndexAsync(HttpClient http)
    {
        if (index is null)
        {
            string text;
            try
            {
                text = await http.GetStringAsync($"{Folder}/index.json");
            }
            catch (HttpRequestException e)
            {
                throw new InvalidOperationException("This copy of Rulpane has no copy of the Registry's rule sets.", e);
            }

            index = [.. JsonNode.Parse(text)!["ruleSets"]!.AsArray().Select(r => new Registered(
                r!["id"]!.GetValue<string>(),
                r["description"]?.GetValue<string>(),
                [.. r["versions"]!.AsArray().Select(v => v!.GetValue<string>())]))
                .Where(r => r.Versions.Count > 0)];
        }

        return index;
    }

    /// <summary>The rule set <paramref name="id"/> declares, at exactly <paramref name="version"/>, and the parts its package ships beside it.</summary>
    public static async Task<Document[]> PackageAsync(HttpClient http, string id, string version)
    {
        // A package identifier is not case-sensitive; the mirror's folder is, so the index's spelling is the one fetched.
        Registered entry = (await IndexAsync(http)).FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The Registry has no rule set {id}.");
        if (!entry.Versions.Contains(version))
        {
            throw new InvalidOperationException(
                $"The Registry has no version {version} of {entry.Id} that Rulpane can open; it has {string.Join(", ", entry.Versions)}. A version that is withheld is not opened.");
        }

        if (!packages.TryGetValue((entry.Id, version), out Document[]? documents))
        {
            JsonNode package = JsonNode.Parse(await http.GetStringAsync($"{Folder}/{entry.Id}/{version}.json"))!;
            documents = [.. package["documents"]!.AsArray().Select(d => new Document(
                d!["id"]!.GetValue<string>(), d["version"]!.GetValue<string>(), d["text"]!.GetValue<string>()))];
            packages[(entry.Id, version)] = documents;
        }

        return documents;
    }

    /// <summary>
    /// Every document reachable through the <c>uses</c> of <paramref name="rules"/>, in the order
    /// first named. One its own package ships (<paramref name="shipped"/>) is taken from there;
    /// any other is the lowest version the Registry has that satisfies what names it, as
    /// <see cref="RuleSetRequirement.Choose"/> decides, so a link resolves to the same documents
    /// however many versions are published after it was made.
    /// </summary>
    public static async Task<IReadOnlyList<Document>> HoldsAsync(HttpClient http, string rules, IEnumerable<Document>? shipped = null)
    {
        var pool = (shipped ?? []).ToDictionary(d => d.Id, StringComparer.Ordinal);
        var held = new Dictionary<string, Document>(StringComparer.Ordinal);
        var wanted = new Dictionary<string, List<RuleSetRequirement>>(StringComparer.Ordinal);
        var pending = new Queue<RuleSetRequirement>(RuleSetRequirement.ReadFrom(rules));

        while (pending.TryDequeue(out RuleSetRequirement? requirement))
        {
            wanted.TryAdd(requirement.RuleSet, []);
            wanted[requirement.RuleSet].Add(requirement);

            // One version of each identifier: a later constraint the first choice does not meet
            // is the compiler's to report, with the paths that disagree.
            if (held.ContainsKey(requirement.RuleSet))
            {
                continue;
            }

            if (!pool.TryGetValue(requirement.RuleSet, out Document? document))
            {
                Registered entry = (await IndexAsync(http)).FirstOrDefault(r => r.Id == requirement.RuleSet)
                    ?? throw new InvalidOperationException($"uses: {requirement.RuleSet} is not a rule set the Registry has.");
                Version? version = RuleSetRequirement.Choose(wanted[requirement.RuleSet], entry.Versions.Select(Version.Parse))
                    ?? throw new InvalidOperationException(
                        $"uses: no version of {requirement.RuleSet} the Registry has satisfies {string.Join(" and ", wanted[requirement.RuleSet].Select(w => w.Constraint ?? "any version"))}; it has {string.Join(", ", entry.Versions)}.");

                Document[] package = await PackageAsync(http, entry.Id, version.ToString(3));
                foreach (Document part in package.Skip(1))
                {
                    pool.TryAdd(part.Id, part);
                }

                document = package[0];
            }

            held[document.Id] = document;
            foreach (RuleSetRequirement next in RuleSetRequirement.ReadFrom(document.Text))
            {
                pending.Enqueue(next);
            }
        }

        return [.. held.Values];
    }

    /// <summary>Compiles <paramref name="rules"/> with every document it holds.</summary>
    public static RuleContext Compile(RuleRuntime runtime, string rules, IReadOnlyList<Document> held) =>
        held.Count == 0
            ? runtime.CreateContext(rules)
            : runtime.CreateContext(rules, held.ToDictionary(d => d.Id, d => d.Text, StringComparer.Ordinal));
}
