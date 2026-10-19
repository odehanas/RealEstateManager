# Tools

Developer utilities. None of this is part of the application: the project file excludes the
whole folder from the build and from publishing.

## Help content: what needs a deployment and what does not

The help is **content, not code**. It lives in `wwwroot/help`:

| File | What it is |
|---|---|
| `catalog.json` | Every topic, its group, and which screens open it |
| `topics/<key>.html` | One topic's text |
| `images/<key>.webp` / `.jpg` | A screenshot (WebP for screens, JPEG twin for the PDF) |
| `images/shots.json` | Each screenshot's size, caption and numbered call-outs |

The application re-reads any of these the moment the file changes. **Editing a topic or replacing
a screenshot needs no rebuild and no restart** — the file only has to reach the server, by your
normal push/publish or by uploading just that file. Only changes to `.cs` or `.cshtml` files need
a full deployment.

After changing help text or pictures, refresh the downloadable PDF:

```
python Tools/export_manual_pdf.py
```

## Putting a screenshot in a topic

One tag, wherever the picture should appear:

```html
<figure data-shot="vendor-bill-edit"></figure>
```

Two phone screenshots side by side:

```html
<div class="shot-pair">
    <figure data-shot="portal-account"></figure>
    <figure data-shot="portal-invoices"></figure>
</div>
```

The frame, caption, numbered call-outs and legend are added automatically from `shots.json`.

## Re-capturing screenshots after a screen changes

**Always from the local demo company — never from the live site.** Help images are public files,
and the manual page is open without signing in; a picture of the live system would publish real
tenants' names and balances. The capture tool refuses any address other than localhost for this
reason.

1. Build the demo company (fictional data, local database `RemDemo`):
   ```
   dotnet run --project Tools/DemoData
   ```
2. Start the application against it, and leave it running:
   ```
   set ConnectionStrings__DefaultConnection=Server=(localdb)\ClaudeREM;Database=RemDemo;Trusted_Connection=True;TrustServerCertificate=True
   set ASPNETCORE_URLS=http://localhost:5199
   dotnet run --no-launch-profile
   ```
3. Create its transactions through the application's own screens:
   ```
   python Tools/DemoData/demo_transactions.py
   ```
4. Capture — every screen, or only the ones that changed:
   ```
   python Tools/capture_screenshots.py
   python Tools/capture_screenshots.py --only vendor-bill-edit,pdc-receipts
   ```
5. Refresh the PDF from the same local copy:
   ```
   python Tools/export_manual_pdf.py http://localhost:5199
   ```

`Tools/screenshots.json` lists every screen: its address, caption, size, and the controls that
get a numbered call-out. To add a screen, add an entry there, capture it, and place its tag in a
topic.

Requirements: SQL Server LocalDB (installed with Visual Studio), Chrome or Edge, and
`pip install websocket-client`.

## Recording the training videos

`Tools/VideoCapture/record_videos.py` records ten short walkthroughs of the main tasks:
signing in, master data, leases, invoices and receipts, post-dated cheques, supplier
bills, petty cash, month-end, the reports and the tenant portal. A real browser does the
clicking and typing, with a caption bar explaining each step and a pointer showing where
to click. **Like the screenshots, always record from the local demo copy, never the live
site.** The tool refuses any address other than localhost.

1. Build a fresh demo database and start the application against it (steps 1 and 2 of
   *Re-capturing screenshots* above). The videos add records, so each recording needs a
   fresh database.
2. Record:
   ```
   python Tools/VideoCapture/record_videos.py
   python Tools/VideoCapture/record_videos.py --only 05,09     # just some
   python Tools/VideoCapture/record_videos.py --show           # watch it work
   python Tools/VideoCapture/record_videos.py --pace 1.3       # slower
   ```
   The first run opens a practice company, "Al Waha Properties", and loads a year of
   sample data into it. Every video works inside that company.

Each video is recorded into `Tools/VideoCapture/output/` and then **published into the
help**: `wwwroot/help/videos/<key>.webm`, a cover picture `<key>.jpg`, and `videos.json`
listing them. A help topic shows a video with one tag, just like a screenshot:

```html
<figure data-video="lease"></figure>
```

Every video sits in the topic for its screen, and the **Training Videos** topic lists all
ten. In the printed manual and the PDF a short note stands in for each video. Like the
rest of the help, the videos go live with your normal publish; replacing a `.webm` file
on the server is enough to update one. The `.webm` files are kept out of git because of
their size - re-create them with this tool. `--publish-only` copies already-recorded
videos into the help again without recording.

To add a voice-over or make an `.mp4` copy, open a video in Microsoft Clipchamp and export it.

Requirements: Google Chrome or Microsoft Edge, `pip install playwright`, and
`python -m playwright install ffmpeg`.

## Checking e-invoice files against the official UAE rules

`Tools/EInvoicing` holds what an Accredited Service Provider runs on every e-invoice before
accepting it:

| Folder | What it is |
|---|---|
| `schematron/` | The official PINT AE validation rules (PINT AE Billing 1.0.4, from docs.peppol.eu) |
| `ubl-2.1/` | The OASIS UBL 2.1 XML schema |
| `SchemaCheck/` | A small program that checks files against that schema |
| `samples/` | Three official sample documents, for a sanity check |

Download e-invoice files from the E-Invoice Monitor (on the local demo copy) and run:

```
python Tools/EInvoicing/validate_einvoice.py path/to/files-or-folder
```

Every file must report PASS twice: once for the PINT AE rules, once for the UBL 2.1 schema.
When the Ministry of Finance publishes a new version of PINT AE, replace the two files in
`schematron/` with the new ones and run the check again.

Requirements: `pip install saxonche`, and the .NET 8 SDK.

`REM_DEMO_DB` makes the demo builder create a differently named database, which is useful
for comparing the schema a SQL script produces with the one the code expects:

```
set REM_DEMO_DB=RemCompare
dotnet run --project Tools/DemoData
```
