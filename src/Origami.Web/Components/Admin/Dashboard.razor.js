// 儀表板圖表：以自架 Chart.js（UMD 全域）繪製 30 日瀏覽趨勢與熱門內容水平條。
// 任何一步失敗都不回呼 OnChartsReady，讓頁面保留伺服端渲染的 CSS 條與表格 fallback。
let state = null;
let chartJsPromise = null;
let palette = null;

export async function initCharts(opts) {
  try {
    await loadChartJs(opts.chartUrl);
    const shell = document.querySelector(".admin-shell");
    if (!window.Chart || !shell) return;
    teardown();
    applyChartDefaults(shell);
    palette = readPalette(shell);
    state = { charts: [], observer: null, mql: null };
    const trend = opts.trend ? createTrendChart(opts.trend) : null;
    const top = opts.top ? createTopChart(opts.top, opts.texts, opts.dotnetRef) : null;
    if (trend) state.charts.push(trend);
    if (top) state.charts.push(top);
    // 沒有任何圖表就維持 fallback，不要讓 OnChartsReady 隱藏了內容卻沒有圖可看。
    if (!state.charts.length) { teardown(); return; }
    // 主題三態寫在 .admin-shell 的 data-color-scheme；auto 態的 OS 深色切換只改媒體查詢，兩者都要聽。
    state.observer = new MutationObserver(applyTheme);
    state.observer.observe(shell, { attributes: true, attributeFilter: ["data-color-scheme"] });
    state.mql = window.matchMedia("(prefers-color-scheme: dark)");
    state.mql.addEventListener("change", applyTheme);
    await opts.dotnetRef.invokeMethodAsync("OnChartsReady");
    for (const chart of state.charts) { if (chart) ensureVisible(chart); }
  } catch { /* 載入或初始化失敗：fallback 已在頁面上，保持原樣。 */ }
}

export function disposeCharts() {
  teardown();
}

function loadChartJs(url) {
  if (window.Chart) return Promise.resolve();
  if (chartJsPromise) return chartJsPromise;
  chartJsPromise = new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = url;
    script.onload = () => window.Chart ? resolve() : reject(new Error("Chart.js loaded without global."));
    script.onerror = () => reject(new Error("Chart.js failed to load."));
    document.head.appendChild(script);
  });
  return chartJsPromise;
}

function teardown() {
  if (!state) return;
  if (state.observer) state.observer.disconnect();
  if (state.mql) state.mql.removeEventListener("change", applyTheme);
  for (const chart of state.charts) { if (chart) chart.destroy(); }
  state = null;
}

// 圖表必須在容器可量測時建立：容器仍是 hidden（display:none）時量到 0×0，
// 頭幾個繪製週期的點擊會落空。顯示容器後由 Blazor 隱藏 CSS 條 fallback。
function revealFrame(canvas) {
  const frame = canvas.closest(".chart-frame");
  if (frame) frame.hidden = false;
}

// Blazor 重繪可能把容器重新標回 hidden；圖表需保留可見尺寸。
function ensureVisible(chart) {
  const frame = chart.canvas.closest(".chart-frame");
  if (!frame || !frame.hidden) return;
  frame.hidden = false;
  chart.resize();
}

function applyTheme() {
  if (!state) return;
  const shell = document.querySelector(".admin-shell");
  if (!shell) return;
  palette = readPalette(shell);
  for (const chart of state.charts) {
    if (!chart) continue;
    applyPalette(chart, palette);
    chart.update();
  }
}

// 圖表字體沿用後台字體堆疊，避免中文刻度與介面字型不一致。
function applyChartDefaults(shell) {
  const family = getComputedStyle(shell).fontFamily;
  if (family) window.Chart.defaults.font.family = family;
  window.Chart.defaults.font.size = 12;
}

function createTrendChart(trend) {
  const canvas = document.getElementById(trend.canvasId);
  if (!canvas) return null;
  window.Chart.getChart(canvas)?.destroy();
  revealFrame(canvas);
  const chart = new window.Chart(canvas, {
    type: "line",
    data: {
      labels: trend.labels,
      datasets: [{
        data: trend.values,
        fill: true,
        tension: 0.4,
        borderWidth: 2.25,
        borderColor: palette.accent,
        backgroundColor: (context) => areaGradient(context.chart, palette),
        pointRadius: 0,
        pointHoverRadius: 4.5,
        pointHitRadius: 16,
        pointBackgroundColor: palette.surface,
        pointHoverBackgroundColor: palette.surface,
        pointBorderColor: palette.accent,
        pointHoverBorderColor: palette.accent,
        pointHoverBorderWidth: 2.5,
      }],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: "index", intersect: false },
      animation: { duration: 320, easing: "easeOutQuart" },
      layout: { padding: { top: 12, right: 18, bottom: 6, left: 6 } },
      plugins: {
        legend: { display: false },
        tooltip: tooltipOptions(trend.dateTitles, (item) => trend.valueLabels[item.dataIndex]),
      },
      scales: {
        x: {
          grid: { display: false },
          border: { display: false },
          ticks: { color: palette.muted, maxTicksLimit: 8, maxRotation: 0, autoSkip: true, padding: 6 },
        },
        y: {
          beginAtZero: true,
          grid: { color: palette.border, borderDash: [3, 4], drawTicks: false },
          border: { display: false },
          ticks: { color: palette.muted, precision: 0, padding: 8 },
        },
      },
    },
  });
  return chart;
}

function createTopChart(top, texts, dotnetRef) {
  const canvas = document.getElementById(top.canvasId);
  if (!canvas) return null;
  window.Chart.getChart(canvas)?.destroy();
  revealFrame(canvas);
  const items = top.items;
  const axisLabel = (title) => title.length > 14 ? title.slice(0, 13) + "…" : title;
  // 卡片高度隨項目數收斂，內容少時不留大片空白。
  const frame = canvas.closest(".chart-frame");
  if (frame) frame.style.height = Math.min(320, Math.max(150, items.length * 32 + 64)) + "px";
  const chart = new window.Chart(canvas, {
    type: "bar",
    data: {
      labels: items.map((item) => axisLabel(item.title)),
      datasets: [{
        data: items.map((item) => item.viewsPeriod),
        backgroundColor: withAlpha(palette.accent, barAlpha(palette)),
        hoverBackgroundColor: palette.accent,
        borderRadius: 6,
        borderSkipped: false,
        maxBarThickness: 20,
      }],
    },
    options: {
      indexAxis: "y",
      responsive: true,
      maintainAspectRatio: false,
      animation: { duration: 320, easing: "easeOutQuart" },
      layout: { padding: { top: 12, right: 18, bottom: 6, left: 6 } },
      plugins: {
        legend: { display: false },
        tooltip: tooltipOptions(
          (elements) => elements.length ? items[elements[0].dataIndex].title : "",
          (item) => texts.legend30d + " " + items[item.dataIndex].viewsPeriodLabel
            + " · " + texts.legendTotal + " " + items[item.dataIndex].viewsTotalLabel),
      },
      scales: {
        x: {
          beginAtZero: true,
          grid: { color: palette.border, borderDash: [3, 4], drawTicks: false },
          border: { display: false },
          ticks: { color: palette.muted, precision: 0, padding: 6 },
        },
        y: {
          grid: { display: false },
          border: { display: false },
          ticks: { color: palette.ink, autoSkip: false, padding: 8 },
        },
      },
      onClick: (_event, elements) => {
        if (!elements.length) return;
        dotnetRef.invokeMethodAsync("OpenContent", items[elements[0].index].id).catch(() => {});
      },
      onHover: (event, elements) => {
        event.native.target.style.cursor = elements.length ? "pointer" : "default";
      },
    },
  });
  // 水平條形圖的分類軸（內容標題）用墨色呈現，與數值刻度的弱化色區隔。
  chart.$categoryLabelInk = true;
  return chart;
}

// tooltip 依後台主題：底色、文字與邊框都取自設計令牌，切換主題時重新套用。
function tooltipOptions(title, label) {
  return {
    backgroundColor: palette.surfaceRaised,
    titleColor: palette.ink,
    bodyColor: palette.muted,
    borderColor: palette.border,
    borderWidth: 1,
    padding: 10,
    cornerRadius: 8,
    boxPadding: 4,
    displayColors: false,
    titleFont: { weight: "600", size: 12 },
    bodyFont: { size: 12 },
    callbacks: { title, label },
  };
}

// 趨勢面積以漸層淡出，貼齊背景而不是一整塊實色。
function areaGradient(chart, colors) {
  const area = chart.chartArea;
  const dark = colors.scheme === "dark";
  if (!area) return withAlpha(colors.accent, dark ? 0.12 : 0.16);
  const gradient = chart.ctx.createLinearGradient(0, area.top, 0, area.bottom);
  gradient.addColorStop(0, withAlpha(colors.accent, dark ? 0.20 : 0.30));
  gradient.addColorStop(1, withAlpha(colors.accent, dark ? 0.01 : 0.02));
  return gradient;
}

function readPalette(shell) {
  const styles = getComputedStyle(shell);
  const pick = (name, fallback) => resolveColor(styles.getPropertyValue(name), fallback);
  return {
    accent: pick("--accent", "#315d48"),
    accentSoft: pick("--accent-soft", "#eaf1e9"),
    ink: pick("--ink", "#24342d"),
    muted: pick("--muted", "#5f6b63"),
    border: pick("--border", "#e0e6df"),
    surface: pick("--surface", "#fff"),
    surfaceRaised: pick("--surface-raised", "#fff"),
    // 深色模式的 accent 是提亮色，同樣透明度會顯得過於厚重。
    scheme: styles.colorScheme === "dark" ? "dark" : "light",
  };
}

// 深色模式的 accent 是 color-mix() 運算式，自訂屬性取到的是未解析文字；
// 交由 CSS 引擎解析成實際色彩，Chart.js 才認得。
function resolveColor(value, fallback) {
  const trimmed = (value || "").trim();
  if (!trimmed) return fallback;
  const probe = document.createElement("span");
  probe.style.color = trimmed;
  document.body.appendChild(probe);
  const resolved = getComputedStyle(probe).color;
  probe.remove();
  return resolved && resolved !== trimmed ? resolved : trimmed;
}

// 瀏覽器把 color-mix() 的計算值寫成 oklab(...)，自行換算代價高；
// 改由 CSS 引擎產生帶透明度的變體，Chart.js 與 canvas 都能直接使用。
function withAlpha(color, alpha) {
  const value = (color || "").trim();
  if (!value) return color;
  const probe = document.createElement("span");
  probe.style.color = "color-mix(in srgb, " + value + " " + Math.round(alpha * 100) + "%, transparent)";
  document.body.appendChild(probe);
  const resolved = getComputedStyle(probe).color;
  probe.remove();
  return resolved || value;
}

// 深色模式的 accent 是提亮色，條形與面積需降低不透明度才不會壓過畫面。
function barAlpha(colors) {
  return colors.scheme === "dark" ? 0.72 : 0.85;
}

function applyPalette(chart, colors) {
  const isLine = chart.config.type === "line";
  for (const dataset of chart.data.datasets) {
    dataset.borderColor = colors.accent;
    dataset.pointBorderColor = colors.accent;
    dataset.pointHoverBorderColor = colors.accent;
    dataset.pointBackgroundColor = colors.surface;
    dataset.pointHoverBackgroundColor = colors.surface;
    if (!isLine) {
      dataset.backgroundColor = withAlpha(colors.accent, barAlpha(colors));
      dataset.hoverBackgroundColor = colors.accent;
    }
  }
  for (const scale of Object.values(chart.options.scales || {})) {
    if (scale.ticks) {
      scale.ticks.color = scale.type === "category" && chart.$categoryLabelInk ? colors.ink : colors.muted;
    }
    if (scale.grid && scale.grid.color) scale.grid.color = colors.border;
  }
  const tooltip = chart.options.plugins && chart.options.plugins.tooltip;
  if (tooltip) {
    tooltip.backgroundColor = colors.surfaceRaised;
    tooltip.titleColor = colors.ink;
    tooltip.bodyColor = colors.muted;
    tooltip.borderColor = colors.border;
  }
}
