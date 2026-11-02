export function isPdvOnline() {
  return typeof navigator === "undefined" ? true : navigator.onLine !== false;
}
