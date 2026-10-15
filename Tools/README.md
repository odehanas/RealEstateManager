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
