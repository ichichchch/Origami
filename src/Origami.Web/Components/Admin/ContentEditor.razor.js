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
