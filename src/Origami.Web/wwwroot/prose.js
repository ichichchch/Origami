// 內文增強：程式碼語法高亮與頁籤容器。兩者都是漸進增強——
// 高亮庫只在頁面帶有標了語言的程式碼區塊時才載入；頁籤在沒有腳本時依序展開並保留標題。
(function () {
    "use strict";
    var highlightLoading = null;
    var tabCounter = 0;

    function loadHighlight() {
        if (window.hljs) return Promise.resolve(window.hljs);
        if (highlightLoading) return highlightLoading;
        var source = document.documentElement.getAttribute("data-highlight-src");
        highlightLoading = new Promise(function (resolve) {
            if (!source) { resolve(null); return; }
            var script = document.createElement("script");
            script.src = source;
            script.async = true;
            script.onload = function () { resolve(window.hljs || null); };
            script.onerror = function () { resolve(null); };
            document.head.appendChild(script);
        });
        return highlightLoading;
    }

    // 只處理帶 language- 標記的區塊：自動偵測會把純文字（例如指令輸出）塗上不正確的顏色。
    function highlight(scope) {
        var blocks = (scope || document).querySelectorAll("pre code[class*=\"language-\"]:not([data-highlighted])");
        if (!blocks.length) return Promise.resolve();
        return loadHighlight().then(function (hljs) {
            if (!hljs) return;
            for (var i = 0; i < blocks.length; i++) hljs.highlightElement(blocks[i]);
        });
    }

    function tabs(scope) {
        var roots = (scope || document).querySelectorAll(".tabs:not([data-tabs-ready])");
        for (var i = 0; i < roots.length; i++) {
            var root = roots[i];
            var panels = root.querySelectorAll(".tab-panel");
            if (panels.length < 2) continue;
            var list = document.createElement("div");
            list.className = "tab-list";
            list.setAttribute("role", "tablist");
            var buttons = [];
            for (var j = 0; j < panels.length; j++) {
                var panel = panels[j];
                var button = document.createElement("button");
                var id = "origami-tab-" + (++tabCounter);
                button.type = "button";
                button.className = "tab-button";
                button.id = id;
                button.textContent = panel.getAttribute("data-tab-title") || String(j + 1);
                button.setAttribute("role", "tab");
                button.setAttribute("aria-selected", j === 0 ? "true" : "false");
                button.setAttribute("tabindex", j === 0 ? "0" : "-1");
                panel.id = id + "-panel";
                panel.setAttribute("role", "tabpanel");
                panel.setAttribute("aria-labelledby", id);
                button.setAttribute("aria-controls", panel.id);
                panel.hidden = j > 0;
                list.appendChild(button);
                buttons.push(button);
            }
            var select = function (index) {
                for (var k = 0; k < buttons.length; k++) {
                    var active = k === index;
                    buttons[k].setAttribute("aria-selected", active ? "true" : "false");
                    buttons[k].setAttribute("tabindex", active ? "0" : "-1");
                    panels[k].hidden = !active;
                }
            };
            buttons.forEach(function (button, index) {
                button.addEventListener("click", function () { select(index); });
            });
            list.addEventListener("keydown", function (event) {
                var current = buttons.indexOf(document.activeElement);
                if (current < 0) return;
                var count = buttons.length;
                var next = event.key === "ArrowRight" ? (current + 1) % count
                    : event.key === "ArrowLeft" ? (current - 1 + count) % count
                    : event.key === "Home" ? 0
                    : event.key === "End" ? count - 1 : -1;
                if (next < 0) return;
                event.preventDefault();
                select(next);
                buttons[next].focus();
            });
            root.insertBefore(list, root.firstChild);
            root.setAttribute("data-tabs-ready", "");
        }
    }

    // 分享列的「複製連結」：成功後以預先提供的文字回饋；不支援剪貼簿 API 時按鈕維持原狀。
    document.addEventListener("click", function (event) {
        var button = event.target instanceof Element ? event.target.closest("[data-share-url]") : null;
        if (!button || !navigator.clipboard || !navigator.clipboard.writeText) return;
        var copied = button.getAttribute("data-copied-label");
        navigator.clipboard.writeText(button.getAttribute("data-share-url") || "").then(function () {
            if (copied) button.textContent = copied;
        }, function () {});
    });

    function enhance(scope) {
        tabs(scope);
        return highlight(scope);
    }

    document.addEventListener("DOMContentLoaded", function () { enhance(document); });
    window.origamiProse = { enhance: enhance, highlight: highlight, tabs: tabs };
})();
