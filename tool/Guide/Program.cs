// Writes the guide to writing a paneset where a person or an AI reading the site finds it, and
// holds what it points at to what the viewer runs.
//
//   dotnet run --project tool/Guide              write llms.txt and the guide in index.html
//   dotnet run --project tool/Guide -- --check   fail if either is behind doc/guide*.md, or an example does not open
//
// doc/guide.md and doc/guide.ja.md are the short guide people see, one per language the start page
// speaks; doc/guide.ai.md is the longer one for an AI, which the page never shows. All three go out as
// HTML in wwwroot/index.html between <!-- guide en --> and <!-- /guide en --> (and ja, and ai),
// outside the app, so a reader that fetches the page without running it — which is how an AI most
// often reads a page, styles unapplied — reads them; the page shows the one in its own language. The
// AI's and the English one also go out together as wwwroot/llms.txt.
//
// No guide holds the vocabulary: they say where to look it up, in the Registry and the Gallery. The
// AI's lists the plugins, filled in from the runtime. What they hold is checked against the code that
// will run it:
//   - every operation a guide writes as "op": "…" is one the runtime registers;
//   - every example under ```json paneset in the guide opens as a link would, and plays from its
//     start — the first move each time, up to 24, every position checked against the view — and
//     every link under ```text link opens. The start page's samples are the Gallery's, which holds
//     them to the same when they are submitted and every week after.

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Markdig;
using Rulealize;
using Rulpane.Engine;

bool check = args is ["--check"];
if (args.Length > 0 && !check)
{
    Console.Error.WriteLine("usage: dotnet run --project tool/Guide [-- --check]");
    return 2;
}

string root = FindRoot();
string wwwroot = Path.Combine(root, "src", "Rulpane.Viewer", "wwwroot");
string llms = Path.Combine(wwwroot, "llms.txt");
string index = Path.Combine(wwwroot, "index.html");
RuleRuntime runtime = CuratedRuntime.Create();

// The plugins, and the version a rule set should require of each, are the runtime's own.
string requires = string.Join("\n", runtime.Plugins.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p =>
    $"| `{p.Id}` (`{p.Namespace}.`) | `{{ \"plugin\": \"{p.Id}\", \"version\": \"^{p.Version.Major}.{p.Version.Minor}\" }}` |"));
var guides = new[] { ("ai", "guide.ai.md"), ("en", "guide.md"), ("ja", "guide.ja.md") }
    .Select(g => (Lang: g.Item1, File: g.Item2, Text: File.ReadAllText(Path.Combine(root, "doc", g.Item2)).ReplaceLineEndings("\n").Replace("<!-- requires -->", requires)))
    .ToArray();

var problems = new List<string>();
var registered = runtime.Operations.Select(o => o.Op).ToHashSet(StringComparer.Ordinal);
foreach (var (_, file, text) in guides)
{
    problems.AddRange(Regex.Matches(text, @"""op"":\s*""([a-z]+\.[a-zA-Z]+)""").Select(m => m.Groups[1].Value)
        .Where(op => !registered.Contains(op)).Distinct().Order().Select(op => $"doc/{file} names {op}, which the runtime does not register"));
}

using var http = new HttpClient(new Files(wwwroot)) { BaseAddress = new Uri("http://guide/") };
var opener = new Opener(http, runtime);
int examples = 0;

foreach (var (_, file, text) in guides)
{
    foreach (Match block in Regex.Matches(text, "```(json paneset|text link)\n(.*?)\n```", RegexOptions.Singleline))
    {
        examples++;
        await Try($"doc/{file} {Line(text, block.Index)}", block.Groups[1].Value, block.Groups[2].Value);
    }
}

if (problems.Count > 0)
{
    problems.ForEach(Console.Error.WriteLine);
    return 1;
}

// The ::: lines are the page's cards; to a reader of plain text they are noise.
string plain = string.Join("\n", guides.Where(g => g.Lang != "ja").Select(g => Regex.Replace(g.Text, @"^:::.*\n", "", RegexOptions.Multiline)));
string page = File.ReadAllText(index).ReplaceLineEndings("\n");
string written = page;
var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
foreach (var (lang, _, text) in guides)
{
    var marked = new Regex($"(<!-- guide {lang} -->\n)(?:.*?\n)?( *<!-- /guide {lang} -->)", RegexOptions.Singleline);
    if (!marked.IsMatch(written))
    {
        Console.Error.WriteLine($"index.html has no <!-- guide {lang} --> … <!-- /guide {lang} --> to write the guide between");
        return 1;
    }

    string html = Markdown.ToHtml(text, pipeline).TrimEnd();
    written = marked.Replace(written, m => m.Groups[1].Value + html + "\n" + m.Groups[2].Value);
}

if (check)
{
    var stale = new List<string>();
    if (!File.Exists(llms) || File.ReadAllText(llms).ReplaceLineEndings("\n") != plain)
    {
        stale.Add("llms.txt");
    }

    if (page != written)
    {
        stale.Add("index.html");
    }

    stale.ForEach(f => Console.Error.WriteLine($"{f} is behind doc/guide*.md: run dotnet run --project tool/Guide"));
    Console.WriteLine($"{examples} examples");
    return stale.Count > 0 ? 1 : 0;
}

File.WriteAllText(llms, plain);
File.WriteAllText(index, written);
Console.WriteLine($"{examples} examples → llms.txt, index.html");
return 0;

async Task Try(string where, string kind, string text)
{
    string fragment = kind == "text link"
        ? text[(text.IndexOf('#') is var hash and >= 0 ? hash : throw new InvalidOperationException($"a link with no '#': {text}"))..]
        : Fragment.Build(("p", Fragment.Pack(JsonNode.Parse(text, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.ToJsonString())));
    try
    {
        Opened opened = await opener.OpenAsync(Fragment.Parse(fragment));
        Play(opened);
    }
    catch (Exception e)
    {
        problems.Add($"{where} does not open: {e.Message}");
    }
}

// The first move each time, as the Gallery tries a release: a view that breaks at some position
// breaks there for whoever reaches it.
static void Play(Opened opened)
{
    Session session = opened.Session;
    for (int move = 0; move < 24 && !session.Terminal.IsTerminal; move++)
    {
        ValidInput? next = session.ValidInputs().FirstOrDefault(i => i.IsComplete);
        if (next is null)
        {
            return;
        }

        session = Session.Open(session.Context, Session.PackData(session.Apply(next, null).Result.State));
        var projections = session.Context.Projections.ToDictionary(name => name, session.Project);
        opened.View?.Check(name => projections.GetValueOrDefault(name), session.Data);
    }
}

static string Line(string text, int at) => $"line {text[..at].Count(c => c == '\n') + 1}";

static string FindRoot()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Rulpane.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("Run it inside the Rulpane repository.");
}

// The viewer's own files, served as its origin serves them.
sealed class Files(string root) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, Uri.UnescapeDataString(request.RequestUri!.AbsolutePath.TrimStart('/')));
        return Task.FromResult(File.Exists(path)
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(path)) }
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
