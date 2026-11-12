/**
 * Collects observations from content.js, OWA network requests, tabs and
 * downloads into chrome.storage.local, and exports them as JSON when the
 * toolbar icon is clicked.
 *
 * Query-string values and URL fragments are redacted before storage so the
 * exported log carries endpoint shapes, not tokens.
 */

const LOG_KEY = "log";
const LOG_LIMIT = 1000;
const ATTACHMENT_URL = /attach|service\.svc|download/i;

/** Query parameters whose values name an operation rather than carry data or tokens. */
const KEPT_PARAMS = new Set(["action", "app", "scriptVer"]);

/** Returns origin + path + query parameter names; values are dropped except for KEPT_PARAMS. */
function redactUrl(raw) {
  if (!raw) return raw;
  if (raw.startsWith("data:")) return "data:…";
  try {
    const url = new URL(raw);
    const params = [...url.searchParams].map(([n, v]) => `${n}=${KEPT_PARAMS.has(n) ? v : "…"}`);
    return `${url.origin}${url.pathname}${params.length ? "?" + params.join("&") : ""}`;
  } catch {
    return "(unparsable)";
  }
}

let writes = Promise.resolve();

/** Appends one entry. Writes are chained so concurrent events do not overwrite each other. */
function record(kind, data) {
  writes = writes.then(async () => {
    const { [LOG_KEY]: log = [] } = await chrome.storage.local.get(LOG_KEY);
    log.push({ at: new Date().toISOString(), kind, ...data });
    await chrome.storage.local.set({ [LOG_KEY]: log.slice(-LOG_LIMIT) });
  });
  return writes;
}

/** Browser name and full version, extension version and OS, for telling observations apart by version. */
async function environment() {
  const data = navigator.userAgentData;
  const high = data ? await data.getHighEntropyValues(["fullVersionList", "platformVersion"]) : {};
  return {
    extensionVersion: chrome.runtime.getManifest().version,
    browsers: high.fullVersionList?.map((b) => `${b.brand} ${b.version}`) ?? null,
    platform: data ? `${data.platform} ${high.platformVersion ?? ""}`.trim() : null,
    userAgent: navigator.userAgent,
  };
}

chrome.runtime.onInstalled.addListener(async (details) => {
  record("extension-installed", { reason: details.reason, ...(await environment()) });
});

chrome.runtime.onStartup.addListener(async () => {
  record("browser-started", await environment());
});

chrome.runtime.onMessage.addListener((message, sender) => {
  record(message.kind, { tabId: sender.tab?.id, pageHost: sender.url ? new URL(sender.url).host : null, ...message.data });
});

chrome.webRequest.onBeforeRequest.addListener(
  (details) => {
    if (!ATTACHMENT_URL.test(details.url)) return;
    record("owa-request", {
      tabId: details.tabId,
      method: details.method,
      type: details.type,
      url: redactUrl(details.url),
    });
    if (/\/GetFileAttachment\?/.test(details.url) && details.tabId !== -1) {
      refetchAttachment(details.url);
    }
  },
  { urls: ["<all_urls>"] },
);

/** Records the header names (never values) Outlook sends with GetFileAttachment, to learn how it authenticates. */
chrome.webRequest.onSendHeaders.addListener(
  (details) => {
    if (!/\/GetFileAttachment\?/.test(details.url)) return;
    record("attachment-request-headers", {
      tabId: details.tabId,
      headerNames: (details.requestHeaders ?? []).map((h) => h.name.toLowerCase()).sort(),
    });
  },
  { urls: ["<all_urls>"] },
  ["requestHeaders", "extraHeaders"],
);

const refetchedUrls = new Set();

/**
 * Fetches an attachment URL that Outlook itself just requested, from the extension
 * instead of the page, and records the size and SHA-256 of what came back.
 * Answers whether an attachment can be fetched again by its URL alone. The URL is
 * held only in memory and never written to the log.
 */
async function refetchAttachment(url) {
  if (refetchedUrls.has(url)) return;
  refetchedUrls.add(url);
  try {
    const response = await fetch(url, { credentials: "include" });
    const bytes = await response.arrayBuffer();
    const digest = await crypto.subtle.digest("SHA-256", bytes);
    const sha256 = [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
    record("attachment-refetched", {
      status: response.status,
      contentType: response.headers.get("content-type"),
      bytes: bytes.byteLength,
      sha256,
      url: redactUrl(url),
    });
  } catch (error) {
    record("attachment-refetch-failed", { error: String(error), url: redactUrl(url) });
  }
}

chrome.tabs.onCreated.addListener((tab) => {
  record("tab-created", { tabId: tab.id, openerTabId: tab.openerTabId, url: redactUrl(tab.pendingUrl || tab.url) });
});

/** True for the log export itself, which byExtensionId does not reliably mark for data: URLs. */
function isOwnExport(item) {
  return item.byExtensionId === chrome.runtime.id || item.url.startsWith("data:application/json");
}

chrome.downloads.onCreated.addListener((item) => {
  if (isOwnExport(item)) return;
  record("download-created", {
    downloadId: item.id,
    url: redactUrl(item.url),
    finalUrl: redactUrl(item.finalUrl),
    referrer: redactUrl(item.referrer),
    mime: item.mime,
  });
});

chrome.downloads.onChanged.addListener(async (delta) => {
  if (delta.state?.current !== "complete") return;
  const [item] = await chrome.downloads.search({ id: delta.id });
  if (!item || isOwnExport(item)) return;
  record("download-complete", {
    downloadId: item.id,
    filename: item.filename,
    fileSize: item.fileSize,
    referrer: redactUrl(item.referrer),
  });
});

chrome.action.onClicked.addListener(async () => {
  await record("log-exported", await environment());
  const { [LOG_KEY]: log = [] } = await chrome.storage.local.get(LOG_KEY);
  const json = JSON.stringify(log, null, 2);
  const url = "data:application/json;charset=utf-8," + encodeURIComponent(json);
  await chrome.downloads.download({ url, filename: "ppapd-poc-log.json", saveAs: true });
});
