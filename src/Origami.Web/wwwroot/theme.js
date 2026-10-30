// 後台外觀三態切換（auto → light → dark）：就地更新 data-color-scheme 並把偏好寫入 cookie。
// cookie 名稱與 Origami.Core 的 ThemePresets.ThemeCookieName（origami.theme）保持一致；
// 服務端在下次整份文件導覽時讀取 cookie 渲染，避免載入閃爍。
(function () {
    "use strict";
    var order = ["auto", "light", "dark"];

    function read() {
        var match = document.cookie.match(/(?:^|;\s*)origami\.theme=(auto|light|dark)(?:;|$)/);
        return match ? match[1] : "auto";
    }

    function write(value) {
        document.cookie = "origami.theme=" + value + ";path=/;max-age=31536000;samesite=lax";
    }

    function apply(value) {
        var shell = document.querySelector(".admin-shell");
        if (shell) shell.setAttribute("data-color-scheme", value);
    }

    function relabel(button, value) {
        var text = button.getAttribute("data-label-" + value);
        if (text) button.textContent = text;
        button.setAttribute("data-scheme", value);
    }

    document.addEventListener("click", function (event) {
        var button = event.target instanceof Element ? event.target.closest("[data-theme-toggle]") : null;
        if (!button) return;
        var next = order[(order.indexOf(read()) + 1) % order.length];
        write(next);
        apply(next);
        relabel(button, next);
        button.focus();
    });
})();
