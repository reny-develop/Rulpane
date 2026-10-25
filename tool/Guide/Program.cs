// Writes the guide to writing a paneset where a person or an AI reading the site finds it, and
// holds it to what the viewer runs.
//
//   dotnet run --project tool/Guide              write llms.txt and the guide in index.html
//   dotnet run --project tool/Guide -- --check   fail if either is behind doc/guide.md, or the guide is wrong
//
// doc/guide.md is the one source; the table of plugins in it is filled in from the runtime. It goes out twice: as wwwroot/llms.txt, as it is, and as HTML
// in wwwroot/index.html between <!-- guide --> and <!-- /guide -->, outside the app, so a reader
// that fetches the page without running it — which is how an AI reads a page — reads it too.
//
// Before writing either, it checks what the guide says against the code that will run it:
//   - every operation it writes is one the curated runtime registers, and its table lists every one;
//   - every example under ```json paneset opens as a link would, and plays from its start — the
//     first move each time, up to 24, every position checked against the view;
//   - every link under ```text link opens.

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
string source = Path.Combine(root, "doc", "guide.md");
string wwwroot = Path.Combine(root, "src", "Rulpane.Viewer", "wwwroot");
string llms = Path.Combine(wwwroot, "llms.txt");
string index = Path.Combine(wwwroot, "index.html");
string guide = File.ReadAllText(source).ReplaceLineEndings("\n");

var problems = new List<string>();
RuleRuntime runtime = CuratedRuntime.Create();

// The plugins, and the version a rule set should require of each, are the runtime's own.
guide = guide.Replace("<!-- requires -->", string.Join("\n", runtime.Plugins.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p =>
    $"| `{p.Id}` (`{p.Namespace}.`) | `{{ \"plugin\": \"{p.Id}\", \"version\": \"^{p.Version.Major}.{p.Version.Minor}\" }}` |")));

// The operations: each one the guide writes as "op": "…" or lists in a table's first column,
// against what the runtime registers.
var registered = runtime.Operations.Select(o => o.Op).ToHashSet(StringComparer.Ordinal);
var named = Regex.Matches(guide, @"(?:""op"":\s*""|^\| `)([a-z]+\.[a-zA-Z]+)", RegexOptions.Multiline)
    .Select(m => m.Groups[1].Value)
    .ToHashSet(StringComparer.Ordinal);
problems.AddRange(named.Except(registered).Order().Select(op => $"names {op}, which the runtime does not register"));
problems.AddRange(registered.Except(named).Order().Select(op => $"never names {op}, which the runtime registers"));

// The examples, opened as the viewer opens a link, over the bundled documents.
using var http = new HttpClient(new Files(wwwroot)) { BaseAddress = new Uri("http://guide/") };
var opener = new Opener(http, runtime);
int examples = 0;
foreach (Match block in Regex.Matches(guide, "```(json paneset|text link)\n(.*?)\n```", RegexOptions.Singleline))
{
    examples++;
    string text = block.Groups[2].Value;
    string fragment = block.Groups[1].Value == "text link"
        ? text[(text.IndexOf('#') is var hash and >= 0 ? hash : throw new InvalidOperationException($"a link with no '#': {text}"))..]
        : Fragment.Build(("p", Fragment.Pack(JsonNode.Parse(text, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.ToJsonString())));
    try
    {
        Opened opened = await opener.OpenAsync(Fragment.Parse(fragment));
        Play(opened);
    }
    catch (Exception e)
    {
        problems.Add($"example {examples} ({Line(block.Index)}) does not open: {e.Message}");
    }
}

if (problems.Count > 0)
{
    problems.ForEach(p => Console.Error.WriteLine($"doc/guide.md {p}"));
    return 1;
}

string html = Markdown.ToHtml(guide, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
string page = File.ReadAllText(index).ReplaceLineEndings("\n");
var marked = new Regex("(<!-- guide -->\n)(?:.*?\n)?( *<!-- /guide -->)", RegexOptions.Singleline);
if (!marked.IsMatch(page))
{
    Console.Error.WriteLine("index.html has no <!-- guide --> … <!-- /guide --> to write the guide between");
    return 1;
}

string written = marked.Replace(page.ReplaceLineEndings("\n"), m => m.Groups[1].Value + html.TrimEnd() + "\n" + m.Groups[2].Value);
if (check)
{
    var stale = new List<string>();
    if (!File.Exists(llms) || File.ReadAllText(llms).ReplaceLineEndings("\n") != guide)
    {
        stale.Add("llms.txt");
    }

    if (page.ReplaceLineEndings("\n") != written)
    {
        stale.Add("index.html");
    }

    stale.ForEach(f => Console.Error.WriteLine($"{f} is behind doc/guide.md: run dotnet run --project tool/Guide"));
    Console.WriteLine($"{named.Count} operations, {examples} examples");
    return stale.Count > 0 ? 1 : 0;
}

File.WriteAllText(llms, guide);
File.WriteAllText(index, written);
Console.WriteLine($"{named.Count} operations, {examples} examples → llms.txt, index.html");
return 0;

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

string Line(int at) => $"line {guide[..at].Count(c => c == '\n') + 1}";

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
