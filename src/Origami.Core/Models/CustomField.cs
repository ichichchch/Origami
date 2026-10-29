namespace Origami.Core.Models;

/// <summary>
/// 自訂欄位：宣告式擴充點。欄位以資料宣告，不載入任何程式碼；
/// 欄位定義掛在內容類型上，內容以值配對。
/// </summary>
public sealed class CustomField
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public Guid ContentTypeId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>穩定鍵值；內容以此鍵保存輸入值，顯示名稱可自由修改。</summary>
    public string Key { get; set; } = "";
    /// <summary>欄位種類：text／longtext／number／date／checkbox。</summary>
    public string FieldType { get; set; } = CustomFieldTypes.Text;
    public bool IsRequired { get; set; }
    /// <summary>顯示順序；數字越小越前面。</summary>
    public int Position { get; set; }
    public bool IsArchived { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public ContentType ContentType { get; set; } = null!;
}

/// <summary>內容在自訂欄位上的值。</summary>
public sealed class ContentFieldValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public Guid ContentId { get; set; }
    public Guid FieldId { get; set; }
    /// <summary>以字串保存的值；不同欄位種類的格式由服務層驗證。</summary>
    public string Value { get; set; } = "";
    public Content Content { get; set; } = null!;
    public CustomField Field { get; set; } = null!;
}

/// <summary>自訂欄位的受控種類；只提供輸入型態，不允許程式碼或樣式。</summary>
public static class CustomFieldTypes
{
    public const string Text = "text";
    public const string LongText = "longtext";
    public const string Number = "number";
    public const string Date = "date";
    public const string Checkbox = "checkbox";

    public static readonly IReadOnlyList<string> All = [Text, LongText, Number, Date, Checkbox];

    public static bool IsValid(string? kind) =>
        kind is not null && All.Contains(kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>欄位值的長度上限；長文字欄位允許較長的內容。</summary>
    public static int MaxLength(string kind) => kind == LongText ? 2000 : 200;
}

/// <summary>自訂欄位值的驗證規則；各種類只接受可安全呈現的格式。</summary>
public static class CustomFieldRules
{
    public static void ValidateValue(string kind, string? value, bool required)
    {
        var text = (value ?? "").Trim();
        if (required && text.Length == 0) throw new CmsException("此欄位為必填。");
        if (text.Length > CustomFieldTypes.MaxLength(kind)) throw new CmsException("欄位內容超過長度上限。");
        if (text.Length == 0) return;
        switch (kind)
        {
            case CustomFieldTypes.Number:
                if (!decimal.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    throw new CmsException("此欄位須為數字。");
                break;
            case CustomFieldTypes.Date:
                if (!DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out _))
                    throw new CmsException("此欄位須為日期（yyyy-MM-dd）。");
                break;
            case CustomFieldTypes.Checkbox:
                if (text is not ("true" or "false")) throw new CmsException("此欄位僅接受 true 或 false。");
                break;
        }
    }
}
