// 儀表板圖表：以自架 Chart.js（UMD 全域）繪製 30 日瀏覽趨勢與熱門內容水平條。
// 任何一步失敗都不回呼 OnChartsReady，讓頁面保留伺服端渲染的 CSS 條與表格 fallback。
let state = null;
let chartJsPromise = null;

export async function initCharts(opts) {
  try {
    await loadChartJs(opts.chartUrl);
    const shell = document.querySelector(".admin-shell");
    if (!window.Chart || !shell) return;
    teardown();
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
  for (const chart of state.charts) {
    if (!chart) continue;
    applyPalette(chart, readPalette(shell));
    chart.update();
  }
}

function createTrendChart(trend) {
  const canvas = document.getElementById(trend.canvasId);
  if (!canvas) return null;
  window.Chart.getChart(canvas)?.destroy();
  revealFrame(canvas);
  const palette = readPalette(document.querySelector(".admin-shell"));
  const chart = new window.Chart(canvas, {
    type: "line",
    data: {
      labels: trend.labels,
      datasets: [{
        data: trend.values,
        fill: true,
        tension: 0.35,
        borderWidth: 2,
        pointRadius: 2,
        pointHitRadius: 14,
      }],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: "index", intersect: false },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            title: (items) => trend.dateTitles[items[0].dataIndex],
            label: (item) => trend.valueLabels[item.dataIndex],
          },
        },
      },
      scales: {
        x: { ticks: { maxTicksLimit: 10, maxRotation: 0 }, grid: { display: false } },
        y: { beginAtZero: true, ticks: { precision: 0 } },
      },
    },
  });
  applyPalette(chart, palette);
  return chart;
}

function createTopChart(top, texts, dotnetRef) {
  const canvas = document.getElementById(top.canvasId);
  if (!canvas) return null;
  window.Chart.getChart(canvas)?.destroy();
  revealFrame(canvas);
  const palette = readPalette(document.querySelector(".admin-shell"));
  const items = top.items;
  const axisLabel = (title) => title.length > 16 ? title.slice(0, 15) + "…" : title;
  const chart = new window.Chart(canvas, {
    type: "bar",
    data: {
      labels: items.map((item) => axisLabel(item.title)),
      datasets: [{ data: items.map((item) => item.viewsPeriod), borderRadius: 3 }],
    },
    options: {
      indexAxis: "y",
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            title: (elements) => elements.length ? items[elements[0].dataIndex].title : "",
            label: (item) => texts.legend30d + " " + items[item.dataIndex].viewsPeriodLabel
              + " · " + texts.legendTotal + " " + items[item.dataIndex].viewsTotalLabel,
          },
        },
      },
      scales: {
        x: { beginAtZero: true, ticks: { precision: 0 } },
        y: { ticks: { autoSkip: false } },
      },
      onClick: (_event, elements) => {
        if (!elements.length) return;
        dotnetRef.invokeMethodAsync("OpenContent", items[elements[0].index].id).catch(() => {});
      },
    },
  });
  applyPalette(chart, palette);
  return chart;
}

function readPalette(shell) {
  const styles = getComputedStyle(shell);
  const pick = (name, fallback) => resolveColor(styles.getPropertyValue(name), fallback);
  return {
    accent: pick("--accent", "#315d48"),
    accentSoft: pick("--accent-soft", "#eaf1e9"),
    ink: pick("--ink", "#24342d"),
    border: pick("--border", "#e0e6df"),
  };
}

// 深色模式的 --accent 是 color-mix() 運算式，自訂屬性取到的是未解析文字；
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

function applyPalette(chart, palette) {
  const isLine = chart.config.type === "line";
  for (const dataset of chart.data.datasets) {
    dataset.borderColor = palette.accent;
    dataset.backgroundColor = isLine ? palette.accentSoft : palette.accent;
    dataset.pointBackgroundColor = palette.accent;
    dataset.pointBorderColor = palette.accent;
  }
  for (const scale of Object.values(chart.options.scales || {})) {
    if (scale.ticks) scale.ticks.color = palette.ink;
    if (scale.grid) scale.grid.color = palette.border;
  }
}
