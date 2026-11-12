/**
 * Runs in Outlook on the web and reports to background.js:
 * - the item ID visible in the URL when a message is opened,
 * - elements that look like attachments, with their data-* attributes,
 * - conversation IDs exposed as data-convid on the selected list item,
 * - clicks on links inside the reading pane.
 *
 * It reads the page only. It does not fetch, click or modify anything.
 */

const ATTACHMENT_NAME = /\.(zip|7z|pdf|xlsx?|docx?|pptx?|csv|txt)\b/i;
const ITEM_ID_IN_URL = /\/id\/([^/?#]+)/;

function send(kind, data) {
  chrome.runtime.sendMessage({ kind, data });
}

function dataAttributes(element) {
  const result = {};
  for (const attr of element.attributes) {
    if (attr.name.startsWith("data-")) result[attr.name] = attr.value.slice(0, 200);
  }
  return result;
}

function currentItemId() {
  const match = location.pathname.match(ITEM_ID_IN_URL);
  return match ? decodeURIComponent(match[1]) : null;
}

function describeAttachments() {
  const seen = new Set();
  const found = [];
  for (const element of document.querySelectorAll("[title], [aria-label]")) {
    const label = element.getAttribute("title") || element.getAttribute("aria-label");
    if (!ATTACHMENT_NAME.test(label) || seen.has(label)) continue;
    seen.add(label);
    found.push({
      label: label.slice(0, 200),
      tag: element.tagName,
      role: element.getAttribute("role"),
      data: dataAttributes(element),
      parentData: element.parentElement ? dataAttributes(element.parentElement) : {},
    });
  }
  return found.slice(0, 20);
}

function selectedConversationId() {
  const selected = document.querySelector('[data-convid][aria-selected="true"]');
  return {
    selected: selected?.getAttribute("data-convid") ?? null,
    listedCount: document.querySelectorAll("[data-convid]").length,
  };
}

let lastPath = null;

function observeOpenedMessage() {
  if (location.pathname === lastPath) return;
  lastPath = location.pathname;
  const itemId = currentItemId();
  if (!itemId) return;
  for (const delayMs of [1500, 5000]) {
    setTimeout(() => {
      if (currentItemId() !== itemId) return;
      send("message-opened", {
        delayMs,
        itemIdFromUrl: itemId,
        conversation: selectedConversationId(),
        attachments: describeAttachments(),
      });
    }, delayMs);
  }
}

document.addEventListener(
  "click",
  (event) => {
    const link = event.target.closest?.("a[href]");
    if (!link) return;
    let host = "(unparsable)";
    try {
      host = new URL(link.href).host;
    } catch {}
    send("link-clicked", { itemIdFromUrl: currentItemId(), host, target: link.target || null });
  },
  true,
);

send("content-script-loaded", { path: location.pathname.replace(ITEM_ID_IN_URL, "/id/…") });
setInterval(observeOpenedMessage, 1000);
observeOpenedMessage();
