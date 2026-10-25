using System.Security.Cryptography;
using System.Text.Json.Nodes;

/// <summary>
/// Copies the Gallery's releases beside the viewer, out of a checkout of the Gallery
/// (Rulpane.Gallery): its ledger says which releases exist, what each is, and which are withheld,
/// and the copy beside each line is the document as its author committed it. A withheld release
/// is left out, so a link to one stops opening the next time this runs.
/// </summary>
internal static class Gallery
{
    public static async Task<int> MirrorAsync(string output, string gallery)
    {
        JsonNode ledger = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(gallery, "ledger", "submitted.json")))
            ?? throw new InvalidDataException("The Gallery's ledger is not a JSON document.");

        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }

        Directory.CreateDirectory(output);

        var versions = new Dictionary<string, (string Kind, JsonArray Versions)>(StringComparer.Ordinal);
        foreach (JsonNode? line in ledger["releases"]?.AsArray() ?? [])
        {
            string id = line!["id"]!.GetValue<string>();
            int version = line["version"]!.GetValue<int>();
            string kind = line["kind"]!.GetValue<string>();
            versions.TryAdd(id, (kind, []));
            if (line["withheld"] is not null)
            {
                Console.Error.WriteLine($"{id}@{version}: withheld, not mirrored");
                continue;
            }

            string file = Path.Combine(gallery, "release", id, version.ToString(), "document.json");
            byte[] bytes = await File.ReadAllBytesAsync(file);
            // The Gallery holds its copies to their lines on every push; one that disagrees here
            // is nothing to paper over.
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != line["sha256"]!.GetValue<string>())
            {
                throw new InvalidDataException($"{file} is not the document the Gallery's ledger recorded for {id}@{version}.");
            }

            Directory.CreateDirectory(Path.Combine(output, id));
            await File.WriteAllTextAsync(Path.Combine(output, id, $"{version}.json"), new JsonObject
            {
                ["id"] = id,
                ["version"] = version,
                ["kind"] = kind,
                ["repository"] = line["repository"]!.GetValue<string>(),
                ["commit"] = line["commit"]!.GetValue<string>(),
                ["path"] = line["path"]!.GetValue<string>(),
                ["license"] = line["license"]!.GetValue<string>(),
                ["text"] = System.Text.Encoding.UTF8.GetString(bytes),
            }.ToJsonString());
            versions[id].Versions.Add(version);
            Console.Error.WriteLine($"{kind} {id}@{version}");
        }

        await File.WriteAllTextAsync(
            Path.Combine(output, "index.json"),
            new JsonObject
            {
                ["$schema"] = "rulpane/gallery/v1",
                ["releases"] = new JsonArray([.. versions.Select(v => new JsonObject { ["id"] = v.Key, ["kind"] = v.Value.Kind, ["versions"] = v.Value.Versions })]),
            }.ToJsonString());

        Console.Error.WriteLine($"{versions.Count} name(s) → {output}");
        return 0;
    }
}
