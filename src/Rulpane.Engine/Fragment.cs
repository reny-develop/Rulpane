using System.IO.Compression;
using System.Text;

namespace Rulpane.Engine;

/// <summary>
/// Everything a screen is made from travels in the URL fragment, which the browser never sends
/// to a server: <c>#r=&lt;rules&gt;&amp;v=&lt;view&gt;&amp;t=&lt;theme&gt;&amp;s=&lt;state&gt;</c>.
/// </summary>
public static class Fragment
{
    /// <summary>Prefix of a value that is deflated and then base64url-encoded.</summary>
    public const string PackedPrefix = "z:";

    public static Dictionary<string, string> Parse(string fragment)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = part.IndexOf('=');
            if (separator > 0)
            {
                values[part[..separator]] = Uri.UnescapeDataString(part[(separator + 1)..]);
            }
        }

        return values;
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
