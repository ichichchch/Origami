namespace Origami.Web.Services;

/// <summary>行為本的差異比對（LCS 動態規劃）；供修訂版本比較視圖使用。</summary>
public static class TextDiff
{
    public enum DiffKind { Unchanged, Added, Removed }

    public sealed record DiffLine(DiffKind Kind, string Text);

    /// <summary>
    /// 比較兩段文字的行差異。行數超過上限時回傳 null，呼叫端退回全文對照，
    /// 避免 20 萬字本文的比較表拖垮互動電路。
    /// </summary>
    public static List<DiffLine>? Compare(string? oldText, string? newText, int maxLines = 1500)
    {
        var oldLines = (oldText ?? "").ReplaceLineEndings("\n").Split('\n');
        var newLines = (newText ?? "").ReplaceLineEndings("\n").Split('\n');
        if (oldLines.Length > maxLines || newLines.Length > maxLines) return null;
        var table = new int[oldLines.Length + 1, newLines.Length + 1];
        for (var i = oldLines.Length - 1; i >= 0; i--)
            for (var j = newLines.Length - 1; j >= 0; j--)
                table[i, j] = oldLines[i] == newLines[j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
        List<DiffLine> result = [];
        int a = 0, b = 0;
        while (a < oldLines.Length && b < newLines.Length)
        {
            if (oldLines[a] == newLines[b])
            {
                result.Add(new DiffLine(DiffKind.Unchanged, oldLines[a]));
                a++;
                b++;
            }
            else if (table[a + 1, b] >= table[a, b + 1])
            {
                result.Add(new DiffLine(DiffKind.Removed, oldLines[a]));
                a++;
            }
            else
            {
                result.Add(new DiffLine(DiffKind.Added, newLines[b]));
                b++;
            }
        }
        while (a < oldLines.Length) result.Add(new DiffLine(DiffKind.Removed, oldLines[a++]));
        while (b < newLines.Length) result.Add(new DiffLine(DiffKind.Added, newLines[b++]));
        return result;
    }
}
