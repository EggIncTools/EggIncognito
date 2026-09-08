
import { get as getRaw, set as setRaw } from "./uiPrefs.js";

const REDACTION_KEY = "capture.redaction";
const SHOW_HEADERS_KEY = "capture.showHeaders";
const AUTOSCROLL_KEY = "capture.autoScroll";
const COMPARE_KEY = "capture.compareToKnown";
const DEFAULT_FORMAT_KEY = "capture.defaultFormat";
const SETUP_EXPANDED_KEY = "capture.setupExpanded";

const REDACTION_MODES = new Set(["off", "blur", "redact"]);

export function getRedactionMode() {
  const stored = getRaw(REDACTION_KEY);
  return REDACTION_MODES.has(stored) ? stored : "blur";
}
export function setRedactionMode(mode) {
  if (REDACTION_MODES.has(mode)) setRaw(REDACTION_KEY, mode);
}

export function getShowHeaders() { return getRaw(SHOW_HEADERS_KEY) === "true"; }
export function setShowHeaders(value) { setRaw(SHOW_HEADERS_KEY, String(!!value)); }
export function getAutoScroll() { return getRaw(AUTOSCROLL_KEY) !== "false"; }
export function setAutoScroll(value) { setRaw(AUTOSCROLL_KEY, String(!!value)); }

export function getCompareToKnown() { return getRaw(COMPARE_KEY) === "true"; }
export function setCompareToKnown(value) { setRaw(COMPARE_KEY, String(!!value)); }

export function getDefaultFormat() { return getRaw(DEFAULT_FORMAT_KEY) || "json-tree"; }
export function setDefaultFormat(value) { setRaw(DEFAULT_FORMAT_KEY, String(value || "json-tree")); }

export function getSetupExpanded() { return getRaw(SETUP_EXPANDED_KEY) !== "false"; }
export function setSetupExpanded(value) { setRaw(SETUP_EXPANDED_KEY, String(!!value)); }
