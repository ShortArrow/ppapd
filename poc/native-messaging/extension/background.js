// Relays messages from the content script to the Native Messaging host over one
// long-lived port, and the host's replies back to the tab that asked.

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

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  const seq = nextSeq++;
  pending.set(seq, sendResponse);
  try {
    ensurePort().postMessage({ ...message, seq });
  } catch (e) {
    pending.delete(seq);
    sendResponse({ ok: false, error: String(e) });
  }
  return true;
});
