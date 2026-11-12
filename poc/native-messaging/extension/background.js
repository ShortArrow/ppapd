// Relays messages from the content script to the Native Messaging host over one
// long-lived port, and the host's replies back to the tab that asked.
// Also reports tab creation and downloads, so the host log shows whether a download
// can be traced back to the mail whose link started it. Only host names leave the browser.

const HOST = "jp.shortarrow.ppapd.poc";

let port = null;
const pending = new Map();
let nextSeq = 1;

function ensurePort() {
  if (port) return port;
  port = chrome.runtime.connectNative(HOST);
  port.onMessage.addListener((reply) => {
    const sendResponse = pending.get(reply.seq);
    pending.delete(reply.seq);
    if (sendResponse) sendResponse({ ok: true, reply });
  });
  port.onDisconnect.addListener(() => {
    const error = chrome.runtime.lastError?.message ?? "disconnected";
    for (const sendResponse of pending.values()) sendResponse({ ok: false, error });
    pending.clear();
    port = null;
  });
  return port;
}

function post(message, sendResponse) {
  const seq = nextSeq++;
  if (sendResponse) pending.set(seq, sendResponse);
  try {
    ensurePort().postMessage({ ...message, seq });
  } catch (e) {
    pending.delete(seq);
    sendResponse?.({ ok: false, error: String(e) });
  }
}

function hostOf(url) {
  try {
    return url ? new URL(url).host : "";
  } catch {
    return "unparseable";
  }
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  post({ ...message, tabId: sender.tab?.id }, sendResponse);
  return true;
});

chrome.tabs.onCreated.addListener((tab) => {
  post({ kind: "tab-created", tabId: tab.id, openerTabId: tab.openerTabId, urlHost: hostOf(tab.pendingUrl || tab.url) });
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.url) post({ kind: "tab-navigated", tabId, urlHost: hostOf(changeInfo.url) });
});

chrome.downloads.onCreated.addListener((item) => {
  post({
    kind: "download-created",
    downloadId: item.id,
    urlHost: hostOf(item.url),
    finalUrlHost: hostOf(item.finalUrl),
    referrerHost: hostOf(item.referrer),
    byExtensionId: item.byExtensionId ?? "",
  });
});

chrome.downloads.onChanged.addListener((delta) => {
  if (delta.state?.current !== "complete") return;
  chrome.downloads.search({ id: delta.id }, ([item]) => {
    post({ kind: "download-complete", downloadId: delta.id, fileName: item?.filename.split(/[\\/]/).pop() ?? "" });
  });
});
