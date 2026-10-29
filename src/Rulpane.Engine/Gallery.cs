using System.Text.Json.Nodes;

namespace Rulpane.Engine;

/// <summary>
/// One release published to the Gallery: a view, a theme or a paneset, as its author committed
/// it, and where it was read from. <see cref="Id"/> is <c>&lt;owner&gt;/&lt;name&gt;</c>, the owner
/// being the GitHub account that submitted it; <see cref="Version"/> is the Gallery's own count
/// of that name's releases.
/// </summary>
public sealed record Published(string Id, string Version, string Kind, string Repository, string Commit, string License, string Text)
{
    /// <summary>The GitHub account that published it, which is the one that owns <see cref="Repository"/>.</summary>
    public string Owner => Id[..Id.IndexOf('/')];

    /// <summary>What a link names it by: <c>octocat/kanban@1</c>.</summary>
    public string Reference => $"{Id}@{Version}";
}

/// <summary>
/// What the Gallery (Rulpane.Gallery) publishes, read from its own site as a link is opened, where
/// a withheld release is not served. A name is one thing — a view, a theme or a paneset — so a
/// name given where another kind is wanted is refused rather than read as something it is not.
/// </summary>
public static class Gallery
{
    private static readonly Dictionary<(string, string), Published> releases = [];

    /// <summary>The release <paramref name="id"/>@<paramref name="version"/>, which has to be a <paramref name="kind"/>.</summary>
    public static async Task<Published> ReadAsync(HttpClient http, string id, string version, string kind)
    {
        if (!releases.TryGetValue((id, version), out Published? release))
        {
            string text;
            try
            {
                text = await http.GetStringAsync(new Uri(Addresses.Gallery, $"release/{id}/{version}.json"));
            }
            catch (HttpRequestException e)
            {
                throw new InvalidOperationException(
                    $"The Gallery has no {id}@{version} that Rulpane can open. A release that is withheld is not opened.", e);
            }

            JsonNode entry = JsonNode.Parse(text)!;
            release = new Published(
                entry["id"]!.GetValue<string>(),
                entry["version"]!.GetValue<int>().ToString(),
                entry["kind"]!.GetValue<string>(),
                entry["repository"]!.GetValue<string>(),
                entry["commit"]!.GetValue<string>(),
                entry["license"]!.GetValue<string>(),
                entry["text"]!.GetValue<string>());
            releases[(id, version)] = release;
        }

        return release.Kind == kind
            ? release
            : throw new InvalidOperationException($"{id}@{version} is a {release.Kind} in the Gallery, not a {kind}.");
    }
}
