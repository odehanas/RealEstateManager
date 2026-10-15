"""Capture the screenshots used in the help and the user manual.

Opens each screen listed in Tools/screenshots.json in a hidden Chrome (or Edge), signs
in as staff or as a tenant as the screen requires, and saves:

    wwwroot/help/images/<key>.webp      the picture, for the screens
    wwwroot/help/images/<key>.jpg       the same picture, for the printed manual and PDF
    wwwroot/help/images/shots.json      its size, caption and where each numbered
                                        call-out sits on it

Topics show a screenshot with one tag - <figure data-shot="key"></figure> - so after a
screen changes, re-running this refreshes every topic that shows it. Nothing else has to
be edited, and the help picks the new pictures up without a restart.

USAGE
    python Tools/capture_screenshots.py                     # every screen
    python Tools/capture_screenshots.py --only vendor-bills-edit,pdc-receipts

ALWAYS RUN IT AGAINST A DEMO COPY, NEVER THE LIVE SITE.
Help images are public files, and the manual page is open without signing in. A
screenshot of the live system would publish real tenants' names and balances. The tool
therefore refuses any address other than localhost unless --allow-remote is given, and
that flag should only ever be used for a demo site.

Requires:  pip install websocket-client
"""
import argparse
import base64
import json
import pathlib
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.parse
import urllib.request

import websocket  # websocket-client

PROJECT = pathlib.Path(__file__).resolve().parent.parent
CONFIG = PROJECT / "Tools" / "screenshots.json"
OUT_DIR = PROJECT / "wwwroot" / "help" / "images"
MANIFEST = OUT_DIR / "shots.json"

DEVICES = {
    # Staff screens: a typical laptop. Wide enough that the menus are not collapsed.
    "desktop": {"width": 1366, "height": 900, "scale": 1, "mobile": False},
    # Tenant portal: a typical phone, captured at 2x so it stays sharp in the phone frame.
    "phone": {"width": 390, "height": 844, "scale": 2, "mobile": True},
}

BROWSERS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    "google-chrome",
    "chromium",
]


# ---------------------------------------------------------------------------
#  A minimal Chrome DevTools Protocol client
# ---------------------------------------------------------------------------
class Browser:
    def __init__(self, port):
        with urllib.request.urlopen(f"http://127.0.0.1:{port}/json") as response:
            pages = [t for t in json.load(response) if t.get("type") == "page"]
        self.ws = websocket.create_connection(pages[0]["webSocketDebuggerUrl"], timeout=60, suppress_origin=True)
        self.next_id = 0
        self.send("Page.enable")
        self.send("Runtime.enable")

    def send(self, method, params=None):
        self.next_id += 1
        my_id = self.next_id
        self.ws.send(json.dumps({"id": my_id, "method": method, "params": params or {}}))
        while True:
            message = json.loads(self.ws.recv())
            if message.get("id") == my_id:
                if "error" in message:
                    raise RuntimeError(f"{method}: {message['error']}")
                return message.get("result", {})

    def wait_for(self, event, timeout=30):
        deadline = time.time() + timeout
        while time.time() < deadline:
            message = json.loads(self.ws.recv())
            if message.get("method") == event:
                return message
        raise TimeoutError(event)

    def navigate(self, url):
        self.send("Page.navigate", {"url": url})
        self.wait_for("Page.loadEventFired")
        time.sleep(0.5)  # let scripts settle and fonts render

    def evaluate(self, expression):
        result = self.send("Runtime.evaluate", {
            "expression": expression, "awaitPromise": True, "returnByValue": True})
        if result.get("exceptionDetails"):
            raise RuntimeError(result["exceptionDetails"].get("text", "script error"))
        return result.get("result", {}).get("value")

    def device(self, name):
        d = DEVICES[name]
        self.send("Emulation.setDeviceMetricsOverride", {
            "width": d["width"], "height": d["height"],
            "deviceScaleFactor": d["scale"], "mobile": d["mobile"]})
        return d

    def close(self):
        self.ws.close()


# ---------------------------------------------------------------------------
#  Capture
# ---------------------------------------------------------------------------
LOGIN_SCRIPT = """
(async () => {
  const form = document.querySelector('form[method="post"], form[method="POST"]');
  if (!form) return 'no form';
  form.querySelector('[name="Username"]').value = %s;
  form.querySelector('[name="Password"]').value = %s;
  form.querySelector('[type="submit"]').click();
  return 'ok';
})()
"""

MEASURE_SCRIPT = """
(() => {
  const selectors = %s;
  const maxHeight = %d;
  const height = Math.min(Math.max(document.documentElement.scrollHeight, window.innerHeight), maxHeight);
  const points = selectors.map(sel => {
    const el = document.querySelector(sel);
    if (!el) return null;
    const r = el.getBoundingClientRect();
    // Sit the call-out just OUTSIDE the element's left edge, vertically centred on its
    // first line, so it points at the control without covering what is written in it.
    // Where there is no room on the left it moves inside the edge instead.
    const x = r.left >= 26 ? r.left - 15 : r.left + 15;
    return { x: x + window.scrollX, y: r.top + window.scrollY + Math.min(r.height / 2, 18) };
  });
  return { height, width: window.innerWidth, points };
})()
"""


def sign_in(browser, site, who, credentials):
    path = "/portal/login" if who == "tenant" else "/Auth/Login"
    user, password = credentials[who]
    browser.send("Network.clearBrowserCookies")
    browser.navigate(site + path)
    browser.evaluate(LOGIN_SCRIPT % (json.dumps(user), json.dumps(password)))
    time.sleep(2.5)  # the sign-in redirect completes; the next navigate waits for its own load


def capture(browser, site, shot):
    device = browser.device(shot.get("device", "desktop"))
    browser.navigate(site + shot["url"])

    if shot.get("before"):
        browser.evaluate(shot["before"])
        time.sleep(float(shot.get("settle", 1.0)))

    markers = shot.get("markers", [])
    measured = browser.evaluate(MEASURE_SCRIPT % (
        json.dumps([m["selector"] for m in markers]), int(shot.get("maxHeight", 1100))))

    width, height = measured["width"], measured["height"]
    image = browser.send("Page.captureScreenshot", {
        "format": "webp", "quality": 84, "captureBeyondViewport": True,
        "clip": {"x": 0, "y": 0, "width": width, "height": height, "scale": 1}})

    out = OUT_DIR / f"{shot['key']}.webp"
    out.write_bytes(base64.b64decode(image["data"]))

    # A JPEG twin for the printed manual. Chrome copies a JPEG into a PDF as it is, but
    # expands a WebP to an uncompressed image - about five times the size - so the PDF
    # uses these while the screens keep the lighter WebP.
    jpeg = browser.send("Page.captureScreenshot", {
        "format": "jpeg", "quality": 86, "captureBeyondViewport": True,
        "clip": {"x": 0, "y": 0, "width": width, "height": height, "scale": 1}})
    (OUT_DIR / f"{shot['key']}.jpg").write_bytes(base64.b64decode(jpeg["data"]))

    placed = []
    for number, (marker, point) in enumerate(zip(markers, measured["points"]), start=1):
        if point is None:
            print(f"    ! marker {number} not found: {marker['selector']}")
            continue
        placed.append({
            "number": number,
            "label": marker["label"],
            "x": round(max(2.0, min(98.0, point["x"] / width * 100)), 2),
            "y": round(max(2.0, min(98.0, point["y"] / height * 100)), 2),
        })

    return {
        "key": shot["key"],
        "file": out.name,
        "width": int(width * device["scale"]),
        "height": int(height * device["scale"]),
        "frame": "phone" if shot.get("device") == "phone" else "browser",
        "address": shot.get("address", ""),
        "caption": shot.get("caption", ""),
        "version": int(time.time()),
        "markers": placed,
    }


def find_browser():
    for candidate in BROWSERS:
        if pathlib.Path(candidate).exists() or shutil.which(candidate):
            return candidate
    raise SystemExit("Chrome or Edge was not found.")


def main():
    parser = argparse.ArgumentParser(description="Capture help screenshots from a demo copy.")
    parser.add_argument("--site", default="http://localhost:5199")
    parser.add_argument("--only", default="", help="comma-separated keys to capture")
    parser.add_argument("--staff-user", default="admin")
    parser.add_argument("--staff-password", default="Admin@123")
    parser.add_argument("--tenant-user", default="tenant@example.com")
    parser.add_argument("--tenant-password", default="Tenant123!")
    parser.add_argument("--allow-remote", action="store_true",
                        help="permit a non-localhost site - for a DEMO site only")
    args = parser.parse_args()

    site = args.site.rstrip("/")
    host = urllib.parse.urlparse(site).hostname or ""
    if host not in ("localhost", "127.0.0.1") and not args.allow_remote:
        raise SystemExit(
            f"Refusing to capture from {host}. Screenshots become public files; capture from a "
            "local demo copy so no real tenant data is published. Use --allow-remote only for a demo site.")

    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    wanted = {k.strip() for k in args.only.split(",") if k.strip()}
    shots = [s for s in config["shots"] if not wanted or s["key"] in wanted]

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    manifest = {}
    if MANIFEST.exists():
        manifest = {m["key"]: m for m in json.loads(MANIFEST.read_text(encoding="utf-8"))}

    credentials = {"staff": (args.staff_user, args.staff_password),
                   "tenant": (args.tenant_user, args.tenant_password)}

    port = 9333
    failures = []
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as profile:
        process = subprocess.Popen(
            [find_browser(), "--headless=new", "--disable-gpu", "--no-sandbox", "--hide-scrollbars",
             "--force-color-profile=srgb", f"--remote-debugging-port={port}",
             f"--user-data-dir={profile}", "about:blank"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            for _ in range(50):
                try:
                    urllib.request.urlopen(f"http://127.0.0.1:{port}/json/version", timeout=1)
                    break
                except OSError:
                    time.sleep(0.2)

            browser = Browser(port)
            signed_in_as = None
            failures = []

            # Staff first, then tenants: each sign-in starts from a clean cookie jar.
            for shot in sorted(shots, key=lambda s: s.get("who", "staff") == "tenant"):
                who = shot.get("who", "staff")
                if who != signed_in_as:
                    sign_in(browser, site, who, credentials)
                    signed_in_as = who
                print(f"  {shot['key']}")
                try:
                    manifest[shot["key"]] = capture(browser, site, shot)
                except Exception as error:  # report and carry on with the other screens
                    failures.append(shot["key"])
                    print(f"    ! skipped: {error}")

            browser.close()
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()

    ordered = [manifest[s["key"]] for s in config["shots"] if s["key"] in manifest]
    MANIFEST.write_text(json.dumps(ordered, indent=2, ensure_ascii=False), encoding="utf-8")
    total = sum((OUT_DIR / m["file"]).stat().st_size + (OUT_DIR / m["file"]).with_suffix(".jpg").stat().st_size
                for m in ordered) // 1024
    print(f"Captured {len(shots) - len(failures)} of {len(shots)} screen(s). "
          f"{len(ordered)} in the manifest, {total} KB of images.")
    if failures:
        print("Skipped: " + ", ".join(failures))


if __name__ == "__main__":
    sys.exit(main())
