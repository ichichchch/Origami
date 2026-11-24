namespace Origami.Tests;

/// <summary>編輯器工具列、尋找取代、專注模式、HTML 貼上轉換，以及公開頁的語法高亮與頁籤。</summary>
[Collection(PostgresCollection.Name)]
public sealed class EditorToolbarBrowserTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Toolbar_find_focus_paste_and_public_enhancements_work_in_browser()
    {
        var password = IsolatedPostgres.NewPassword();
        await App.Provisioner.ResetPasswordAsync(password);
        using var actor = await App.AdministratorAsync();
        var seeded = await actor.Content.SaveAsync(NewContent("工具列瀏覽器文章", status: ContentStatus.Published));

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync(environment: "Production");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("ORIGAMI_BROWSER_CHANNEL") ?? "msedge",
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = site.Address.ToString() });
        var page = await context.NewPageAsync();
        var pageErrors = new ConcurrentQueue<string>();
        var requests = new ConcurrentQueue<string>();
        page.PageError += (_, error) => pageErrors.Enqueue(error);
        page.Request += (_, request) => requests.Enqueue(request.Method + " " + request.Url);
        page.Response += (_, response) => requests.Enqueue(response.Status + " " + response.Url);

        try
        {
            await LoginAsync(page, password);
            await page.GotoAsync("/admin/content/" + seeded.Id);
            var textarea = page.Locator("#content-markdown");
            await textarea.WaitForAsync();
            // 腳本在電路就緒後才掛上；以標記為準，避免在預渲染畫面上點擊（互動接管期間元素可能短暫不存在）。
            var console = new ConcurrentQueue<string>();
            page.Console += (_, message) => console.Enqueue(message.Type + ": " + message.Text);
            try { await WaitForEditorWiringAsync(page); }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                var diag = await page.EvaluateAsync<string>(EditorDiagnostic);
                Assert.Fail("編輯器腳本未掛上。主控台：" + string.Join(" | ", console) + "；頁面錯誤：" + string.Join(" | ", pageErrors)
                    + "；本文框：" + (await page.Locator("#content-markdown").EvaluateAsync<string>("el => el ? (el.tagName + ' ' + JSON.stringify(el.dataset)) : 'missing'"))
                    + "；診斷：" + diag);
            }

            // 工具列與快捷鍵都以游標選取範圍為準。
            await textarea.FillAsync("hello world");
            await SelectAsync(page, 0, 5);
            await page.GetByRole(AriaRole.Button, new() { Name = "粗體", Exact = true }).ClickAsync();
            await Expect(textarea).ToHaveValueAsync("**hello** world");
            await SelectAsync(page, 10, 15);
            await textarea.PressAsync("Control+b");
            await Expect(textarea).ToHaveValueAsync("**hello** **world**");

            // 區塊插入在選取範圍之後，不取代選取的文字。
            await textarea.EvaluateAsync("el => el.setSelectionRange(el.value.length, el.value.length)");
            await page.Locator("#editor-insert-block").SelectOptionAsync("table");
            await Expect(textarea).ToHaveValueAsync(new Regex(Regex.Escape("**hello** **world**") + @"\n\n\| 欄位 1 \| 欄位 2 \| 欄位 3 \|"));
            await Expect(page.Locator("#editor-insert-block")).ToHaveValueAsync("");

            // 尋找取代：Enter 不會送出表單，全部取代直接改寫本文。
            await page.GetByRole(AriaRole.Button, new() { Name = "尋找取代", Exact = true }).ClickAsync();
            await page.Locator("#editor-find-term").FillAsync("world");
            await page.Locator("#editor-replace-term").FillAsync("earth");
            await Expect(page.Locator("#editor-find [role='status']")).ToHaveTextAsync("共 1 處");
            await page.GetByRole(AriaRole.Button, new() { Name = "全部取代", Exact = true }).ClickAsync();
            await Expect(textarea).ToHaveValueAsync(new Regex("earth"));
            Assert.DoesNotContain("world", await textarea.InputValueAsync(), StringComparison.Ordinal);
            await page.Locator("#editor-find").GetByRole(AriaRole.Button, new() { Name = "關閉", Exact = true }).ClickAsync();

            // 專注模式：整個面板全螢幕，Esc 離開。
            await page.GetByRole(AriaRole.Button, new() { Name = "專注模式", Exact = true }).ClickAsync();
            await Expect(page.Locator("#markdown-panel")).ToHaveClassAsync(new Regex("editor-focus"));
            await textarea.PressAsync("Escape");
            await Expect(page.Locator("#markdown-panel")).Not.ToHaveClassAsync(new Regex("editor-focus"));

            // 從網頁或文件複製的 HTML 貼上時轉為 Markdown。
            await page.EvaluateAsync(
                "() => { const el = document.getElementById('content-markdown'); el.focus(); el.setSelectionRange(el.value.length, el.value.length);" +
                " const dt = new DataTransfer(); dt.setData('text/html', '<p>貼上的<b>粗體</b>段落</p>'); dt.setData('text/plain', '貼上的粗體段落');" +
                " el.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true })); }");
            await Expect(textarea).ToHaveValueAsync(new Regex("貼上的\\*\\*粗體\\*\\*段落"));

            // 轉換等待期間重建 textarea 時，結果應落在目前元素；電路拒絕時回退純文字且不產生未處理 rejection。
            var pasteRace = await page.EvaluateAsync<PasteRaceFacts>(PasteRaceRegressionScript);
            Assert.Equal("before", pasteRace.OldValue);
            Assert.Equal("after**converted**", pasteRace.CurrentValue);
            Assert.Equal("before plain", pasteRace.RejectedValue);
            Assert.Empty(pasteRace.Unhandled);

            // 預覽：程式碼高亮與頁籤在互動渲染後重新套用。
            // 服務端已著色的語言（js、csharp 等）由 CodeHighlighter 改寫成 figure，不再經前端 highlight.js；
            // 這裡以服務端不支援的語言（rust）驗證前端漸進增強仍然生效。
            await textarea.FillAsync("```rust\nlet x = 1;\n```\n\n::: tab 第一頁\n甲內容\n:::\n::: tab 第二頁\n乙內容\n:::");
            await page.GetByRole(AriaRole.Button, new() { Name = "預覽", Exact = true }).ClickAsync();
            await page.Locator(".editor-preview pre code .hljs-keyword").WaitForAsync();
            Assert.Equal(2, await page.Locator(".editor-preview .tab-list [role='tab']").CountAsync());

            // Ctrl+S 儲存後，公開頁同樣有高亮與可切換的頁籤。
            await page.GetByRole(AriaRole.Button, new() { Name = "Markdown", Exact = true }).ClickAsync();
            await WaitForEditorWiringAsync(page);
            await Expect(page.Locator("span.save-indicator.unsaved")).ToHaveCountAsync(1);
            await textarea.EvaluateAsync("el => { el.dataset.saveKeydowns = '0'; el.addEventListener('keydown', (e) => { if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') el.dataset.saveKeydowns = String(Number(el.dataset.saveKeydowns) + 1); }); }");
            await textarea.PressAsync("Control+s");
            try
            {
                await Expect(page.Locator("span.save-indicator.unsaved")).ToHaveCountAsync(0);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                Assert.Fail("Ctrl+S 後仍有未儲存變更。" + ex.Message
                    + "\n儲存狀態：" + await page.EvaluateAsync<string>(SaveDiagnostic)
                    + "\n主控台：" + string.Join(" | ", console)
                    + "\n頁面錯誤：" + string.Join(" | ", pageErrors)
                    + "\n網路：" + string.Join(" | ", requests)
                    + "\n伺服器錯誤：" + string.Join("\n", site.ErrorLines)
                    + "\nApi 錯誤：" + string.Join("\n", site.ApiErrorLines));
            }

            await page.GotoAsync("/posts/" + seeded.Slug);
            await page.Locator(".prose pre code .hljs-keyword").WaitForAsync();
            var tabs = page.Locator(".prose .tab-list [role='tab']");
            Assert.Equal(2, await tabs.CountAsync());
            await Expect(page.Locator(".prose .tab-panel").Nth(1)).ToBeHiddenAsync();
            await tabs.Nth(1).ClickAsync();
            await Expect(page.Locator(".prose .tab-panel").Nth(1)).ToBeVisibleAsync();
            await Expect(page.Locator(".prose .tab-panel").Nth(0)).ToBeHiddenAsync();
            await Expect(page.Locator(".prose .tab-list [role='tab']").Nth(1)).ToHaveAttributeAsync("aria-selected", "true");

            Assert.Empty(pageErrors);
        }
        finally
        {
            if (pageErrors.Count > 0)
                throw new Exception("頁面發生錯誤：" + string.Join(" | ", pageErrors));
        }
    }

    // 診斷：Blazor 錯誤條是否顯示、importmap 對應的指紋檔能否載入、模組匯出為何。
    private const string SaveDiagnostic = "() => { const t = document.getElementById('content-markdown'); const f = document.querySelector('.editor-grid'); " +
        "const text = (s) => Array.from(document.querySelectorAll(s)).map((e) => e.innerText.trim()).filter(Boolean).join(' | '); " +
        "return JSON.stringify({ textarea: t ? { dataset: {...t.dataset}, length: t.value.length, disabled: t.disabled } : null, " +
        "fieldsetDisabled: f ? f.disabled : null, indicator: text('span.save-indicator'), visibleAlerts: Array.from(document.querySelectorAll('[role=\"alert\"]')).filter((e) => e.getClientRects().length > 0).map((e) => e.innerText.trim()), " +
        "errorUi: document.getElementById('blazor-error-ui') ? getComputedStyle(document.getElementById('blazor-error-ui')).display : 'missing', " +
        "notice: text('.notice-success'), busy: text('.operation-status'), saveButtons: Array.from(document.querySelectorAll('button[type=\"submit\"], aside button')).map((b) => ({ text: b.innerText.trim(), disabled: b.disabled })) }); }";

    private const string EditorDiagnostic = "async () => { const out = {}; const e = document.getElementById('blazor-error-ui'); " +
        "out.errorUi = e ? (getComputedStyle(e).display !== 'none' ? e.innerText.slice(0, 400) : 'hidden') : 'none'; " +
        "const map = document.querySelector('script[type=\"importmap\"]'); " +
        "let mapped = 'nomap'; try { mapped = JSON.parse(map.textContent).imports['./Components/Admin/ContentEditor.razor.js'] || 'nokey'; } catch (err) { mapped = 'badmap:' + err.message; } " +
        "out.mapped = mapped; " +
        "if (mapped.startsWith('./')) { try { const mod = await import(mapped); out.exports = Object.keys(mod).join(','); } catch (err) { out.importError = String(err && err.message || err); } } " +
        "out.toolbar = !!document.getElementById('editor-insert-block'); out.blazor = typeof Blazor; return JSON.stringify(out); }";

    private const string PasteRaceRegressionScript = "async () => {" +
        "const imports = JSON.parse(document.querySelector('script[type=\"importmap\"]').textContent).imports;" +
        "const editor = await import(imports['./Components/Admin/ContentEditor.razor.js']);" +
        "const unhandled = [];" +
        "const onUnhandled = (event) => unhandled.push(String(event.reason));" +
        "window.addEventListener('unhandledrejection', onUnhandled);" +
        "const paste = (el, plain) => {" +
          "el.focus(); const data = new DataTransfer(); data.setData('text/plain', plain); data.setData('text/html', '<p><strong>rich</strong></p>');" +
          "el.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));" +
        "};" +
        "const old = document.createElement('textarea'); old.id = 'paste-race-old'; old.value = 'before'; document.body.append(old);" +
        "let resolveConversion;" +
        "const pending = { invokeMethodAsync: () => new Promise((resolve) => { resolveConversion = resolve; }) };" +
        "editor.wireEditorPaste(old.id, pending);" +
        "old.setSelectionRange(0, 0); paste(old, 'plain');" +
        "const current = document.createElement('textarea'); current.id = old.id; current.value = 'after';" +
        "current.setSelectionRange(5, 5); old.replaceWith(current);" +
        "resolveConversion('**converted**'); await Promise.resolve(); await Promise.resolve();" +
        "const stale = { old: old.value, current: current.value }; current.remove();" +
        "const fallback = document.createElement('textarea'); fallback.id = 'paste-race-fallback'; fallback.value = 'before'; document.body.append(fallback);" +
        "let rejectConversion;" +
        "const rejected = { invokeMethodAsync: () => new Promise((resolve, reject) => { rejectConversion = reject; }) };" +
        "editor.wireEditorPaste(fallback.id, rejected);" +
        "fallback.setSelectionRange(fallback.value.length, fallback.value.length); paste(fallback, ' plain');" +
        "rejectConversion(new Error('circuit disconnected')); await Promise.resolve(); await Promise.resolve();" +
        "await new Promise((resolve) => setTimeout(resolve, 0));" +
        "const rejectedValue = fallback.value; fallback.remove();" +
        "window.removeEventListener('unhandledrejection', onUnhandled);" +
        "return { oldValue: stale.old, currentValue: stale.current, rejectedValue, unhandled };" +
"}";

    private sealed class PasteRaceFacts
    {
        public string OldValue { get; set; } = "";
        public string CurrentValue { get; set; } = "";
        public string RejectedValue { get; set; } = "";
        public string[] Unhandled { get; set; } = [];
    }

    private static Task WaitForEditorWiringAsync(IPage page) =>
        page.WaitForFunctionAsync("() => document.getElementById('content-markdown')?.dataset.keysWired === '1'");

    private static Task SelectAsync(IPage page, int start, int end) => page.EvaluateAsync(
        $"() => {{ const el = document.getElementById('content-markdown'); el.focus(); el.setSelectionRange({start}, {end}); }}");

    private static async Task LoginAsync(IPage page, string password)
    {
        await page.GotoAsync("/account/login");
        await page.Locator("#username").FillAsync("test-admin");
        await page.Locator("#password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "登入", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/admin/posts");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
