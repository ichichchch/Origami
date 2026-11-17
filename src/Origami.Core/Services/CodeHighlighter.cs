namespace Origami.Core.Services;

/// <summary>
/// 服務端程式碼高亮：把 fenced code block 的內容做輕量詞法著色，輸出僅含
/// <c>tok-*</c> class 的自產 span（在 sanitizer 之後處理，不經過也不繞過白名單）。
/// 每行包在 <c>cl</c> span 中，行號與語言標籤由 CSS 呈現，前端零腳本。
/// </summary>
public static class CodeHighlighter
{
    private const int MaxCodeLength = 100_000;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private sealed record Rule(string Class, string Pattern);
    private sealed record Language(string Label, Regex Regex, string[] GroupClasses);

    private static readonly Dictionary<string, Language> Languages = BuildLanguages();

    public static bool Supports(string language) => Languages.ContainsKey(Normalize(language));

    /// <summary>著色整個 code block；語言不支援、內容超長或逾時時回 null（呼叫端保留原樣）。</summary>
    public static string? Highlight(string language, string code)
    {
        if (code.Length == 0 || code.Length > MaxCodeLength) return null;
        if (!Languages.TryGetValue(Normalize(language), out var lang)) return null;
        try
        {
            return Emit(lang, code);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string Normalize(string language) => language.Trim().ToLowerInvariant().Trim('-', '_');

    private static string Emit(Language lang, string code)
    {
        var sb = new StringBuilder(code.Length * 2 + 96);
        sb.Append("<figure class=\"code-block\" data-lang=\"").Append(lang.Label).Append("\"><pre><code>");
        var writer = new LineWriter(sb);
        var position = 0;
        foreach (Match match in lang.Regex.Matches(code))
        {
            if (!match.Success) break;
            writer.Append(code, position, match.Index, null);
            writer.Append(code, match.Index, match.Index + match.Length, ClassOf(lang, match));
            position = match.Index + match.Length;
        }
        writer.Append(code, position, code.Length, null);
        writer.CloseLine();
        sb.Append("</code></pre></figure>");
        return sb.ToString();
    }

    private static string? ClassOf(Language lang, Match match)
    {
        for (var i = 0; i < lang.GroupClasses.Length; i++)
        {
            if (match.Groups["g" + i.ToString(CultureInfo.InvariantCulture)].Success)
                return lang.GroupClasses[i];
        }
        return null;
    }

    /// <summary>逐字輸出 [from, to)：以 <c>cl</c> span 分行，行內的 token class 跨行時逐行重開 span。</summary>
    private sealed class LineWriter(StringBuilder sb)
    {
        private bool _lineOpen;
        private string? _activeClass;

        public void Append(string code, int from, int to, string? cls)
        {
            for (var i = from; i < to; i++)
            {
                var ch = code[i];
                if (ch == '\r') continue;
                if (!_lineOpen)
                {
                    sb.Append("<span class=\"cl\">");
                    _lineOpen = true;
                    _activeClass = null;
                }
                if (ch == '\n')
                {
                    CloseToken();
                    sb.Append("</span>\n");
                    _lineOpen = false;
                    continue;
                }
                if (!string.Equals(_activeClass, cls, StringComparison.Ordinal))
                {
                    CloseToken();
                    if (cls is not null) sb.Append("<span class=\"").Append(cls).Append("\">");
                    _activeClass = cls;
                }
                sb.Append(ch switch
                {
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '&' => "&amp;",
                    _ => ch.ToString(),
                });
            }
        }

        public void CloseLine()
        {
            CloseToken();
            if (_lineOpen) sb.Append("</span>");
            _lineOpen = false;
        }

        private void CloseToken()
        {
            if (_activeClass is null) return;
            sb.Append("</span>");
            _activeClass = null;
        }
    }

    private static Dictionary<string, Language> BuildLanguages()
    {
        Rule Kw(string set) => new("tok-kw", $@"\b(?:{set})\b");
        Rule Num() => new("tok-num", @"\b(?:0[xX][0-9a-fA-F_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?)\b");
        Rule LineComment(string prefix) => new("tok-cm", $"{prefix}[^\\n]*");
        Rule BlockComment() => new("tok-cm", @"/\*[\s\S]*?\*/");
        Rule Dq() => new("tok-str", @"""(?:\\.|[^""\\\n])*""");
        Rule Sq() => new("tok-str", @"'(?:\\.|[^'\\\n])*'");

        var csharpKeywords = "abstract|as|async|await|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|dynamic|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|get|goto|if|implicit|in|init|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|partial|private|protected|public|readonly|record|ref|required|return|sbyte|sealed|set|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|when|where|while|yield";
        var jsKeywords = "async|await|break|case|catch|class|const|continue|debugger|default|delete|do|else|export|extends|finally|for|from|function|if|import|in|instanceof|let|new|of|return|static|super|switch|this|throw|try|typeof|var|void|while|with|yield|true|false|null|undefined";
        var tsExtra = "type|interface|enum|namespace|implements|readonly|declare|keyof|infer|satisfies|never|unknown|any|string|number|boolean|bigint|symbol|as|is|asserts";
        var sqlKeywords = "select|from|where|insert|into|values|update|set|delete|create|table|alter|drop|index|view|join|left|right|inner|outer|full|cross|on|and|or|not|null|is|in|like|between|exists|group|by|order|having|limit|offset|union|all|distinct|case|when|then|else|end|primary|key|foreign|references|default|unique|check|constraint|asc|desc|with|returning|top|count|sum|avg|min|max|truncate|begin|commit|rollback|transaction|declare|execute|exec|if|while|return|go|identity|partition|over|row_number|rank|dense_rank|lag|lead|int|bigint|smallint|tinyint|varchar|nvarchar|char|nchar|date|datetime|datetime2|smalldatetime|time|bit|uniqueidentifier|decimal|numeric|float|real|money|binary|varbinary|text|ntext|image|xml|json|cursor|output";
        var bashKeywords = "if|then|else|elif|fi|for|while|until|do|done|case|esac|function|in|select|time|return|break|continue|local|export|readonly|declare|typeset|unset|shift|exit|echo|printf|read|cd|ls|cp|mv|rm|mkdir|rmdir|touch|cat|grep|sed|awk|curl|wget|sudo|set|trap|source|alias|pushd|popd";
        var pythonKeywords = "and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|while|with|yield|True|False|None|match|case|self";

        var defs = new (string[] Names, string Label, Rule[] Rules, RegexOptions Options)[]
        {
            (["csharp", "cs"], "C#", [LineComment("//"), BlockComment(), new("tok-str", "@\"(?:\"\"|[^\"])*\""), new("tok-str", "\\$?\"(?:\\\\.|[^\"\\\\\n])*\""), Sq(), Num(), Kw(csharpKeywords), new("tok-typ", @"(?<![\w.])[A-Z]\w*\b")], RegexOptions.CultureInvariant),
            (["javascript", "js"], "JavaScript", [LineComment("//"), BlockComment(), Dq(), Sq(), new("tok-str", "`(?:\\\\.|[^`\\\\])*`"), Num(), Kw(jsKeywords), new("tok-typ", @"(?<![\w.])[A-Z]\w*\b")], RegexOptions.CultureInvariant),
            (["typescript", "ts"], "TypeScript", [LineComment("//"), BlockComment(), Dq(), Sq(), new("tok-str", "`(?:\\\\.|[^`\\\\])*`"), Num(), Kw($"{jsKeywords}|{tsExtra}"), new("tok-typ", @"(?<![\w.])[A-Z]\w*\b")], RegexOptions.CultureInvariant),
            (["sql"], "SQL", [LineComment("--"), BlockComment(), new("tok-str", "'(?:''|[^'])*'"), Num(), Kw(sqlKeywords)], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            (["json"], "JSON", [new("tok-typ", "\"(?:\\\\.|[^\"\\\\])*\"(?=\\s*:)"), Dq(), Num(), Kw("true|false|null")], RegexOptions.CultureInvariant),
            (["python", "py"], "Python", [LineComment("#"), new("tok-str", "[frbuFRBU]{0,2}\"\"\"[\\s\\S]*?\"\"\"|[frbuFRBU]{0,2}'''[\\s\\S]*?'''"), new("tok-str", "[frbuFRBU]{0,2}\"(?:\\\\.|[^\"\\\\\n])*\"|[frbuFRBU]{0,2}'(?:\\\\.|[^'\\\\\n])*'"), Num(), Kw(pythonKeywords), new("tok-typ", @"(?<![\w.])[A-Z]\w*\b")], RegexOptions.CultureInvariant),
            (["bash", "sh", "shell", "console"], "Bash", [LineComment("#"), new("tok-str", "'[^']*'"), new("tok-str", "\"(?:\\\\.|[^\"\\\\])*\""), new("tok-typ", @"\$\{[^}]*\}|\$\w+"), Num(), Kw(bashKeywords)], RegexOptions.CultureInvariant),
            (["yaml", "yml"], "YAML", [LineComment("#"), Dq(), Sq(), new("tok-kw", @"^[ \t]*[-\w.$/@]+(?=\s*:)"), Num(), Kw("true|false|null|yes|no")], RegexOptions.Multiline | RegexOptions.CultureInvariant),
            (["css"], "CSS", [BlockComment(), Dq(), Sq(), new("tok-kw", "@[a-zA-Z-]+"), new("tok-kw", "[-a-zA-Z]+(?=\\s*:)"), new("tok-num", "#[0-9a-fA-F]{3,8}\\b"), Num()], RegexOptions.CultureInvariant),
            (["html"], "HTML", [new("tok-cm", "<!--[\\s\\S]*?-->"), Dq(), Sq(), new("tok-kw", "(?<=</?)[a-zA-Z][\\w:-]*"), new("tok-cm", "<!DOCTYPE[^>]*>")], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            (["xml", "xaml"], "XML", [new("tok-cm", "<!--[\\s\\S]*?-->"), Dq(), Sq(), new("tok-kw", "(?<=</?)[a-zA-Z][\\w:-]*")], RegexOptions.CultureInvariant),
        };

        var result = new Dictionary<string, Language>(StringComparer.Ordinal);
        foreach (var (names, label, rules, options) in defs)
        {
            var parts = new string[rules.Length];
            for (var i = 0; i < rules.Length; i++)
                parts[i] = $"(?<g{i.ToString(CultureInfo.InvariantCulture)}>{rules[i].Pattern})";
            var regex = new Regex(string.Join("|", parts), options | RegexOptions.Compiled, MatchTimeout);
            var lang = new Language(label, regex, [.. rules.Select(r => r.Class)]);
            foreach (var name in names) result[name] = lang;
        }
        return result;
    }
}
