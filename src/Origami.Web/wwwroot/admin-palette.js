// 後台命令面板（Ctrl／Cmd + K）：以靜態渲染的導覽項目為基礎，搭配同源的關鍵字搜尋端點
// （/admin/palette-search，Cookie 登入、唯讀 GET）動態補上內容結果。
// 不載入任何第三方腳本；鍵盤操作：上下鍵選擇、Enter 開啟、Esc 關閉。
(function () {
    "use strict";
    var source = document.getElementById("admin-palette-items");
    var dialog = document.getElementById("admin-palette");
    if (!source || !dialog) return;

    var input = dialog.querySelector(".palette-input");
    var list = dialog.querySelector(".palette-list");
    var status = dialog.querySelector(".palette-status");
    var staticItems = [];
    var dynamicItems = [];
    var active = 0;
    var previousFocus = null;
    var sequence = 0;
    var searchTimer = null;
    try { staticItems = JSON.parse(source.textContent || "[]") || []; } catch { staticItems = []; }

    function items() { return staticItems.concat(dynamicItems); }

    function render(filter) {
        var query = (filter || "").trim().toLowerCase();
        var shown = items().filter(function (item) { return !query || item.title.toLowerCase().indexOf(query) >= 0; }).slice(0, 12);
        active = 0;
        list.textContent = "";
        shown.forEach(function (item, index) {
            var option = document.createElement("li");
            option.id = "admin-palette-option-" + index;
            option.setAttribute("role", "option");
            option.setAttribute("aria-selected", index === 0 ? "true" : "false");
            option.setAttribute("data-url", item.url);
            option.tabIndex = -1;
            var kind = document.createElement("span");
            kind.className = "palette-kind";
            kind.textContent = item.kind || "";
            option.appendChild(kind);
            option.appendChild(document.createTextNode(item.title));
            option.addEventListener("click", function () { open(item.url); });
            option.addEventListener("mousemove", function () { setActive(index); });
            list.appendChild(option);
        });
        input.setAttribute("aria-activedescendant", shown.length > 0 ? "admin-palette-option-0" : "");
        status.textContent = shown.length === 0 ? (dialog.getAttribute("data-empty") || "") : "";
    }

    function setActive(index) {
        var options = list.querySelectorAll("[role='option']");
        if (!options.length) return;
        active = Math.max(0, Math.min(index, options.length - 1));
        options.forEach(function (option, position) {
            option.setAttribute("aria-selected", position === active ? "true" : "false");
        });
        var current = options[active];
        input.setAttribute("aria-activedescendant", current.id);
        if (typeof current.scrollIntoView === "function") current.scrollIntoView({ block: "nearest" });
    }

    function open(url) {
        close();
        if (url) window.location.assign(url);
    }

    function show() {
        if (!dialog.hidden) return;
        previousFocus = document.activeElement;
        dialog.hidden = false;
        document.body.classList.add("palette-open");
        dynamicItems = [];
        sequence++;
        input.value = "";
        render("");
        input.focus();
    }

    function close() {
        if (dialog.hidden) return;
        dialog.hidden = true;
        document.body.classList.remove("palette-open");
        if (previousFocus && typeof previousFocus.focus === "function") previousFocus.focus();
    }

    function search(query) {
        var ticket = ++sequence;
        window.fetch("/admin/palette-search?q=" + encodeURIComponent(query), { credentials: "same-origin", headers: { Accept: "application/json" } })
            .then(function (response) { return response.ok ? response.json() : { items: [] }; })
            .then(function (data) {
                // 只採用最新一次查詢的結果；面板已關閉時丟棄。
                if (ticket !== sequence || dialog.hidden) return;
                dynamicItems = Array.isArray(data && data.items) ? data.items : [];
                render(query);
            })
            .catch(function () { if (ticket === sequence && !dialog.hidden) { dynamicItems = []; render(query); } });
    }

    input.addEventListener("input", function () {
        render(input.value);
        if (searchTimer) window.clearTimeout(searchTimer);
        var query = input.value.trim();
        if (query.length < 2) { sequence++; dynamicItems = []; return; }
        searchTimer = window.setTimeout(function () { search(query); }, 220);
    });
    input.addEventListener("keydown", function (event) {
        var options = list.querySelectorAll("[role='option']");
        if (event.key === "ArrowDown") { event.preventDefault(); setActive(active + 1); }
        else if (event.key === "ArrowUp") { event.preventDefault(); setActive(active - 1); }
        else if (event.key === "Enter") { if (options[active]) { event.preventDefault(); open(options[active].getAttribute("data-url")); } }
        else if (event.key === "Escape") { event.preventDefault(); close(); }
    });
    document.addEventListener("keydown", function (event) {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
            event.preventDefault();
            if (dialog.hidden) show(); else close();
        }
    });
    var trigger = document.getElementById("admin-palette-trigger");
    if (trigger) trigger.addEventListener("click", show);
    dialog.addEventListener("click", function (event) { if (event.target === dialog) close(); });
    var closeButton = dialog.querySelector(".palette-close");
    if (closeButton) closeButton.addEventListener("click", close);
})();
