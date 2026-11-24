// 編輯器本文：檔案貼上／拖放轉送、游標操作（格式化、行前綴、區塊插入）、尋找取代、
// 鍵盤快捷鍵、專注模式與 HTML 貼上轉 Markdown。改值後都會通知 Blazor 的 oninput 繫結：
// 編輯中的操作走瀏覽器原生 insertText（保留復原堆疊並觸發 input 事件），
// 其餘直接設值並派發 input 事件。各函式都回傳新值，呼叫端可再核對長度上限。

function getTextarea(id) {
  const textarea = document.getElementById(id);
  return textarea instanceof HTMLTextAreaElement ? textarea : null;
}

// 只取代前後綴之間的差異，讓復原堆疊記成一次「插入」。
function write(textarea, next, selectionStart, selectionEnd, keepFocus) {
  const current = textarea.value;
  if (next === current) {
    textarea.setSelectionRange(selectionStart, selectionEnd);
    return;
  }
  let inserted = false;
  if (keepFocus) {
    let prefix = 0;
    const limit = Math.min(current.length, next.length);
    while (prefix < limit && current.charCodeAt(prefix) === next.charCodeAt(prefix)) prefix++;
    let suffix = 0;
    const maxSuffix = Math.min(current.length, next.length) - prefix;
    while (suffix < maxSuffix && current.charCodeAt(current.length - 1 - suffix) === next.charCodeAt(next.length - 1 - suffix)) suffix++;
    textarea.focus();
    textarea.setSelectionRange(prefix, current.length - suffix);
    try {
      inserted = document.execCommand("insertText", false, next.substring(prefix, next.length - suffix));
    } catch {
      inserted = false;
    }
    if (inserted && textarea.value !== next) inserted = false;
  }
  if (!inserted) {
    textarea.value = next;
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
  }
  textarea.setSelectionRange(selectionStart, selectionEnd);
  if (keepFocus) textarea.focus();
}

function reveal(textarea, index) {
  const lineHeight = parseFloat(getComputedStyle(textarea).lineHeight) || 24;
  const line = textarea.value.substring(0, index).split("\n").length;
  textarea.scrollTop = Math.max(0, (line - 3) * lineHeight);
}

function wrap(textarea, before, after, placeholder) {
  const start = textarea.selectionStart ?? textarea.value.length;
  const end = textarea.selectionEnd ?? start;
  const selected = start === end ? placeholder : textarea.value.substring(start, end);
  const next = textarea.value.substring(0, start) + before + selected + after + textarea.value.substring(end);
  write(textarea, next, start + before.length, start + before.length + selected.length, true);
  return next;
}

export function wrapSelection(textareaId, before, after, placeholder) {
  const textarea = getTextarea(textareaId);
  return textarea ? wrap(textarea, before, after, placeholder) : null;
}

// 在選取範圍涵蓋的每一行加上 prefix；全部都已帶 prefix 時改為移除（切換）。
// stripPattern 用於標題：換級距時先剝掉既有的 # 標記。
export function prefixLines(textareaId, prefix, stripPattern) {
  const textarea = getTextarea(textareaId);
  if (!textarea) return null;
  const value = textarea.value;
  const start = textarea.selectionStart ?? 0;
  const end = textarea.selectionEnd ?? start;
  const lineStart = start === 0 ? 0 : value.lastIndexOf("\n", start - 1) + 1;
  // 選取若結束在換行字元上，該換行之後不算進來。
  const selectionEnd = end > start && value[end - 1] === "\n" ? end - 1 : end;
  let lineEnd = value.indexOf("\n", selectionEnd);
  if (lineEnd < 0) lineEnd = value.length;
  const lines = value.substring(lineStart, lineEnd).split("\n");
  const strip = stripPattern ? new RegExp(stripPattern) : null;
  const allPrefixed = lines.every((line) => line.startsWith(prefix));
  const block = lines
    .map((line) => {
      const bare = line.startsWith(prefix) ? line.slice(prefix.length) : (strip ? line.replace(strip, "") : line);
      return allPrefixed ? bare : prefix + bare;
    })
    .join("\n");
  const caret = lineStart + block.length;
  write(textarea, value.substring(0, lineStart) + block + value.substring(lineEnd), caret, caret, true);
  return textarea.value;
}

// 在游標位置插入區塊，前後補足空行。使用者未曾點入本文時，沿用「加在結尾」。
export function insertBlock(textareaId, text) {
  const textarea = getTextarea(textareaId);
  if (!textarea) return null;
  const value = textarea.value;
  let start = textarea.selectionStart ?? 0;
  let end = textarea.selectionEnd ?? start;
  if (textarea.dataset.touched !== "1" && value.length > 0) {
    start = end = value.length;
  }
  // 有選取範圍時插在選取文字之後，不取代使用者選取的內容。
  if (end > start) start = end;
  const head = value.substring(0, start);
  const tail = value.substring(end);
  const leading = start === 0 || /\n[ \t]*\n[ \t]*$/.test(head) ? "" : (/\n[ \t]*$/.test(head) ? "\n" : "\n\n");
  const trailing = end === value.length || /^\n[ \t]*\n/.test(tail) ? "" : (/^\n/.test(tail) ? "\n" : "\n\n");
  const inserted = leading + text + trailing;
  const caret = start + inserted.length;
  write(textarea, head + inserted + tail, caret, caret, true);
  return textarea.value;
}

function fold(value, term, caseSensitive) {
  return caseSensitive ? { hay: value, needle: term } : { hay: value.toLowerCase(), needle: term.toLowerCase() };
}

// 從游標之後找下一個相符文字並選取（到底後從頭循環）；回傳 { index, total }，找不到時 index 為 -1。
export function findText(textareaId, term, caseSensitive) {
  const textarea = getTextarea(textareaId);
  if (!textarea || !term) return null;
  const { hay, needle } = fold(textarea.value, term, caseSensitive);
  if (!needle) return { index: -1, total: 0 };
  let total = 0;
  for (let position = hay.indexOf(needle); position >= 0; position = hay.indexOf(needle, position + needle.length)) total++;
  let index = hay.indexOf(needle, textarea.selectionEnd ?? 0);
  if (index < 0) index = hay.indexOf(needle);
  if (index >= 0) {
    textarea.setSelectionRange(index, index + term.length);
    reveal(textarea, index);
  }
  return { index, total };
}

// 目前的選取若是相符文字就取代，再選取下一處；回傳 { value, replaced, found }。
export function replaceNext(textareaId, term, replacement, caseSensitive) {
  const textarea = getTextarea(textareaId);
  if (!textarea || !term) return null;
  const value = textarea.value;
  const start = textarea.selectionStart ?? 0;
  const end = textarea.selectionEnd ?? start;
  let next = value;
  let replaced = false;
  const current = fold(value, term, caseSensitive);
  if (end > start && end - start === term.length && current.hay.substring(start, end) === current.needle) {
    next = value.substring(0, start) + replacement + value.substring(end);
    replaced = true;
  }
  const folded = fold(next, term, caseSensitive);
  let index = folded.hay.indexOf(folded.needle, start);
  if (index < 0) index = folded.hay.indexOf(folded.needle);
  if (index >= 0) {
    write(textarea, next, index, index + term.length, false);
    reveal(textarea, index);
  } else {
    write(textarea, next, start, start, false);
  }
  return { value: replaced ? next : null, replaced, found: index >= 0 };
}

export function replaceAll(textareaId, term, replacement, caseSensitive) {
  const textarea = getTextarea(textareaId);
  if (!textarea || !term) return null;
  const value = textarea.value;
  const { hay, needle } = fold(value, term, caseSensitive);
  let result = "";
  let count = 0;
  let position = 0;
  for (let index = hay.indexOf(needle); index >= 0; index = hay.indexOf(needle, position)) {
    result += value.substring(position, index) + replacement;
    position = index + term.length;
    count++;
  }
  result += value.substring(position);
  const caret = Math.min(textarea.selectionStart ?? 0, result.length);
  write(textarea, result, caret, caret, false);
  reveal(textarea, caret);
  return { value: result, count };
}

export function setFocusMode(panelId, on) {
  const panel = document.getElementById(panelId);
  if (!panel) return false;
  panel.classList.toggle("editor-focus", on === true);
  return true;
}

export function wireEditorKeys(textareaId, dotNetRef, labels) {
  const textarea = getTextarea(textareaId);
  if (!textarea) return;
  // 本文在預覽切換後會重新建立，每次渲染都會呼叫；只在尚未掛上時綁定。
  if (textarea.dataset.keysWired === "1") return;
  textarea.dataset.keysWired = "1";
  ["focus", "click", "keyup", "input"].forEach((type) =>
    textarea.addEventListener(type, () => { textarea.dataset.touched = "1"; }));
  textarea.addEventListener("keydown", (event) => {
    // 輸入法組字中不攔截任何按鍵。
    if (event.isComposing || event.altKey) return;
    if (event.key === "Escape") {
      dotNetRef.invokeMethodAsync("ShortcutEscapeAsync");
      return;
    }
    if (event.key === "Tab" && !event.shiftKey && !event.ctrlKey && !event.metaKey) {
      // 只接住 Tab；Shift+Tab 仍可離開欄位，鍵盤使用者不會被困住。
      event.preventDefault();
      textarea.focus();
      document.execCommand("insertText", false, "  ");
      return;
    }
    if (!(event.ctrlKey || event.metaKey) || event.shiftKey) return;
    switch (event.key.toLowerCase()) {
      case "b": event.preventDefault(); wrap(textarea, "**", "**", labels.bold); break;
      case "i": event.preventDefault(); wrap(textarea, "*", "*", labels.italic); break;
      case "k": event.preventDefault(); wrap(textarea, "[", "](https://)", labels.link); break;
      case "f": event.preventDefault(); dotNetRef.invokeMethodAsync("OpenFindAsync"); break;
      case "s": event.preventDefault(); dotNetRef.invokeMethodAsync("ShortcutSaveAsync"); break;
    }
  });
}

// 只有帶語意標記的 HTML 才轉換；純文字來源（程式碼編輯器等）維持原生貼上，保留縮排。
function isRich(html) {
  return html.length <= 500000 && /<(a|strong|b|em|i|h[1-6]|ul|ol|li|table|img|pre|blockquote|code)\b/i.test(html);
}

export function wireEditorPaste(textareaId, dotNetRef) {
  const textarea = getTextarea(textareaId);
  if (!textarea) return;
  if (textarea.dataset.pasteWired === "1") return;
  textarea.dataset.pasteWired = "1";
  textarea.addEventListener("paste", async (event) => {
    const clipboard = event.clipboardData;
    // 檔案交給既有的上傳流程；Shift+Ctrl+V 明確要求純文字。
    if (!clipboard || event.shiftKey || (clipboard.files && clipboard.files.length > 0)) return;
    const plain = clipboard.getData("text/plain");
    const html = clipboard.getData("text/html");
    if (!plain || !isRich(html)) return;
    event.preventDefault();
    const start = textarea.selectionStart ?? textarea.value.length;
    const end = textarea.selectionEnd ?? start;
    const snapshot = textarea.value;
    let markdown;
    try {
      markdown = await dotNetRef.invokeMethodAsync("ConvertPastedHtmlAsync", html);
    } catch {
      // 電路中斷或轉換失敗時仍保留純文字貼上，且不產生未處理的 Promise rejection。
      const current = getTextarea(textareaId);
      if (current?.isConnected) insertAtCaret(current, plain);
      return;
    }
    // 預覽切換會重建 textarea；不可把結果寫回已離開 DOM 的舊元素。
    const current = getTextarea(textareaId);
    if (!current?.isConnected) return;
    const pasted = typeof markdown === "string" && markdown.length > 0 ? markdown : plain;
    if (current.value !== snapshot) {
      // 等待轉換期間本文已變動：改貼在目前游標位置。
      insertAtCaret(current, pasted);
      return;
    }
    current.setSelectionRange(start, end);
    insertAtCaret(current, pasted);
  });
}

// 尋找面板每次開啟都會重新建立：在面板上攔截輸入框的 Enter，避免送出外層表單（儲存）。
// 其他按鍵與焦點移出（Tab）不受影響。
export function wireFindPanel(panelId) {
  const panel = document.getElementById(panelId);
  if (!panel || panel.dataset.findWired === "1") return;
  panel.dataset.findWired = "1";
  panel.addEventListener("keydown", (event) => {
    if (event.key === "Enter" && event.target instanceof HTMLInputElement) event.preventDefault();
  });
}

function insertAtCaret(textarea, text) {
  textarea.focus();
  if (!document.execCommand("insertText", false, text)) {
    const start = textarea.selectionStart ?? textarea.value.length;
    const end = textarea.selectionEnd ?? start;
    const value = textarea.value.substring(0, start) + text + textarea.value.substring(end);
    write(textarea, value, start + text.length, start + text.length, true);
  }
}

// 編輯器本文貼上或拖放檔案：轉送到隱藏的 InputFile，共用既有媒體上傳與驗證管線。
export function wireEditorUpload(textareaId, fileInputId) {
  const textarea = getTextarea(textareaId);
  const input = document.getElementById(fileInputId);
  if (!textarea || !input || textarea.dataset.uploadWired === "1") return;
  textarea.dataset.uploadWired = "1";

  const forwardFiles = (fileList) => {
    if (!fileList || fileList.length === 0) return false;
    const dt = new DataTransfer();
    for (let i = 0; i < fileList.length; i++) {
      dt.items.add(fileList[i]);
    }
    input.files = dt.files;
    input.dispatchEvent(new Event("change"));
    return true;
  };

  textarea.addEventListener("paste", (event) => {
    const items = event.clipboardData && event.clipboardData.files;
    if (items && items.length > 0) {
      event.preventDefault();
      forwardFiles(items);
    }
  });

  textarea.addEventListener("dragover", (event) => {
    if (event.dataTransfer && event.dataTransfer.types && Array.from(event.dataTransfer.types).includes("Files")) {
      event.preventDefault();
    }
  });

  textarea.addEventListener("drop", (event) => {
    const files = event.dataTransfer && event.dataTransfer.files;
    if (files && files.length > 0) {
      event.preventDefault();
      forwardFiles(files);
    }
  });
}

// 寫作輔助側欄：讀取本文目前的選取文字（無選取回空字串）。
export function getSelection(textareaId) {
  const textarea = getTextarea(textareaId);
  if (!textarea) return "";
  const start = textarea.selectionStart ?? 0;
  const end = textarea.selectionEnd ?? 0;
  return start === end ? "" : textarea.value.substring(start, end);
}

// 寫作輔助側欄：把選取範圍取代為新文字；無選取時插入在游標位置。回傳操作後的完整本文。
export function replaceSelection(textareaId, text) {
  const textarea = getTextarea(textareaId);
  if (!textarea) return null;
  const start = textarea.selectionStart ?? textarea.value.length;
  const end = textarea.selectionEnd ?? textarea.value.length;
  const value = textarea.value.substring(0, start) + text + textarea.value.substring(end);
  const caret = start + text.length;
  textarea.value = value;
  textarea.setSelectionRange(caret, caret);
  textarea.dispatchEvent(new Event("change"));
  return value;
}
