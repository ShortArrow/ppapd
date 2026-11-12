// Sends an event to the native host when a mail is opened (the URL changes) or the
// visible mail list changes, and puts a "済" mark on every list item the host answers with.
// Only the conversation IDs of the visible list items leave the page.

const MARK_CLASS = "ppapd-poc-mark";
let lastUrl = location.href;
let lastListKey = "";
let prevRoundTripMs = null;
let timer = null;

function visibleConvids() {
  return [...document.querySelectorAll("[data-convid]")].map((e) => e.getAttribute("data-convid"));
}

function mark(convids) {
  const wanted = new Set(convids);
  for (const item of document.querySelectorAll("[data-convid]")) {
    if (!wanted.has(item.getAttribute("data-convid")) || item.querySelector(`.${MARK_CLASS}`)) continue;
    const badge = document.createElement("span");
    badge.className = MARK_CLASS;
    badge.textContent = "済";
    badge.style.cssText = "margin-left:4px;padding:0 4px;border-radius:4px;background:#2e7d32;color:#fff;font-size:11px;";
    item.appendChild(badge);
  }
}

async function send(kind) {
  const convids = visibleConvids();
  const startedAt = performance.now();
  const response = await chrome.runtime.sendMessage({ kind, convids, prevRoundTripMs });
  prevRoundTripMs = Math.round(performance.now() - startedAt);
  if (response?.ok) {
    mark(response.reply.delivered);
  } else {
    console.warn("[ppapd-poc]", kind, response?.error);
  }
}

function check() {
  if (location.href !== lastUrl) {
    lastUrl = location.href;
    send("message-opened");
  }
  const listKey = visibleConvids().join(",");
  if (listKey !== lastListKey) {
    lastListKey = listKey;
    send("list-changed");
  }
}

new MutationObserver((mutations) => {
  if (mutations.every((m) => [...m.addedNodes].every((n) => n.classList?.contains(MARK_CLASS)))) return;
  clearTimeout(timer);
  timer = setTimeout(check, 500);
}).observe(document.body, { childList: true, subtree: true });

check();
