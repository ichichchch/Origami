// 編輯器本文貼上或拖放檔案：轉送到隱藏的 InputFile，共用既有媒體上傳與驗證管線。
export function wireEditorUpload(textareaId, fileInputId) {
  const textarea = document.getElementById(textareaId);
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
  const textarea = document.getElementById(textareaId);
  if (!textarea) return "";
  const start = textarea.selectionStart ?? 0;
  const end = textarea.selectionEnd ?? 0;
  return start === end ? "" : textarea.value.substring(start, end);
}

// 寫作輔助側欄：把選取範圍取代為新文字；無選取時插入在游標位置。回傳操作後的完整本文。
export function replaceSelection(textareaId, text) {
  const textarea = document.getElementById(textareaId);
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

