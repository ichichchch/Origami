// 媒體庫拖放：把拖入的檔案轉送給同一個多選 InputFile，
// 讓選取與拖放共用同一條上傳與驗證管線。
export function wireDropZone(dropZoneId, inputId) {
  const zone = document.getElementById(dropZoneId);
  const input = document.getElementById(inputId);
  if (!zone || !input) return;
  zone.addEventListener("dragover", (event) => {
    event.preventDefault();
    zone.classList.add("drag-over");
  });
  zone.addEventListener("dragleave", () => zone.classList.remove("drag-over"));
  zone.addEventListener("drop", (event) => {
    event.preventDefault();
    zone.classList.remove("drag-over");
    if (event.dataTransfer && event.dataTransfer.files.length > 0) {
      input.files = event.dataTransfer.files;
      input.dispatchEvent(new Event("change"));
    }
  });
}
