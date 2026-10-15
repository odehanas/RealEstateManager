"""Regenerate the downloadable PDF of the user manual.

The manual is built live from the help topics in wwwroot/help, and is always current
inside the application at /Help/Manual. The PDF offered for download is a snapshot of
that page, so run this after changing any help topic.

USAGE
    python Tools/export_manual_pdf.py                         # from the live site
    python Tools/export_manual_pdf.py http://localhost:5199   # from a local copy

It prints /Help/Manual with headless Chrome (or Edge) and writes the result to
wwwroot/manual/RealEstateManager-User-Manual.pdf. Commit and publish that file as usual.

No sign-in is needed: the manual page is deliberately public, because it is product
documentation and carries no company data.
"""
import pathlib
import shutil
import subprocess
import sys
import tempfile

DEFAULT_SITE = "https://realestatemanager.odehanas.com"
PROJECT = pathlib.Path(__file__).resolve().parent.parent
OUTPUT = PROJECT / "wwwroot" / "manual" / "RealEstateManager-User-Manual.pdf"

BROWSERS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    "google-chrome",
    "chromium",
]


def find_browser():
    for candidate in BROWSERS:
        if pathlib.Path(candidate).exists() or shutil.which(candidate):
            return candidate
    raise SystemExit("Chrome or Edge was not found. Install one, or open /Help/Manual and use "
                     "Print > Save as PDF instead.")


def main():
    site = (sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SITE).rstrip("/")
    url = f"{site}/Help/Manual"
    browser = find_browser()

    # A throwaway profile, so the run never touches the user's own browser settings.
    with tempfile.TemporaryDirectory() as profile:
        result = subprocess.run(
            [browser, "--headless=new", "--disable-gpu", "--no-sandbox",
             f"--user-data-dir={profile}",
             f"--print-to-pdf={OUTPUT}", "--no-pdf-header-footer",
             "--virtual-time-budget=10000", url],
            capture_output=True, text=True, timeout=180)

    if not OUTPUT.exists() or OUTPUT.stat().st_size < 50_000:
        print(result.stderr[-2000:])
        raise SystemExit(f"The PDF was not produced from {url}. Is the site reachable?")

    print(f"Written {OUTPUT} ({OUTPUT.stat().st_size // 1024} KB) from {url}")


if __name__ == "__main__":
    main()
