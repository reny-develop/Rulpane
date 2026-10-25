// Copies every rule set the Rulealize Registry indexes into a folder the viewer is served from,
// so that `r=<id>@<version>` can be read from Rulpane's own origin.
//
//   dotnet run --project tool/Mirror -- <folder> [<registry>]
//
// The Registry's catalogue says which versions exist and which are withheld; the documents
// themselves are inside the packages on nuget.org, which a page cannot fetch (the CSP lets it
// reach nothing but its own origin). A withheld version is left out, so a link to one stops
// opening the next time this runs.
//
// It writes
//   <folder>/index.json             every rule set, its description and the versions mirrored
//   <folder>/<id>/<version>.json    the documents in that package, its own first

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rulealize;

const string FlatContainer = "https://api.nuget.org/v3-flatcontainer";

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("usage: Rulpane.Mirror <folder> [<registry>]");
    return 2;
}

string output = args[0];
string registry = (args.Length > 1 ? args[1] : "https://reny-develop.github.io/Rulealize.Registry/").TrimEnd('/') + "/";

using var http = new HttpClient();

JsonNode index = await GetJson($"{registry}index.json");
var entries = new JsonArray();

if (Directory.Exists(output))
{
    Directory.Delete(output, recursive: true);
}

Directory.CreateDirectory(output);

foreach (JsonNode? summary in index["ruleSets"]?.AsArray() ?? [])
{
    string id = summary!["id"]!.GetValue<string>();
    JsonNode entry = await GetJson($"{registry}ruleset/{id}.json");

    var versions = new JsonArray();
    foreach (JsonNode? release in entry["versions"]!.AsArray())
    {
        string version = release!["version"]!.GetValue<string>();
        if (release["withheld"] is not null)
        {
            Console.Error.WriteLine($"{id} {version}: withheld, not mirrored");
            continue;
        }

        JsonArray documents = await Documents(id, version);
        Directory.CreateDirectory(Path.Combine(output, id));
        await File.WriteAllTextAsync(
            Path.Combine(output, id, $"{version}.json"),
            new JsonObject { ["id"] = id, ["version"] = version, ["documents"] = documents }.ToJsonString());

        versions.Add(version);
        Console.Error.WriteLine($"{id} {version}: {documents.Count} document(s)");
    }

    entries.Add(new JsonObject
    {
        ["id"] = id,
        ["description"] = entry["description"]?.GetValue<string>(),
        ["versions"] = versions,
    });
}

await File.WriteAllTextAsync(
    Path.Combine(output, "index.json"),
    new JsonObject
    {
        ["$schema"] = "rulpane/registry/v1",
        // When the Registry last read nuget.org, which is how old what is mirrored here may be.
        ["checked"] = index["checked"]?.GetValue<string>(),
        ["ruleSets"] = entries,
    }.ToJsonString());

Console.Error.WriteLine($"{entries.Count} rule set(s) → {output}");
return 0;

async Task<JsonNode> GetJson(string url) =>
    JsonNode.Parse(await http.GetStringAsync(url))
    ?? throw new InvalidDataException($"{url} is not a JSON document.");

// The documents of `ruleset/` as they are, comments and all: the viewer shows them to whoever
// asks to read the rules. Each is keyed by the identifier it declares, which is what a `uses`
// names, and the one declaring the package's own identifier comes first.
async Task<JsonArray> Documents(string id, string version)
{
    string lower = id.ToLowerInvariant();
    using var archive = new ZipArchive(await http.GetStreamAsync($"{FlatContainer}/{lower}/{version}/{lower}.{version}.nupkg"));

    var documents = new List<(RuleSetIdentity Identity, string Text)>();
    foreach (ZipArchiveEntry file in archive.Entries.Where(static file =>
        file.FullName.StartsWith("ruleset/", StringComparison.OrdinalIgnoreCase)
        && file.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        && file.FullName.Count(static c => c is '/') is 1))
    {
        using var reader = new StreamReader(file.Open());
        string text = await reader.ReadToEndAsync();
        documents.Add((RuleSetIdentity.ReadFrom(text), text));
    }

    // The catalogue withholds a package whose document says otherwise, so this is the
    // catalogue and the package disagreeing, which nothing downstream should paper over.
    if (!documents.Any(d => d.Identity.Id == id && d.Identity.Version == version))
    {
        throw new InvalidDataException($"{id} {version} holds no document declaring {id}@{version}.");
    }

    return [.. documents
        .OrderBy(d => d.Identity.Id == id ? 0 : 1)
        .ThenBy(d => d.Identity.Id, StringComparer.Ordinal)
        .Select(d => new JsonObject { ["id"] = d.Identity.Id, ["version"] = d.Identity.Version, ["text"] = d.Text })];
}
