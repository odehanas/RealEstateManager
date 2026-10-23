"""Regenerate the downloadable PDFs of the user manual and the training workbook.

The manual is built live from the help topics in wwwroot/help, and is always current
inside the application at /Help/Manual; the training workbook is /Help/Training. The
PDFs offered for download are snapshots of those pages, so run this after changing
any help topic or the workbook.

USAGE
    python Tools/export_manual_pdf.py                         # from the live site
    python Tools/export_manual_pdf.py http://localhost:5199   # from a local copy

It prints both pages with headless Chrome (or Edge) and writes the results to
wwwroot/manual/RealEstateManager-User-Manual.pdf and
wwwroot/manual/RealEstateManager-Training-Workbook.pdf. Commit and publish them as usual.

No sign-in is needed: both pages are deliberately public, because they are product
documentation and carry no company data.
"""
import pathlib
import shutil
import subprocess
import sys
import tempfile

DEFAULT_SITE = "https://realestatemanager.odehanas.com"
PROJECT = pathlib.Path(__file__).resolve().parent.parent
DOCUMENTS = [
    ("/Help/Manual", PROJECT / "wwwroot" / "manual" / "RealEstateManager-User-Manual.pdf"),
    ("/Help/Training", PROJECT / "wwwroot" / "manual" / "RealEstateManager-Training-Workbook.pdf"),
]

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


def export(browser, url, output):
    # A throwaway profile, so the run never touches the user's own browser settings.
    with tempfile.TemporaryDirectory() as profile:
        result = subprocess.run(
            [browser, "--headless=new", "--disable-gpu", "--no-sandbox",
             f"--user-data-dir={profile}",
             f"--print-to-pdf={output}", "--no-pdf-header-footer",
             "--virtual-time-budget=10000", url],
            capture_output=True, text=True, timeout=180)

    if not output.exists() or output.stat().st_size < 50_000:
        print(result.stderr[-2000:])
        raise SystemExit(f"The PDF was not produced from {url}. Is the site reachable?")

    print(f"Written {output} ({output.stat().st_size // 1024} KB) from {url}")


def main():
    site = (sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SITE).rstrip("/")
    browser = find_browser()
    for path, output in DOCUMENTS:
        export(browser, f"{site}{path}", output)


if __name__ == "__main__":
    main()
