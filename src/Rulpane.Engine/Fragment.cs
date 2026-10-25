using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Rulpane.Engine;

/// <summary>
/// Everything a screen is made from travels in the URL fragment, which the browser never sends
/// to a server: <c>#r=&lt;rules&gt;&amp;v=&lt;view&gt;&amp;t=&lt;theme&gt;&amp;s=&lt;state&gt;</c>.
/// </summary>
public static class Fragment
{
    /// <summary>Prefix of a value that is deflated and then base64url-encoded.</summary>
    public const string PackedPrefix = "z:";

    /// <summary>
    /// The keys of a fragment and their values. A value that starts with <c>{</c> is a document
    /// written as it is, which somebody, or an AI, can type without packing it: it runs to the end
    /// of its JSON, whatever <c>&amp;</c> or <c>#</c> its strings hold, and is packed here, so
    /// everything after this reads one form and the link is rewritten short on the first move.
    /// </summary>
    public static Dictionary<string, string> Parse(string fragment)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string rest = fragment.TrimStart('#');
        while (rest.Length > 0)
        {
            int end = rest.IndexOf('&');
            string part = end < 0 ? rest : rest[..end];
            int separator = part.IndexOf('=');
            if (separator > 0 && ReadWritten(rest[(separator + 1)..]) is { } written)
            {
                values[part[..separator]] = Pack(written.Json);
                rest = written.After.StartsWith('&') ? written.After[1..] : written.After;
                continue;
            }

            if (separator > 0)
            {
                values[part[..separator]] = Uri.UnescapeDataString(part[(separator + 1)..]);
            }

            rest = end < 0 ? "" : rest[(end + 1)..];
        }

        return values;
    }

    /// <summary>How a document written in the link is read: comments and trailing commas are let through.</summary>
    public static readonly JsonReaderOptions Written = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // A browser escapes a typed space or quote in a fragment, so the text is unescaped before its
    // JSON is read. JSON that does not read is left as an ordinary value, for Source to refuse.
    private static (string Json, string After)? ReadWritten(string value)
    {
        string text = Uri.UnescapeDataString(value);
        if (!text.StartsWith('{'))
        {
            return null;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, Written);
        try
        {
            reader.Read();
            reader.Skip();
        }
        catch (JsonException)
        {
            return null;
        }

        int length = (int)reader.BytesConsumed;
        return (Encoding.UTF8.GetString(bytes, 0, length), Encoding.UTF8.GetString(bytes, length, bytes.Length - length));
    }

    public static string Build(params (string Key, string? Value)[] values) =>
        "#" + string.Join("&", values
            .Where(v => !string.IsNullOrEmpty(v.Value))
            .Select(v => v.Key + "=" + Escape(v.Value!)));

    public static string Pack(string text)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize))
        {
            deflate.Write(Encoding.UTF8.GetBytes(text));
        }

        return PackedPrefix + Convert.ToBase64String(buffer.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Unpack(string value)
    {
        if (!value.StartsWith(PackedPrefix, StringComparison.Ordinal))
        {
            throw new FormatException($"A packed value starts with '{PackedPrefix}'.");
        }

        string base64 = value[PackedPrefix.Length..].Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');

        using var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // Base64url, ':', '@' and '/' are left as they are, so a link stays readable and short: a
    // fragment may hold all of them, and a name such as octocat/kanban@1 reads as written.
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' or ':' or '@' or '/')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(Uri.EscapeDataString(c.ToString()));
            }
        }

        return builder.ToString();
    }
}
