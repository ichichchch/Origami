namespace Origami.Tests;

/// <summary>
/// 驗證英文資源與介面呼叫保持一致：每個被參照的鍵都要有翻譯，格式佔位符要相符，且不留下無人使用的條目。
/// </summary>
public sealed class UiTextResourceIntegrityTests
{
    private static readonly Regex KeyPattern = new(@"UiText\.T\(\s*""(?<key>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex LiteralPattern = new(
        @"UiText\.T\(\s*""(?<key>[^""]+)""\s*,\s*""(?<fallback>(?:\\.|[^""\\])*)""",
        RegexOptions.Compiled);
    private static readonly Regex PlaceholderPattern = new(@"(?<!\{)\{(?<index>\d+)(?:[^{}]*)\}(?!\})", RegexOptions.Compiled);

    [Fact]
    public void Every_referenced_key_has_an_english_translation()
    {
        var (referenced, translations) = Load();

        var missing = referenced.Keys
            .Where(key => !translations.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Translations_and_fallbacks_use_the_same_placeholders()
    {
        var (referenced, translations) = Load();

        var mismatched = referenced
            .Where(entry => entry.Value.Fallback is not null && translations.TryGetValue(entry.Key, out var translation))
            .Select(entry => (Key: entry.Key, Fallback: entry.Value.Fallback!, Translation: translations[entry.Key]))
            .Where(entry => !Placeholders(entry.Fallback).SetEquals(Placeholders(entry.Translation)))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}: fallback [{string.Join(",", Placeholders(entry.Fallback))}] translation [{string.Join(",", Placeholders(entry.Translation))}]")
            .ToList();

        Assert.Empty(mismatched);
    }

    [Fact]
    public void No_translation_is_left_without_a_caller()
    {
        var (referenced, translations) = Load();

        var orphans = translations.Keys
            .Where(key => !referenced.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void Every_translation_is_loadable_at_runtime_in_english()
    {
        var (referenced, _) = Load();
        var manager = new ResourceManager("Origami.Web.Resources.UiText", typeof(UiText).Assembly);
        var culture = CultureInfo.GetCultureInfo("en");

        var unresolved = referenced.Keys
            .Where(key => manager.GetString(key, culture) is null)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unresolved);
    }

    private static (Dictionary<string, CallSite> Referenced, Dictionary<string, string> Translations) Load()
    {
        var web = Path.Combine(RunningSite.FindSolutionRoot(), "src", "Origami.Web");
        var resx = Path.Combine(web, "Resources", "UiText.en.resx");
        Assert.True(File.Exists(resx), $"Missing resource file: {resx}");

        var document = XDocument.Load(resx);
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in document.Root!.Elements("data"))
        {
            var name = element.Attribute("name")!.Value;
            Assert.True(translations.TryAdd(name, element.Element("value")?.Value ?? string.Empty),
                $"Duplicate resource key: {name}");
        }

        var referenced = new Dictionary<string, CallSite>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(web, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (extension is not (".razor" or ".cs")) continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(path);
            var literals = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in LiteralPattern.Matches(text))
                literals.TryAdd(match.Groups["key"].Value, match.Groups["fallback"].Value);

            foreach (Match match in KeyPattern.Matches(text))
            {
                var key = match.Groups["key"].Value;
                string? fallback = literals.TryGetValue(key, out var literal) ? literal : null;
                referenced[key] = new CallSite(
                    key,
                    Path.GetRelativePath(web, path),
                    text.AsSpan(0, match.Index).Count('\n') + 1,
                    fallback);
            }
        }

        return (referenced, translations);
    }

    private static HashSet<string> Placeholders(string? value) =>
        value is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : PlaceholderPattern.Matches(value)
                .Cast<Match>()
                .Select(match => match.Groups["index"].Value)
                .ToHashSet(StringComparer.Ordinal);

    private sealed record CallSite(string Key, string File, int Line, string? Fallback);
}
