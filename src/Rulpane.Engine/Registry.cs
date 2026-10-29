using System.IO.Compression;
using System.Text.Json.Nodes;
using Rulealize;

namespace Rulpane.Engine;

/// <summary>One rule set document, under the identity it declares.</summary>
public sealed record Document(string Id, string Version, string Text);

/// <summary>A rule set the Rulealize Registry indexes, and the versions of it that are not withheld.</summary>
public sealed record Registered(string Id, string? Description, IReadOnlyList<string> Versions)
{
    public string Latest => Versions[^1];
}

/// <summary>
/// The rule sets published to the Rulealize Registry, read as a link is opened: the Registry
/// says which versions exist and which are withheld, and each document comes out of its package
/// on the feed, where its publisher put it. What a rule set holds through <c>uses</c> is read
/// from here too, whichever way the rule set itself arrived.
/// </summary>
public static class Registry
{
    private static IReadOnlyList<string>? ids;
    private static readonly Dictionary<string, Registered?> entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<(string, string), Document[]> packages = [];

    /// <summary>
    /// The rule set the Registry indexes as <paramref name="id"/>, under its own spelling, and
    /// its versions that are not withheld; null when it indexes none.
    /// </summary>
    public static async Task<Registered?> EntryAsync(HttpClient http, string id)
    {
        // A package identifier is not case-sensitive; the Registry's file names are, so its own spelling is the one fetched.
        ids ??= [.. (await ReadAsync(http, new Uri(Addresses.Registry, "index.json")))["ruleSets"]!.AsArray()
            .Select(r => r!["id"]!.GetValue<string>())];
        if (ids.FirstOrDefault(i => string.Equals(i, id, StringComparison.OrdinalIgnoreCase)) is not string spelled)
        {
            return null;
        }

        if (!entries.TryGetValue(spelled, out Registered? entry))
        {
            JsonNode page = await ReadAsync(http, new Uri(Addresses.Registry, $"ruleset/{Uri.EscapeDataString(spelled)}.json"));
            entry = new Registered(
                spelled,
                page["description"]?.GetValue<string>(),
                [.. page["versions"]!.AsArray().Where(v => v!["withheld"] is null).Select(v => v!["version"]!.GetValue<string>())]);
            entries[spelled] = entry = entry.Versions.Count > 0 ? entry : null;
        }

        return entry;
    }

    /// <summary>The rule set <paramref name="id"/> declares, at exactly <paramref name="version"/>, and the parts its package ships beside it.</summary>
    public static async Task<Document[]> PackageAsync(HttpClient http, string id, string version)
    {
        Registered entry = await EntryAsync(http, id)
            ?? throw new InvalidOperationException($"The Registry has no rule set {id}.");
        if (!entry.Versions.Contains(version))
        {
            throw new InvalidOperationException(
                $"The Registry has no version {version} of {entry.Id} that Rulpane can open; it has {string.Join(", ", entry.Versions)}. A version that is withheld is not opened.");
        }

        if (!packages.TryGetValue((entry.Id, version), out Document[]? documents))
        {
            documents = await DocumentsAsync(http, entry.Id, version);
            packages[(entry.Id, version)] = documents;
        }

        return documents;
    }

    // The documents of the package's `ruleset/` as they are, comments and all: the viewer shows
    // them to whoever asks to read the rules. Each is keyed by the identifier it declares, which
    // is what a `uses` names, and the one declaring the package's own identifier comes first.
    private static async Task<Document[]> DocumentsAsync(HttpClient http, string id, string version)
    {
        string lower = id.ToLowerInvariant();
        Uri address = new(Addresses.Feed, $"{lower}/{version}/{lower}.{version}.nupkg");
        byte[] package;
        try
        {
            package = await http.GetByteArrayAsync(address);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"{id} {version} could not be read from {address.Host}: {e.Message}", e);
        }

        using var archive = new ZipArchive(new MemoryStream(package));
        var documents = new List<Document>();
        foreach (ZipArchiveEntry file in archive.Entries.Where(static file =>
            file.FullName.StartsWith("ruleset/", StringComparison.OrdinalIgnoreCase)
            && file.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && file.FullName.Count(static c => c is '/') is 1))
        {
            using var reader = new StreamReader(file.Open());
            string text = await reader.ReadToEndAsync();
            RuleSetIdentity identity = RuleSetIdentity.ReadFrom(text);
            documents.Add(new Document(identity.Id, identity.Version, text));
        }

        // The Registry withholds a package whose document says otherwise, so this is the
        // Registry and the package disagreeing, which nothing downstream should paper over.
        if (!documents.Any(d => d.Id == id && d.Version == version))
        {
            throw new InvalidOperationException($"{id} {version} holds no document declaring {id}@{version}.");
        }

        return [.. documents.OrderBy(d => d.Id == id ? 0 : 1).ThenBy(d => d.Id, StringComparer.Ordinal)];
    }

    private static async Task<JsonNode> ReadAsync(HttpClient http, Uri address)
    {
        try
        {
            return JsonNode.Parse(await http.GetStringAsync(address))!;
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"The Registry could not be read at {address}: {e.Message}", e);
        }
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
                Registered entry = await EntryAsync(http, requirement.RuleSet)
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
