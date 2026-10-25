using System.Globalization;

namespace Rulpane.Engine;

/// <summary>
/// The language Rulpane's own words are in: Japanese for a browser that asks for it first,
/// English otherwise. Only the words Rulpane writes follow it; what a link carries is in the
/// one language its author wrote it in, and a document's errors stay in the language of its keys.
/// </summary>
public static class Language
{
    // The runtime takes its culture from the browser's first language before anything here runs.
    private static readonly bool Japanese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";

    /// <summary>The page's <c>lang</c>, which also picks the Japanese forms of the system font.</summary>
    public static string Code => Japanese ? "ja" : "en";

    /// <summary>The same words in each language, written side by side where they are shown.</summary>
    public static string Say(string english, string japanese) => Japanese ? japanese : english;
}
