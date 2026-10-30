namespace Origami.Api;

/// <summary>
/// 由實際路由表產生 OpenAPI paths，使契約不會與端點實作漂移。
/// </summary>
public static class OpenApiDocumentBuilder
{
    /// <summary>全部端點的中文摘要與分類標籤，涵蓋公開與管理路由。</summary>
    private static readonly Dictionary<string, (string Summary, string Tag)> CuratedOperations =
        new(StringComparer.Ordinal)
        {
            // 公開端點
            ["get /openapi/v1.json"] = ("取得 OpenAPI v3 規格文件", "公開"),
            ["get /api/v1/openapi.json"] = ("取得 OpenAPI v3 規格文件", "公開"),
            ["get /api/v1/posts"] = ("列出已發布文章（支援分頁、分類、標籤、作者、年月篩選）", "公開"),
            ["get /api/v1/posts/{slug}"] = ("依代稱取得單篇已發布文章", "公開"),
            ["get /api/v1/posts/by-id/{id}"] = ("依識別碼取得單篇已發布文章", "公開"),
            ["get /api/v1/pages/{slug}"] = ("依代稱取得已發布獨立頁面", "公開"),
            ["get /api/v1/search"] = ("混合式關鍵字與語意搜尋（依內容類型判斷，支援納入獨立頁面）", "公開"),
            ["get /api/v1/settings"] = ("取得站點公開設定、版型、永久連結與時區格式", "公開"),
            ["get /api/v1/navigation"] = ("取得站點公開導覽選單項目", "公開"),
            ["get /api/v1/contents/{id}/published"] = ("依識別碼取得已設定為首頁的已發布頁面", "公開"),
            ["get /api/v1/media/widths"] = ("取得公開圖片響應式寬度變體設定", "公開"),
            ["get /api/v1/contents/{id}/comments"] = ("列出指定內容的已核可公開評論", "公開"),
            ["get /api/v1/contents/{id}/neighbors"] = ("取得指定內容的上一篇與下一篇相鄰文章", "公開"),
            ["get /api/v1/contents/{id}/related"] = ("取得指定內容的語意相關文章推薦", "公開"),
            ["get /api/v1/archives"] = ("取得文章年月彙整統計清單", "公開"),
            ["get /api/v1/types"] = ("列出公開自訂內容類型", "公開"),
            ["get /api/v1/types/{slug}/contents"] = ("列出指定自訂內容類型的已發布內容（支援自訂分類法與詞彙篩選）", "公開"),
            ["get /api/v1/types/{slug}/taxonomies/{taxonomySlug}/terms/{termSlug}"] = ("取得公開自訂分類法詞彙詳情", "公開"),
            ["get /api/v1/types/{slug}/contents/{contentSlug}"] = ("取得單篇自訂內容類型詳情", "公開"),
            ["get /api/v1/categories/popular"] = ("取得熱門文章分類清單", "公開"),
            ["get /api/v1/taxonomies/{kind}/{slug}"] = ("依分類或標籤代稱取得公開分類法詳情", "公開"),
            ["get /api/v1/redirects/{slug}"] = ("查詢舊代稱對應的永久轉址目標", "公開"),
            ["get /api/v1/authors/{slug}"] = ("依作者代稱取得公開作者資料", "公開"),
            ["get /api/v1/preview/{token}"] = ("依預覽權杖取得草稿預覽或已發布轉址資訊", "公開"),
            ["get /api/v1/sitemap"] = ("取得全站公開網站地圖項目", "公開"),
            ["get /api/v1/feed"] = ("取得訂閱內容項目（支援分類、標籤、作者、搜尋、自訂類型與詞彙篩選）", "公開"),
            ["get /api/v1/feed/comments"] = ("取得最新已核可評論訂閱項目", "公開"),
            ["get /api/v1/sites/resolve"] = ("依主機名稱解析站點公開基本資訊", "公開"),

            // 內容與評論管理端點
            ["get /api/v1/admin/contents"] = ("後台列出內容（支援狀態、關鍵字、作者、分類、標籤與日期區間篩選）", "管理"),
            ["post /api/v1/admin/contents"] = ("建立新內容（草稿、待審或直接發布）", "管理"),
            ["get /api/v1/admin/contents/{id}"] = ("讀取內容完整編輯模型", "管理"),
            ["put /api/v1/admin/contents/{id}"] = ("更新內容並建立完整修訂快照", "管理"),
            ["delete /api/v1/admin/contents/{id}"] = ("將內容移入回收桶或永久刪除", "管理"),
            ["post /api/v1/admin/contents/{id}/restore"] = ("從回收桶還原指定內容", "管理"),
            ["post /api/v1/admin/contents/{id}/duplicate"] = ("複製指定內容為新草稿", "管理"),
            ["put /api/v1/admin/contents/{id}/quick"] = ("快速編輯內容標題、代稱與狀態", "管理"),
            ["post /api/v1/admin/contents/{id}/comments"] = ("為指定內容新增訪客評論", "管理"),
            ["post /api/v1/admin/contents/{id}/editing"] = ("更新或接管內容編輯鎖定心跳", "管理"),
            ["delete /api/v1/admin/contents/{id}/editing"] = ("釋放目前內容的編輯鎖定", "管理"),
            ["post /api/v1/admin/contents/bulk"] = ("批次操作內容（移入回收桶、還原、永久刪除、改作者、加分類或標籤、開關評論）", "管理"),
            ["get /api/v1/admin/contents/counts"] = ("取得各狀態內容的統計數量", "管理"),
            ["get /api/v1/admin/contents/{id}/revisions"] = ("列出指定內容的修訂版本紀錄", "管理"),
            ["get /api/v1/admin/revisions/{id}"] = ("取得指定修訂版本的完整快照（含標題、分類、自訂欄位等）", "管理"),
            ["post /api/v1/admin/contents/{id}/preview-token"] = ("產生或輪替內容的草稿預覽權杖", "管理"),
            ["delete /api/v1/admin/contents/{id}/preview-token"] = ("撤銷內容的草稿預覽權杖", "管理"),
            ["get /api/v1/admin/contents/published/{kind}/{slug}"] = ("依類型與代稱取得網站渲染用完整已發布內容", "管理"),
            ["get /api/v1/admin/contents/published-by-id/{id}"] = ("依識別碼取得網站渲染用完整已發布文章", "管理"),
            ["get /api/v1/admin/contents/published-page/{path}"] = ("依層級路徑解析網站渲染用完整已發布頁面", "管理"),
            ["get /api/v1/admin/contents/{id}/unlock-state"] = ("查詢受保護內容目前的密碼解鎖狀態", "管理"),
            ["post /api/v1/admin/contents/{id}/unlock"] = ("驗證受保護內容密碼並簽發解鎖權杖", "管理"),
            ["post /api/v1/admin/contents/{id}/view"] = ("累加指定內容的瀏覽次數", "管理"),
            ["get /api/v1/admin/authors/options"] = ("取得編輯器可指派的作者選項清單", "管理"),
            ["get /api/v1/admin/pages/options"] = ("取得可選為父層或靜態首頁的頁面選項清單", "管理"),
            ["get /api/v1/admin/audit-logs"] = ("分頁查詢後台管理操作稽核紀錄", "管理"),
            ["post /api/v1/admin/ai/suggest-summary"] = ("使用智慧寫作助理生成文章摘要與搜尋引擎描述建議", "管理"),
            ["get /api/v1/admin/dashboard"] = ("取得後台管理儀表板統計總覽", "管理"),
            ["get /api/v1/admin/redirects"] = ("列出站點永久轉址規則", "管理"),
            ["post /api/v1/admin/redirects"] = ("建立新的永久轉址規則", "管理"),
            ["delete /api/v1/admin/redirects/{id}"] = ("刪除指定的永久轉址規則", "管理"),
            ["get /api/v1/admin/comments"] = ("後台分頁列出評論（支援依審核狀態篩選）", "管理"),
            ["get /api/v1/admin/comments/counts"] = ("取得各審核狀態的評論統計數量", "管理"),
            ["post /api/v1/admin/comments/{id}/reply"] = ("以管理員或編輯者身分回覆指定評論", "管理"),
            ["post /api/v1/admin/comments/{id}/moderate"] = ("審核指定評論（核可、待審或標記垃圾）", "管理"),
            ["delete /api/v1/admin/comments/{id}"] = ("永久刪除指定評論", "管理"),
            ["get /api/v1/admin/webhooks/deliveries"] = ("分頁查詢網路鉤子投遞紀錄與狀態", "管理"),
            ["post /api/v1/admin/webhooks/deliveries/{id}/retry"] = ("手動重新發送指定的網路鉤子投遞", "管理"),
            ["get /api/v1/admin/site-health"] = ("取得管理後台站點健康檢查完整報告", "管理"),

            // 媒體、設定、分類與匯出入端點
            ["get /api/v1/admin/media"] = ("分頁列出媒體庫檔案（支援關鍵字與類型篩選）", "管理"),
            ["post /api/v1/admin/media"] = ("上傳單一媒體檔案", "管理"),
            ["post /api/v1/admin/media/batch"] = ("批次上傳多個媒體檔案", "管理"),
            ["put /api/v1/admin/media/{id}/meta"] = ("更新媒體檔案的替代文字與圖說中繼資料", "管理"),
            ["get /api/v1/admin/media/{id}/references"] = ("查詢引用指定媒體檔案的內容清單", "管理"),
            ["delete /api/v1/admin/media/{id}"] = ("永久刪除指定媒體檔案", "管理"),
            ["get /api/v1/admin/settings"] = ("取得站點完整管理設定", "管理"),
            ["put /api/v1/admin/settings"] = ("儲存站點完整管理設定", "管理"),
            ["get /api/v1/admin/taxonomies"] = ("分頁列出指定類型的分類或標籤", "管理"),
            ["post /api/v1/admin/taxonomies"] = ("建立新的分類或標籤", "管理"),
            ["put /api/v1/admin/taxonomies/{id}"] = ("更新指定的分類或標籤", "管理"),
            ["delete /api/v1/admin/taxonomies/{id}"] = ("刪除指定的分類或標籤", "管理"),
            ["post /api/v1/admin/taxonomies/{id}/merge"] = ("將指定分類或標籤合併至目標項目", "管理"),
            ["post /api/v1/admin/taxonomies/bulk-delete"] = ("批次刪除多個分類或標籤", "管理"),
            ["get /api/v1/admin/transfer/export"] = ("匯出站點內容為結構化資料檔", "管理"),
            ["get /api/v1/admin/transfer/export-archive"] = ("匯出包含內容與媒體檔案的完整站點封存包", "管理"),
            ["post /api/v1/admin/transfer/import/analyze"] = ("上傳並分析站點匯入封存包預覽結果", "管理"),
            ["post /api/v1/admin/transfer/import/apply"] = ("確認並套用站點匯入封存包資料", "管理"),
            ["get /api/v1/admin/transfer/export-wxr"] = ("匯出站點內容為可攜式網站交換格式檔案", "管理"),
            ["post /api/v1/admin/transfer/import-wxr/analyze"] = ("上傳並分析網站交換格式檔案預覽結果", "管理"),
            ["post /api/v1/admin/transfer/import-wxr/apply"] = ("確認並匯入網站交換格式檔案內容", "管理"),

            // 自訂內容類型與欄位管理端點
            ["get /api/v1/admin/content-types"] = ("列出站點所有自訂內容類型", "管理"),
            ["post /api/v1/admin/content-types"] = ("建立新的自訂內容類型", "管理"),
            ["put /api/v1/admin/content-types/{id}"] = ("更新指定的自訂內容類型", "管理"),
            ["delete /api/v1/admin/content-types/{id}"] = ("刪除指定的自訂內容類型", "管理"),
            ["get /api/v1/admin/content-types/{id}/taxonomies"] = ("列出指定自訂內容類型的自訂分類法", "管理"),
            ["post /api/v1/admin/content-types/{id}/taxonomies"] = ("為指定自訂內容類型建立自訂分類法", "管理"),
            ["put /api/v1/admin/custom-taxonomies/{id}"] = ("更新指定的自訂分類法", "管理"),
            ["delete /api/v1/admin/custom-taxonomies/{id}"] = ("刪除指定的自訂分類法", "管理"),
            ["get /api/v1/admin/custom-taxonomies/{id}/terms"] = ("列出指定自訂分類法下的所有詞彙", "管理"),
            ["post /api/v1/admin/custom-taxonomies/{id}/terms"] = ("在指定自訂分類法下建立新詞彙", "管理"),
            ["put /api/v1/admin/custom-terms/{id}"] = ("更新指定的自訂分類法詞彙", "管理"),
            ["delete /api/v1/admin/custom-terms/{id}"] = ("刪除指定的自訂分類法詞彙", "管理"),
            ["get /api/v1/admin/content-types/{id}/terms"] = ("取得指定自訂內容類型可供選擇的全部詞彙", "管理"),
            ["get /api/v1/admin/content-types/{id}/fields"] = ("列出指定自訂內容類型的自訂欄位定義", "管理"),
            ["post /api/v1/admin/content-types/{id}/fields"] = ("為指定自訂內容類型新增自訂欄位定義", "管理"),
            ["put /api/v1/admin/custom-fields/{id}"] = ("更新指定的自訂欄位定義", "管理"),
            ["delete /api/v1/admin/custom-fields/{id}"] = ("刪除指定的自訂欄位定義", "管理"),

            // 導覽、站點、使用者、成員與憑證管理端點
            ["get /api/v1/admin/navigation"] = ("列出後台導覽選單項目", "管理"),
            ["post /api/v1/admin/navigation"] = ("新增導覽選單項目", "管理"),
            ["put /api/v1/admin/navigation/{id}"] = ("更新指定的導覽選單項目", "管理"),
            ["delete /api/v1/admin/navigation/{id}"] = ("刪除指定的導覽選單項目", "管理"),
            ["put /api/v1/admin/navigation/order"] = ("重新排序導覽選單項目", "管理"),
            ["get /api/v1/admin/access"] = ("取得目前登入者的站點存取權限與角色資訊", "管理"),
            ["get /api/v1/admin/sites"] = ("列出多站點網路中的所有站點", "管理"),
            ["post /api/v1/admin/sites"] = ("建立新的站點", "管理"),
            ["put /api/v1/admin/sites/{id}"] = ("更新指定站點的名稱與主機設定", "管理"),
            ["get /api/v1/admin/users"] = ("分頁列出系統使用者帳號", "管理"),
            ["post /api/v1/admin/users"] = ("建立新的系統使用者帳號", "管理"),
            ["put /api/v1/admin/users/{id}/state"] = ("啟用或停用指定的使用者帳號", "管理"),
            ["post /api/v1/admin/users/{id}/reset-2fa"] = ("重設指定使用者的雙因素驗證設定", "管理"),
            ["get /api/v1/admin/users/{id}/personal-data"] = ("匯出指定使用者的個人資料套件", "隱私"),
            ["delete /api/v1/admin/users/{id}/personal-data"] = ("抹除指定使用者的個人資料並匿名化其內容", "隱私"),
            ["get /api/v1/admin/members"] = ("列出目前站點的成員與角色清單", "管理"),
            ["put /api/v1/admin/members"] = ("新增或更新目前站點的成員角色與顯示名稱", "管理"),
            ["delete /api/v1/admin/members/{userId}"] = ("移除目前站點的指定成員", "管理"),
            ["get /api/v1/admin/credentials"] = ("列出目前站點的個人存取憑證", "管理"),
            ["post /api/v1/admin/credentials"] = ("建立新的個人存取憑證", "管理"),
            ["delete /api/v1/admin/credentials/{id}"] = ("撤銷指定的個人存取憑證", "管理"),
            ["get /api/v1/admin/profile"] = ("取得目前登入帳號的個人資料", "管理"),
            ["put /api/v1/admin/profile"] = ("更新目前登入帳號的個人資料", "管理"),
            ["post /api/v1/admin/profile/password"] = ("修改目前登入帳號的登入密碼", "管理"),
            ["get /api/v1/admin/profile/personal-data"] = ("匯出目前登入帳號的個人資料套件", "隱私"),
            ["delete /api/v1/admin/profile/personal-data"] = ("執行目前登入帳號的個人資料抹除與內容匿名化", "隱私"),
            ["get /api/v1/admin/profile/external-logins"] = ("列出目前登入帳號的外部登入綁定", "管理"),
            ["post /api/v1/admin/profile/external-logins"] = ("以 OAuth 繫結憑證完成外部登入綁定", "管理"),
            ["delete /api/v1/admin/profile/external-logins/{provider}"] = ("解除目前登入帳號的指定外部登入綁定", "管理")
        };

    private static readonly Dictionary<string, string[]> OperationQueryParameters =
        new(StringComparer.Ordinal)
        {
            ["get /api/v1/posts"] = ["page", "pageSize", "category", "tag", "author", "year", "month"],
            ["get /api/v1/search"] = ["q", "limit", "page", "includePages"],
            ["get /api/v1/feed"] = ["category", "tag", "author", "type", "taxonomy", "term", "q"],
            ["get /api/v1/types/{slug}/contents"] = ["page", "pageSize", "taxonomy", "term"],
            ["get /api/v1/sites/resolve"] = ["host"],
            ["get /api/v1/admin/contents"] = ["kind", "page", "pageSize", "status", "search", "authorId", "categoryId", "tagId", "dateFrom", "dateTo"],
            ["delete /api/v1/admin/contents/{id}"] = ["version", "permanent"],
            ["post /api/v1/admin/contents/{id}/restore"] = ["version"],
            ["get /api/v1/admin/audit-logs"] = ["page", "pageSize"],
            ["get /api/v1/admin/comments"] = ["page", "pageSize", "status"],
            ["post /api/v1/admin/comments/{id}/moderate"] = ["version"],
            ["delete /api/v1/admin/comments/{id}"] = ["version"],
            ["post /api/v1/admin/contents/{id}/editing"] = ["takeOver"],
            ["get /api/v1/admin/webhooks/deliveries"] = ["page", "pageSize"],
            ["get /api/v1/admin/contents/counts"] = ["kind"],
            ["get /api/v1/admin/media"] = ["page", "pageSize", "search", "kind"],
            ["delete /api/v1/admin/media/{id}"] = ["confirmed"],
            ["get /api/v1/admin/taxonomies"] = ["kind", "page", "pageSize"],
            ["post /api/v1/admin/taxonomies"] = ["kind"],
            ["put /api/v1/admin/taxonomies/{id}"] = ["kind"],
            ["delete /api/v1/admin/taxonomies/{id}"] = ["kind", "version"],
            ["post /api/v1/admin/taxonomies/{id}/merge"] = ["kind"],
            ["get /api/v1/admin/transfer/export"] = ["kind", "category"],
            ["post /api/v1/admin/transfer/import-wxr/apply"] = ["downloadAttachments", "allowedHosts"],
            ["delete /api/v1/admin/content-types/{id}"] = ["version"],
            ["delete /api/v1/admin/custom-taxonomies/{id}"] = ["version"],
            ["delete /api/v1/admin/custom-terms/{id}"] = ["version"],
            ["delete /api/v1/admin/custom-fields/{id}"] = ["version"],
            ["delete /api/v1/admin/navigation/{id}"] = ["version"],
            ["get /api/v1/admin/users"] = ["page", "search"],
            ["delete /api/v1/admin/members/{userId}"] = ["version"]
        };

    private static readonly Dictionary<string, string> QueryParameterDescriptions =
        new(StringComparer.Ordinal)
        {
            ["page"] = "分頁頁碼，從 1 開始。",
            ["pageSize"] = "每頁筆數。",
            ["category"] = "分類代稱篩選。",
            ["tag"] = "標籤代稱篩選。",
            ["author"] = "作者代稱篩選。",
            ["year"] = "年份篩選（四位數西元年）。",
            ["month"] = "月份篩選（1 至 12）。",
            ["q"] = "關鍵字查詢字串。",
            ["limit"] = "每頁結果筆數上限。",
            ["includePages"] = "是否將獨立頁面納入搜尋結果。",
            ["type"] = "自訂內容類型代稱篩選。",
            ["taxonomy"] = "自訂分類法代稱篩選。",
            ["term"] = "自訂詞彙代稱篩選。",
            ["host"] = "要解析的站點主機名稱。",
            ["kind"] = "資源類型篩選（例如文章、頁面、媒體或分類法類型）。",
            ["status"] = "狀態篩選（例如草稿、待審、已發布或評論審核狀態）。",
            ["search"] = "搜尋關鍵字。",
            ["authorId"] = "作者使用者識別碼篩選。",
            ["categoryId"] = "分類識別碼篩選。",
            ["tagId"] = "標籤識別碼篩選。",
            ["dateFrom"] = "起始日期篩選（格式：年-月-日）。",
            ["dateTo"] = "結束日期篩選（格式：年-月-日）。",
            ["version"] = "並發控制版本識別碼。",
            ["permanent"] = "是否永久刪除（為否則移入回收桶）。",
            ["takeOver"] = "是否強制接管他人目前的編輯鎖定。",
            ["confirmed"] = "是否已由呼叫端完成人工刪除確認。",
            ["downloadAttachments"] = "匯入時是否自動下載遠端附件媒體。",
            ["allowedHosts"] = "允許下載遠端附件的主機白名單（以逗號分隔）。"
        };

    private static readonly Dictionary<string, string> ResourceTranslations =
        new(StringComparer.Ordinal)
        {
            ["openapi.json"] = "OpenAPI 規格",
            ["v1.json"] = "OpenAPI 規格",
            ["content-types"] = "內容類型",
            ["custom-taxonomies"] = "自訂分類法",
            ["custom-terms"] = "自訂詞彙",
            ["custom-fields"] = "自訂欄位",
            ["bulk-delete"] = "批次刪除",
            ["by-id"] = "依識別碼查詢",
            ["published-by-id"] = "已發布內容（依識別碼）",
            ["published-page"] = "已發布頁面",
            ["export-archive"] = "站點封存匯出",
            ["export-wxr"] = "網站交換格式匯出",
            ["import-wxr"] = "網站交換格式匯入",
            ["audit-logs"] = "稽核紀錄",
            ["suggest-summary"] = "摘要建議",
            ["site-health"] = "站點健康檢查",
            ["preview-token"] = "預覽權杖",
            ["unlock-state"] = "解鎖狀態",
            ["reset-2fa"] = "重設雙因素驗證",
            ["personal-data"] = "個人資料",
            ["navigation"] = "導覽選單",
            ["order"] = "排序",
            ["archives"] = "年月彙整",
            ["comments"] = "評論",
            ["neighbors"] = "相鄰內容",
            ["related"] = "相關內容",
            ["widths"] = "圖片寬度",
            ["contents"] = "內容",
            ["types"] = "自訂內容類型",
            ["users"] = "使用者",
            ["state"] = "帳號狀態",
            ["sites"] = "站點",
            ["resolve"] = "主機解析",
            ["members"] = "站點成員",
            ["credentials"] = "存取憑證",
            ["profile"] = "個人資料",
            ["password"] = "登入密碼",
            ["taxonomies"] = "分類法",
            ["terms"] = "詞彙",
            ["fields"] = "自訂欄位",
            ["media"] = "媒體",
            ["settings"] = "站點設定",
            ["deliveries"] = "投遞紀錄",
            ["retry"] = "重送",
            ["analyze"] = "匯入分析",
            ["apply"] = "匯入套用",
            ["access"] = "存取權限",
            ["options"] = "選項清單",
            ["batch"] = "批次上傳",
            ["meta"] = "中繼資料",
            ["references"] = "引用關聯",
            ["restore"] = "還原",
            ["duplicate"] = "複製",
            ["quick"] = "快速編輯",
            ["reply"] = "回覆",
            ["dashboard"] = "儀表板",
            ["redirects"] = "轉址規則",
            ["counts"] = "統計數量",
            ["moderate"] = "評論審核",
            ["editing"] = "編輯鎖定",
            ["bulk"] = "批次操作",
            ["webhooks"] = "網路鉤子",
            ["revisions"] = "修訂版本",
            ["unlock"] = "密碼解鎖",
            ["view"] = "瀏覽計數",
            ["merge"] = "合併",
            ["export"] = "匯出",
            ["import"] = "匯入",
            ["transfer"] = "資料傳輸",
            ["posts"] = "文章",
            ["pages"] = "獨立頁面",
            ["search"] = "搜尋",
            ["categories"] = "分類",
            ["popular"] = "熱門分類",
            ["authors"] = "作者",
            ["preview"] = "預覽",
            ["published"] = "已發布內容",
            ["sitemap"] = "網站地圖",
            ["feed"] = "訂閱來源",
            ["ai"] = "智慧助理"
        };

    public static object Build(HttpContext http)
    {
        var serverUrl = $"{http.Request.Scheme}://{http.Request.Host.Value}";
        return new
        {
            openapi = "3.1.0",
            info = new
            {
                title = "Origami 內容發布系統 API",
                version = "v1",
                description = "Origami 的 API 契約：提供公開唯讀端點（/api/v1）與受保護的管理端點（/api/v1/admin，支援 BFF 與個人 API 憑證 PAT）。Paths 由實際路由表產生並覆蓋全部端點。"
            },
            servers = new[] { new { url = serverUrl } },
            components = new
            {
                securitySchemes = new
                {
                    BearerAuth = new
                    {
                        type = "http",
                        scheme = "bearer",
                        description = "個人 API 憑證（PAT）或 BFF 服務通行證。"
                    }
                }
            },
            paths = BuildPaths(http)
        };
    }

    private static Dictionary<string, object> BuildPaths(HttpContext http)
    {
        var source = http.RequestServices.GetRequiredService<EndpointDataSource>();
        var paths = new SortedDictionary<string, SortedDictionary<string, object>>(StringComparer.Ordinal);

        foreach (var endpoint in source.Endpoints)
        {
            if (endpoint is not RouteEndpoint route) continue;
            var path = ToOpenApiPath(route.RoutePattern.RawText);
            if (path is null || !IsDocumented(path)) continue;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            if (methods is null) continue;

            foreach (var method in methods.HttpMethods)
            {
                var verb = method.ToLowerInvariant();
                if (!paths.TryGetValue(path, out var operations))
                {
                    operations = new SortedDictionary<string, object>(StringComparer.Ordinal);
                    paths[path] = operations;
                }
                operations[verb] = new
                {
                    summary = SummaryFor(path, verb),
                    description = DescriptionFor(path, verb),
                    operationId = OperationId(verb, path),
                    tags = new[] { TagFor(path, verb) },
                    parameters = ParametersFor(path, verb),
                    security = SecurityFor(path)
                };
            }
        }

        return paths.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>契約涵蓋 /api/v1 與規格文件本身的 /openapi/v1.json。</summary>
    private static bool IsDocumented(string path) =>
        path.StartsWith("/api/v1", StringComparison.Ordinal) ||
        path.Equals("/openapi/v1.json", StringComparison.Ordinal);

    /// <summary>管理端點需 BFF 服務通行證或個人 API 憑證（PAT）；公開端點匿名。</summary>
    private static object SecurityFor(string path) =>
        path.StartsWith("/api/v1/admin", StringComparison.Ordinal)
            ? new[] { new { BearerAuth = Array.Empty<string>() } }
            : Array.Empty<object>();

    private static string TagFor(string path, string verb) =>
        CuratedOperations.TryGetValue($"{verb} {path}", out var curated) ? curated.Tag
        : CuratedOperations.TryGetValue($"get {path}", out curated) ? curated.Tag
        : path.StartsWith("/api/v1/admin", StringComparison.Ordinal) ? "管理"
        : "公開";

    private static string SummaryFor(string path, string verb)
    {
        if (CuratedOperations.TryGetValue($"{verb} {path}", out var curated)) return curated.Summary;
        if (path.Equals("/openapi/v1.json", StringComparison.Ordinal) || path.Equals("/api/v1/openapi.json", StringComparison.Ordinal))
            return "取得 OpenAPI 規格文件";

        var action = verb switch
        {
            "get" => "查詢",
            "post" => "建立或執行",
            "put" or "patch" => "更新",
            "delete" => "刪除",
            _ => "呼叫"
        };
        return $"{action} {ResourceFor(path)}";
    }

    private static string ResourceFor(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var resource = segments.LastOrDefault(segment => !segment.StartsWith('{')) ?? path;
        if (ResourceTranslations.TryGetValue(resource, out var translated)) return translated;
        if (!resource.Contains('-')) return resource;

        var parts = resource.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var translatedParts = parts
            .Select(part => ResourceTranslations.TryGetValue(part, out var term) ? term : part)
            .ToArray();
        return string.Join(' ', translatedParts);
    }

    private static string DescriptionFor(string path, string verb)
    {
        var key = $"{verb} {path}";
        if (OperationQueryParameters.TryGetValue(key, out var query) && query.Length > 0)
            return $"支援查詢參數：{string.Join("、", query)}。";
        return CuratedOperations.TryGetValue(key, out var curated)
            ? curated.Summary + "。"
            : "由實際路由端點自動產生的 API 操作。";
    }

    private static object[] ParametersFor(string path, string verb)
    {
        var parameters = new List<object>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!segment.StartsWith('{') || !segment.EndsWith('}')) continue;
            var name = segment.Trim('{', '}');
            parameters.Add(new
            {
                name,
                @in = "path",
                required = true,
                description = PathParameterDescription(name),
                schema = PathParameterSchema(name)
            });
        }

        if (OperationQueryParameters.TryGetValue($"{verb} {path}", out var queryParameters))
        {
            foreach (var name in queryParameters)
            {
                parameters.Add(new
                {
                    name,
                    @in = "query",
                    required = false,
                    description = QueryParameterDescriptions.TryGetValue(name, out var description) ? description : $"{name} 查詢條件。",
                    schema = QueryParameterSchema(name)
                });
            }
        }

        return [.. parameters];
    }

    private static object PathParameterSchema(string name) => name switch
    {
        "id" => (object)new { type = "string", format = "uuid" },
        _ => new { type = "string" }
    };

    private static object QueryParameterSchema(string name) => name switch
    {
        "page" or "pageSize" or "year" or "month" or "limit" => new { type = "integer" },
        "includePages" or "permanent" or "takeOver" or "confirmed" or "downloadAttachments" => new { type = "boolean" },
        "categoryId" or "tagId" or "version" => new { type = "string", format = "uuid" },
        "dateFrom" or "dateTo" => new { type = "string", format = "date" },
        _ => new { type = "string" }
    };

    private static string PathParameterDescription(string name) => name switch
    {
        "id" => "資源識別碼。",
        "slug" => "資源代稱。",
        "contentSlug" => "自訂內容代稱。",
        "taxonomySlug" => "自訂分類法代稱。",
        "termSlug" => "自訂詞彙代稱。",
        "kind" => "資源類型（例如分類、標籤、文章或頁面）。",
        "path" => "層級頁面完整路徑。",
        "userId" => "使用者識別碼。",
        "token" => "預覽存取權杖。",
        _ => $"{name} 路徑參數。"
    };

    private static string OperationId(string verb, string path) =>
        verb + "_" + string.Join('_', path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.StartsWith('{') ? segment.Trim('{', '}') : segment));

    /// <summary>把路由樣式轉成 OpenAPI 參數寫法：去掉路由限制、catch-all 與選用標記。</summary>
    private static string? ToOpenApiPath(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return null;
        var segments = new List<string>();
        foreach (var segment in rawText.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!segment.StartsWith('{') || !segment.EndsWith('}'))
            {
                segments.Add(segment);
                continue;
            }
            var name = segment[1..^1].TrimStart('*');
            var colon = name.IndexOf(':');
            if (colon >= 0) name = name[..colon];
            name = name.TrimEnd('?');
            if (name.Length == 0) continue;
            segments.Add("{" + name + "}");
        }
        return "/" + string.Join('/', segments);
    }
}
