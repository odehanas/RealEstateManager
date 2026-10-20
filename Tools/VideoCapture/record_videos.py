"""Record the training videos: short walkthroughs of the system's main tasks.

Each video drives a real browser through the application's own screens - clicking,
typing and waiting as a person would - with a caption bar explaining every step, a
visible pointer, and a highlight around whatever is being clicked. Each video is then
published into the help - wwwroot/help/videos/<key>.webm with a cover picture, listed in
videos.json - and appears in every help topic that carries <figure data-video="key">.

Because the videos are scripted, they never go stale: after a screen changes, run this
again and every video is re-recorded in a few minutes.

ALWAYS RECORD FROM THE LOCAL DEMO COPY, NEVER THE LIVE SITE.
The videos show names, amounts and screens. Recorded from the live system they would
show real tenants and real balances. The tool refuses any address other than localhost.

BEFORE RECORDING (all local):
    1. dotnet run --project Tools/DemoData          a fresh demo database (RemDemo)
    2. start the application against it on http://localhost:5199 (see Tools/README.md)
    3. python Tools/VideoCapture/record_videos.py

The first run opens a training company, "Al Waha Properties", and fills it with a year of
sample data from the Load Sample Data button. Every video then works inside that company.
Some videos build on the one before (the lease in video 3 is for the tenant added in
video 2), so record them all together, on a fresh demo database.

USAGE
    python Tools/VideoCapture/record_videos.py                  # all videos
    python Tools/VideoCapture/record_videos.py --only 01,09     # some of them
    python Tools/VideoCapture/record_videos.py --show           # watch it work
    python Tools/VideoCapture/record_videos.py --publish-only   # re-publish existing recordings

Requires:  pip install playwright   and Google Chrome or Microsoft Edge installed.
           python -m playwright install ffmpeg   (Playwright's own video encoder)
"""
import argparse
import asyncio
import base64
import datetime as dt
import json
import pathlib
import re
import shutil
import sys
import tempfile
import urllib.parse

from playwright.async_api import async_playwright, Locator, Page

PROJECT = pathlib.Path(__file__).resolve().parent.parent.parent
# Raw recordings (and a screenshot of any video that failed) land here first...
OUT_DIR = PROJECT / "Tools" / "VideoCapture" / "output"
# ...then are published into the help, which shows them inside the topics.
HELP_VIDEOS = PROJECT / "wwwroot" / "help" / "videos"

COMPANY = "Al Waha Properties"
SIZE = {"width": 1280, "height": 720}

TODAY = dt.date.today()
MONTH_START = TODAY.replace(day=1)
NEXT_MONTH = (MONTH_START + dt.timedelta(days=32)).replace(day=1)
MONTH_END = NEXT_MONTH - dt.timedelta(days=1)
LAST_MONTH_END = MONTH_START - dt.timedelta(days=1)


def add_months(d: dt.date, months: int) -> dt.date:
    year, month = divmod(d.month - 1 + months, 12)
    return d.replace(year=d.year + year, month=month + 1)


def iso(d: dt.date) -> str:
    return d.isoformat()


# What the videos create, shared between them.
STATE = {
    "property": "Sunset Courtyard",
    "tenant": "Layla Hassan",
    "tenant_email": "layla.hassan@mail.example",
    "lease_start": MONTH_START,
    "property_code": None,
}

# ---------------------------------------------------------------------------
#  The on-screen layer: caption bar, pointer, highlight ring and title cards.
#  Added to every page as it loads, so it survives navigation; the caption and the
#  pointer position are kept in sessionStorage and redrawn on the next page.
# ---------------------------------------------------------------------------
OVERLAY_JS = r"""
(() => {
  if (window.__demo) return;
  const css = `
  #__cap{position:fixed;left:50%;bottom:20px;transform:translateX(-50%);max-width:88%;
    background:rgba(15,39,64,.95);color:#fff;font:600 19px/1.45 'Segoe UI',Arial,sans-serif;
    padding:12px 24px;border-radius:12px;box-shadow:0 8px 28px rgba(0,0,0,.35);
    z-index:2147483646;pointer-events:none;text-align:center}
  #__cap .s{display:inline-block;background:#f0b429;color:#0f2740;border-radius:999px;
    padding:1px 11px;margin-right:12px;font-size:15px;vertical-align:2px}
  #__cur{position:fixed;width:22px;height:22px;border-radius:50%;background:rgba(240,180,41,.5);
    border:2px solid #f0b429;z-index:2147483647;pointer-events:none;transform:translate(-50%,-50%);
    transition:left .6s ease,top .6s ease}
  #__cur.click{animation:__pulse .5s}
  @keyframes __pulse{0%{box-shadow:0 0 0 0 rgba(240,180,41,.85)}100%{box-shadow:0 0 0 24px rgba(240,180,41,0)}}
  #__ring{position:fixed;border:3px solid #f0b429;border-radius:8px;z-index:2147483645;
    pointer-events:none;transition:all .35s;display:none}
  #__card{position:fixed;inset:0;background:linear-gradient(135deg,#0f2740,#1d4e7a);color:#fff;
    z-index:2147483647;display:none;flex-direction:column;align-items:center;justify-content:center;
    font-family:'Segoe UI',Arial,sans-serif;text-align:center;padding:40px}
  #__card .n{font-size:17px;letter-spacing:.25em;opacity:.7;margin-bottom:18px;text-transform:uppercase}
  #__card h1{font-size:46px;margin:0 0 16px;font-weight:700}
  #__card p{font-size:22px;opacity:.88;margin:0;max-width:900px;line-height:1.5}`;
  const store = {
    get: k => { try { return sessionStorage.getItem(k); } catch (e) { return null; } },
    set: (k, v) => { try { v == null ? sessionStorage.removeItem(k) : sessionStorage.setItem(k, v); } catch (e) {} }
  };
  const ready = fn => document.body ? fn() : document.addEventListener('DOMContentLoaded', fn);
  const el = id => { let e = document.getElementById(id); if (!e) { e = document.createElement('div'); e.id = id; document.body.appendChild(e); } return e; };
  const esc = s => String(s).replace(/[&<>]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));
  function drawCaption() {
    const raw = store.get('__cap'); const e = el('__cap');
    if (!raw) { e.style.display = 'none'; return; }
    const c = JSON.parse(raw);
    e.style.display = 'block';
    e.innerHTML = (c.step ? `<span class="s">${esc(c.step)}</span>` : '') + esc(c.text);
  }
  window.__demo = {
    caption(text, step) { store.set('__cap', text == null ? null : JSON.stringify({ text, step })); ready(drawCaption); },
    cursor(x, y, click) {
      store.set('__cur', JSON.stringify({ x, y }));
      ready(() => { const c = el('__cur'); c.style.left = x + 'px'; c.style.top = y + 'px';
        if (click) { c.classList.remove('click'); void c.offsetWidth; c.classList.add('click'); } });
    },
    ring(r) {
      ready(() => { const e = el('__ring'); if (!r) { e.style.display = 'none'; return; }
        Object.assign(e.style, { display: 'block', left: (r.x - 6) + 'px', top: (r.y - 6) + 'px',
          width: (r.w + 12) + 'px', height: (r.h + 12) + 'px' }); });
    },
    card(n, title, sub) {
      ready(() => { const c = el('__card'); c.style.display = 'flex';
        c.innerHTML = `<div class="n">${esc(n || '')}</div><h1>${esc(title)}</h1><p>${esc(sub || '')}</p>`; });
    },
    hideCard() { const c = document.getElementById('__card'); if (c) c.style.display = 'none'; }
  };
  ready(() => {
    const st = document.createElement('style'); st.textContent = css; document.head.appendChild(st);
    drawCaption();
    const c = el('__cur'); const p = JSON.parse(store.get('__cur') || '{"x":640,"y":360}');
    c.style.left = p.x + 'px'; c.style.top = p.y + 'px';
  });
})();
"""


class Director:
    """Plays one video: moves the pointer, shows captions and does the clicking."""

    def __init__(self, page: Page, site: str, pace: float):
        self.page = page
        self.site = site
        self.pace = pace

    async def js(self, script: str, arg=None):
        """Runs a snippet in the page. A page that is reloading (after a form submits)
        has no script context for a moment, so wait for it and try once more."""
        for attempt in range(2):
            try:
                return await self.page.evaluate(script, arg)
            except Exception as ex:
                if attempt == 1 or "context was destroyed" not in str(ex):
                    raise
                await self.page.wait_for_load_state("load")

    # ---- timing ------------------------------------------------------------
    async def pause(self, seconds: float):
        await self.page.wait_for_timeout(int(seconds * 1000 * self.pace))

    def reading_time(self, text: str) -> float:
        # About three words a second, never less than two seconds on screen.
        return max(2.2, len(text.split()) / 3.0)

    # ---- the layers ----------------------------------------------------------
    async def card(self, number: str, title: str, subtitle: str, seconds: float = 3.8):
        await self.js("([n,t,s]) => __demo.card(n,t,s)", [number, title, subtitle])
        await self.pause(seconds)
        await self.js("() => __demo.hideCard()")

    async def say(self, text: str, step: str | None = None, seconds: float | None = None):
        await self.js("([t,s]) => __demo.caption(t,s)", [text, step])
        await self.pause(seconds if seconds is not None else self.reading_time(text))

    async def clear_caption(self):
        await self.js("() => __demo.caption(null)")

    # ---- moving around ---------------------------------------------------------
    async def goto(self, path: str, text: str | None = None, step: str | None = None):
        await self.page.goto(self.site + path, wait_until="load")
        await self.ring(None)
        if text:
            await self.say(text, step)

    def loc(self, target) -> Locator:
        return target if isinstance(target, Locator) else self.page.locator(target).first

    async def ring(self, box):
        await self.js("r => __demo.ring(r)", box)

    async def point(self, target, text: str | None = None, step: str | None = None, seconds: float | None = None):
        """Moves the pointer to something and outlines it, without clicking."""
        locator = self.loc(target)
        await locator.scroll_into_view_if_needed()
        box = await locator.bounding_box()
        if box:
            await self.js("([x,y]) => __demo.cursor(x,y,false)",
                                     [box["x"] + box["width"] / 2, box["y"] + box["height"] / 2])
            await self.ring({"x": box["x"], "y": box["y"], "w": box["width"], "h": box["height"]})
        await self.pause(0.7)
        if text:
            await self.say(text, step, seconds)

    async def click(self, target, text: str | None = None, step: str | None = None, navigates: bool = True):
        locator = self.loc(target)
        await self.point(locator, text, step)
        box = await locator.bounding_box()
        if box:
            await self.js("([x,y]) => __demo.cursor(x,y,true)",
                                     [box["x"] + box["width"] / 2, box["y"] + box["height"] / 2])
        await self.pause(0.35)
        await self.ring(None)
        await locator.click()
        if navigates:
            await self.page.wait_for_timeout(300)
            await self.page.wait_for_load_state("load")
        await self.pause(0.8)

    async def fill(self, target, value, text: str | None = None, step: str | None = None):
        """Types into a box, or picks from a list: a select is chosen by value or by
        the start of any option's text."""
        locator = self.loc(target)
        await self.point(locator, text, step)
        tag = await locator.evaluate("e => e.tagName.toLowerCase()")
        kind = await locator.evaluate("e => (e.type || '').toLowerCase()")
        value = str(value)

        if tag == "select":
            option = await locator.evaluate(
                """(e, v) => { const o = [...e.options].find(o => o.value === v)
                              || [...e.options].find(o => o.text.trim().toLowerCase().includes(v.toLowerCase()));
                              return o ? o.value : null; }""", value)
            if option is None:
                raise RuntimeError(f"No option matching '{value}' in {target}")
            submits = await locator.evaluate("e => (e.getAttribute('onchange') || '').includes('submit')")
            if submits:
                async with self.page.expect_navigation():
                    await locator.select_option(option)
                await self.page.wait_for_load_state("load")
            else:
                await locator.select_option(option)
        elif kind in ("date", "checkbox", "radio"):
            if kind == "date":
                await locator.fill(value)
            else:
                await locator.set_checked(value.lower() in ("1", "true", "yes", "on"))
        else:
            await locator.click()
            await locator.fill("")
            await locator.type(value, delay=int(38 * self.pace))
        await self.ring(None)
        await self.pause(0.45)

    async def submit(self, action: str, text: str | None = None, step: str | None = None):
        """Clicks the submit button of the form posting to the given address."""
        await self.click(f"form[action*='{action}'] button[type=submit]", text, step)

    async def menu(self, dropdown_id: str, href: str, text: str | None = None, step: str | None = None):
        """Opens a menu at the top, then the item in it."""
        await self.click(f"#{dropdown_id}", navigates=False)
        await self.click(f".dropdown-menu.show a[href='{href}']", text, step)

    async def scroll(self, pixels: int, seconds: float = 1.2):
        await self.page.mouse.wheel(0, pixels)
        await self.pause(seconds)

    async def option_matching(self, select: str, *parts: str) -> str:
        value = await self.page.locator(select).first.evaluate(
            "(e, ps) => { const o = [...e.options].find(o => ps.every(p => o.text.includes(p))); return o ? o.value : null; }",
            list(parts))
        if value is None:
            raise RuntimeError(f"No option containing {parts} in {select}")
        return value

    async def text(self, selector: str) -> str:
        return (await self.page.locator(selector).first.inner_text()).strip()

    async def option_value(self, select: str, contains: str) -> str:
        value = await self.page.locator(select).first.evaluate(
            """(e, v) => { const o = [...e.options].find(o => o.text.toLowerCase().includes(v.toLowerCase()));
                          return o ? o.value : null; }""", contains)
        if value is None:
            raise RuntimeError(f"No option containing '{contains}' in {select}")
        return value


# ===========================================================================
#  THE VIDEOS
# ===========================================================================

async def v01_sign_in(d: Director):
    await d.goto("/Auth/Login")
    await d.card("Video 1", "Signing in and finding your way",
                 "Sign in, switch to the training company and read the dashboard.")
    await d.say("This is the sign-in page. Every user has their own username and password.")
    await d.fill("#Username", ARGS.username, "Type your username...", "Step 1")
    await d.fill("#Password", ARGS.password, "...and your password. It is hidden as you type.", "Step 1")
    await d.submit("/Auth/Login", "Click Login.", "Step 2")
    await d.say("You arrive on the dashboard of your default company.")
    await d.point("select[name=companyId]",
                  "If you work for more than one company, choose the one to work in from this list.", "Step 3")
    await d.fill("select[name=companyId]", COMPANY)
    await d.pause(1)
    await d.say(f"Now you are in {COMPANY}, a practice company full of sample data.")
    await d.point(d.page.locator(".card", has_text="Receivables").first,
                  "Receivables: how much tenants owe, and how much of it is overdue.")
    await d.point(d.page.locator(".card", has_text="Occupancy").first,
                  "Occupancy: how many of your units are let.")
    await d.point(d.page.locator(".card", has_text="Accounting Period").first,
                  "The current accounting period, and how many leases are active.")
    await d.scroll(600, 2.5)
    await d.scroll(-600, 1)
    await d.click("#masterDataDropdown", "Master Data holds properties, units, tenants and vendors.", "Menus", navigates=False)
    await d.pause(1.5)
    await d.page.keyboard.press("Escape")
    await d.click("#operationsDropdown", "Operations is the daily work: leases, invoices, receipts, cheques, bills and petty cash.", "Menus", navigates=False)
    await d.pause(1.5)
    await d.page.keyboard.press("Escape")
    await d.click("#reportingDropdown", "Reporting has the financial statements and the property reports.", "Menus", navigates=False)
    await d.pause(1.5)
    await d.page.keyboard.press("Escape")
    await d.click("#setupDropdown", "And this menu holds accounting set-up, month-end, subscription and help.", "Menus", navigates=False)
    await d.pause(1.5)
    await d.page.keyboard.press("Escape")
    await d.clear_caption()
    await d.card("Done", "You can now find your way around", "Next: adding a property, a unit and a tenant.", 3)


async def v02_master_data(d: Director):
    await d.goto("/")
    await d.card("Video 2", "Adding a property, a unit and a tenant",
                 "Master data is set up once, then used by leases, invoices and reports.")
    await d.menu("masterDataDropdown", "/Properties", "Open Master Data, then Properties.", "Step 1")
    await d.say("This is the list of buildings. Owned buildings earn rent; managed ones earn a commission for the landlord.")
    await d.click("a[href='/Properties/Create']", "Click Create to add a building.", "Step 2")
    await d.fill("#Name", STATE["property"], "Type the building's name.", "Step 2")
    await d.fill("#ownershipType", "Owned", "Owned: the company owns this building.", "Step 2")
    await d.fill("#propertyType", "Residential", "Residential. The system now proposes the property code.", "Step 2")
    await d.point("#codePreview", "The code is made from the type and ownership - you never type it.")
    await d.fill("#VatCategory", "Exempt", "Residential rent is VAT-exempt in the UAE.", "Step 2")
    await d.fill("#Address", "Al Dhait South, Ras Al Khaimah")
    await d.fill("#AssetValue", "9500000", "The building's value, for the balance sheet.", "Step 2")
    await d.submit("/Properties/Create", "Click Save.", "Step 3")
    row = d.page.locator("tr", has_text=STATE["property"]).first
    STATE["property_code"] = (await row.locator("td").first.inner_text()).strip()
    await d.point(row, f"The building is added with the code {STATE['property_code']}.")

    await d.menu("masterDataDropdown", "/Units", "Next, the flats in it. Open Master Data, then Units.", "Step 4")
    await d.click("a[href='/Units/Create']", "Click Create.", "Step 4")
    await d.fill("#PropertyId", STATE["property"], "Choose the building.", "Step 4")
    await d.fill("#Floor", "2", "Floor 2.")
    await d.fill("#UsageType", "Residential", "Residential use.")
    await d.fill("#AreaSqFt", "1180", "The area in square feet...")
    await d.fill("#StandardAnnualRent", "72000", "...and the standard yearly rent, used as the default on new leases.")
    await d.fill("#Status", "Vacant", "It is vacant for now.")
    await d.submit("/Units/Create", "Click Save. The unit number is created for you.", "Step 5")

    await d.menu("masterDataDropdown", "/Tenants", "Now the tenant. Open Master Data, then Tenants.", "Step 6")
    await d.click("a[href='/Tenants/Create']", "Click Create.", "Step 6")
    await d.fill("#FullName", STATE["tenant"], "The tenant's full name.", "Step 6")
    await d.fill("#Email", STATE["tenant_email"], "Their email and phone number.")
    await d.fill("#Phone", "+971 50 000 2244")
    await d.fill("#Nationality", "Egypt")
    await d.fill("#IdNumber", "784-1992-0002244-1", "The Emirates ID number, for the contract and for e-invoicing.")
    await d.submit("/Tenants/Create", "Click Save.", "Step 7")
    await d.point(d.page.locator("tr", has_text=STATE["tenant"]).first,
                  "The tenant is added and gets their own tenant code.")
    await d.clear_caption()
    await d.card("Done", "The building, unit and tenant are ready", "Next: the lease contract.", 3)


async def unit_label() -> str:
    code = STATE["property_code"]
    if not code:
        page = DIRECTOR.page
        await page.goto(ARGS.site + "/Properties")
        code = (await page.locator("tr", has_text=STATE["property"]).first.locator("td").first.inner_text()).strip()
        STATE["property_code"] = code
    return code


async def v03_lease(d: Director):
    code = await unit_label()
    await d.goto("/")
    await d.card("Video 3", "Creating a lease",
                 "One contract creates all of its rent invoices and the monthly income schedule.")
    await d.menu("masterDataDropdown", "/Leases", "Open Master Data, then Leases.", "Step 1")
    await d.click("a[href='/Leases/Create']", "Click Create.", "Step 1")
    await d.fill("#UnitId", code, "Choose the unit...", "Step 2")
    await d.fill("#TenantId", STATE["tenant"], "...and the tenant.", "Step 2")
    start = STATE["lease_start"]
    end = add_months(start, 12) - dt.timedelta(days=1)
    await d.fill("#StartDate", iso(start), "The contract runs for one year.", "Step 3")
    await d.fill("#EndDate", iso(end))
    await d.fill("#AnnualRent", "72000", "The yearly rent.", "Step 3")
    await d.fill("#BillingFrequencyMonths", "3", "The tenant pays every quarter, so four invoices a year.", "Step 3")
    await d.point("#vatCategoryOverride", "VAT is taken from the unit automatically - leave this as it is.")
    await d.fill("#SecurityDepositAmount", "3600", "The security deposit agreed in the contract.", "Step 3")
    await d.click("button[name=nextStep][value=activate]",
                  "Click Save and Activate. (With approval switched on, it would go to a second person first.)", "Step 4")
    await d.point(d.page.locator("tr", has_text=STATE["tenant"]).first,
                  "The lease is active and gets its own contract number.")
    await d.menu("operationsDropdown", "/CustomerInvoices", "Look at Customer Invoices.", "Step 5")
    rows = d.page.locator("tr", has_text=STATE["tenant"])
    await d.point(rows.first,
                  "The system created all four quarterly invoices for the year. They wait as drafts until each one is due.")
    await d.clear_caption()
    await d.card("Done", "The lease and its invoices are in place", "Next: posting an invoice and recording payment.", 3)


async def v04_invoice_receipt(d: Director):
    await d.goto("/")
    await d.card("Video 4", "Posting an invoice and receiving the rent",
                 "A draft invoice is not yet in the accounts. Posting it makes it a receivable.")
    await d.menu("operationsDropdown", "/CustomerInvoices", "Open Operations, then Customer Invoices.", "Step 1")
    row = (d.page.locator("tr", has_text=STATE["tenant"])
           .filter(has_text=STATE["lease_start"].strftime("%d/%m/%Y"))
           .filter(has=d.page.locator("button", has_text="Post")).first)
    await d.point(row, "This quarter's invoice is due today. It is still a draft.", "Step 1")
    await d.click(row.locator("button", has_text="Post"), "Click Post to put it in the accounts.", "Step 2")
    await d.say("Now it is posted: the tenant owes it, and it appears in Accounts Receivable.")

    await d.menu("operationsDropdown", "/CustomerReceipts", "The tenant pays. Open Customer Receipts.", "Step 3")
    await d.click("a[href='/CustomerReceipts/Create']", "Click Create.", "Step 3")
    await d.fill("#CustomerInvoiceId", STATE["tenant"], "Choose the invoice being paid. The list shows only what is still owed.", "Step 4")
    await d.fill("#ReceiptDate", iso(TODAY), "The date the money arrived.")
    await d.fill("#Amount", "18000", "The amount received: the full quarter.", "Step 4")
    await d.fill("#PaymentMethod", "Transfer", "Paid by bank transfer.")
    await d.point("#ReceiptNumber", "Leave the receipt number empty and the system numbers it for you.")
    await d.submit("/CustomerReceipts/Create", "Click Save and Post.", "Step 5")
    await d.point(d.page.locator("tr", has_text=STATE["tenant"]).first, "The receipt is recorded and posted.")

    await d.menu("reportingDropdown", "/TenantStatements", "Check the tenant's statement.", "Step 6")
    await d.fill("select[name=tenantId]", STATE["tenant"], "Choose the tenant...", "Step 6")
    await d.fill("input[name=fromDate]", iso(STATE["lease_start"]), "...and the period.")
    await d.click("form button[type=submit]:has-text('View Statement')", "Click View Statement.", "Step 6")
    await d.scroll(450, 1)
    await d.say("The invoice and the payment are both there, and the balance is nil.", seconds=4)
    await d.clear_caption()
    await d.card("Done", "Invoice posted and paid", "Next: post-dated cheques.", 3)


async def v05_cheques(d: Director):
    await d.goto("/")
    await d.card("Video 5", "Post-dated cheques",
                 "Record cheques when you receive them, and clear them when the bank pays.")
    await d.menu("operationsDropdown", "/PdcReceipts", "Open Operations, then PDC Receipts.", "Step 1")
    await d.say("This register lists every cheque in hand and every cheque cleared.")
    await d.click("a[href='/PdcReceipts/Create']", "Layla hands over a cheque for next quarter. Click Create.", "Step 2")

    lease_value = await d.option_value("#LeaseId", STATE["tenant"])
    await d.page.goto(f"{ARGS.site}/PdcReceipts/Create?leaseId={lease_value}")
    await d.point("#LeaseId", "Choose her lease. The invoices of that lease appear below.", "Step 2")
    next_quarter = add_months(STATE["lease_start"], 3)
    invoice = await d.option_matching("#CustomerInvoiceId", STATE["tenant"], f"-{next_quarter:%Y%m}")
    await d.fill("#CustomerInvoiceId", invoice, "Link it to next quarter's invoice.", "Step 2")
    await d.fill("#SettlementMode", "Receivable", "Receivable: the cheque will settle that invoice.")
    await d.fill("#ChequeNumber", "500321", "The cheque number and bank.", "Step 3")
    await d.fill("#BankName", "Emirates Islamic")
    await d.fill("#ChequeDate", iso(next_quarter), "The date written on the cheque - in the future.", "Step 3")
    await d.fill("#ReceivedDate", iso(TODAY), "The date you received it - today.", "Step 3")
    await d.fill("#Amount", "18000")
    await d.submit("/PdcReceipts/Create", "Click Save and Receive.", "Step 4")
    await d.point(d.page.locator("tr", has_text="500321").first,
                  "It is held as a noted cheque. It is not money yet, so the tenant's balance does not change.")

    await d.click("a[href='/PdcReceipts/Create']", "Khalid gives a cheque dated today for this month's rent.", "Step 5")
    lease_value = await d.option_value("#LeaseId", "Khalid")
    await d.page.goto(f"{ARGS.site}/PdcReceipts/Create?leaseId={lease_value}")
    invoice = await d.option_matching("#CustomerInvoiceId", "Khalid", f"-{TODAY:%Y%m}")
    await d.fill("#CustomerInvoiceId", invoice, "Link it to this month's invoice.", "Step 5")
    await d.fill("#SettlementMode", "Receivable")
    await d.fill("#ChequeNumber", "500322")
    await d.fill("#BankName", "RAKBANK")
    await d.fill("#ChequeDate", iso(TODAY))
    await d.fill("#ReceivedDate", iso(TODAY))
    await d.fill("#Amount", "8000")
    await d.submit("/PdcReceipts/Create", "Save and Receive.", "Step 5")

    row = d.page.locator("tr", has_text="500322").first
    await d.click(row.locator("a", has_text="Clear"), "The bank has paid it. Click Clear.", "Step 6")
    await d.fill("#ClearedDate", iso(TODAY), "The date the money reached the bank.", "Step 6")
    await d.submit("/PdcReceipts/Clear", "Click Clear.", "Step 6")
    await d.point(d.page.locator("tr", has_text="500322").first,
                  "Cleared: the money is in the bank and the invoice is paid.")
    await d.clear_caption()
    await d.card("Done", "Cheques received and cleared", "Next: supplier bills and payments.", 3)


async def v06_supplier(d: Director):
    await d.goto("/")
    await d.card("Video 6", "Entering a supplier bill and paying it",
                 "Bills are entered line by line, posted, and paid later.")
    await d.menu("operationsDropdown", "/VendorBills", "Open Operations, then Vendor Bills.", "Step 1")
    await d.click("a[href='/VendorBills/Create']", "Click Create.", "Step 1")
    await d.fill("#vendorSelect", "CoolTech", "Choose the supplier. The system knows whether it can charge VAT.", "Step 2")
    await d.fill("#VendorReference", f"CT-{TODAY:%m%d}-77", "Their invoice number.")
    await d.fill("#BillDate", iso(TODAY), "The bill date...", "Step 2")
    await d.fill("#AccountingDate", iso(TODAY))
    await d.fill("#DueDate", iso(TODAY + dt.timedelta(days=30)), "...and when it is due.")
    await d.fill("#defaultProperty", "Al Naeem", "The building the work was for.", "Step 2")
    await d.fill("#ExpenseCategory", "Repairs")
    if await d.page.locator("[name='Lines[0].Description']").count() == 0:
        await d.click("#addLine", "Add a line.", navigates=False)
    await d.fill("[name='Lines[0].Description']", "Replace thermostat - office floor 2", "Describe the work...", "Step 3")
    await d.fill("[name='Lines[0].AccountId']", "52100", "...choose the expense account...")
    await d.fill("[name='Lines[0].Quantity']", "1")
    await d.fill("[name='Lines[0].UnitPrice']", "1450", "...and the price before VAT.", "Step 3")
    await d.fill("input[type=checkbox][name='Lines[0].IsTaxable']", "true",
                 "Tick VAT: this supplier is VAT registered and charges 5%.", "Step 3")
    await d.point("#sumTotal", "VAT and the total are worked out for you.")
    await d.submit("/VendorBills/", "Click Save Draft.", "Step 4")
    await d.click("form[action*='/VendorBills/Post'] button[type=submit]",
                  "Check it, then click Post to put it in the accounts.", "Step 4")

    await d.menu("operationsDropdown", "/VendorPayments", "Now pay it. Open Vendor Payments.", "Step 5")
    await d.click("a[href='/VendorPayments/Create']", "Click Create.", "Step 5")
    newest = await d.page.locator("#VendorBillId").evaluate(
        """e => { const o = [...e.options].filter(o => o.text.includes('CoolTech'))
                    .sort((a, b) => Number(b.value) - Number(a.value))[0]; return o ? [o.value, o.text] : null; }""")
    await d.fill("#VendorBillId", newest[0], "Choose the bill. Only posted bills with a balance appear.", "Step 6")
    amount = re.search(r"Outstanding:\s*([\d,.]+)", newest[1]).group(1).replace(",", "")
    await d.fill("#PaymentDate", iso(TODAY))
    await d.fill("#Amount", amount, "Pay the full amount.", "Step 6")
    await d.fill("#PaymentMethod", "Transfer")
    await d.submit("/VendorPayments/Create", "Click Save and Post.", "Step 7")
    await d.say("The supplier is paid and the bill is closed.")
    await d.clear_caption()
    await d.card("Done", "Bill entered, posted and paid", "Next: petty cash.", 3)


async def v07_petty_cash(d: Director):
    await d.goto("/")
    await d.card("Video 7", "Petty cash",
                 "Top up the cash box from the bank and record what is spent.")
    await d.menu("operationsDropdown", "/PettyCash", "Open Operations, then Petty Cash.", "Step 1")
    await d.say("The dashboard shows the cash in the box and what was spent.")
    await d.click(d.page.locator("a", has_text="Replenish Float").first, "Click Replenish Float to top it up.", "Step 2")
    await d.fill("#FundingAccountId", "11100", "Funded from the bank.", "Step 2")
    await d.fill("#ReplenishmentDate", iso(TODAY))
    await d.fill("#Amount", "500", "The amount drawn.", "Step 2")
    await d.fill("#ReferenceNumber", f"PCR-{TODAY:%m%d}")
    await d.submit("/PettyCash/Replenish", "Click Post Replenishment. The float goes up straight away.", "Step 2")
    await d.click(d.page.locator("a", has_text="New Expense Voucher").first, "Now record an expense. Click New Expense Voucher.", "Step 3")
    await d.fill("#VoucherDate", iso(TODAY))
    await d.fill("#PaidTo", "Al Noor Stationery", "Who was paid...", "Step 3")
    await d.fill("#Description", "Printer paper and toner", "...for what...")
    await d.fill("#ExpenseAccountId", "61100", "...and which expense it is.")
    await d.fill("#Amount", "185", "The amount from the receipt.", "Step 3")
    await d.fill("#ReferenceNumber", "AN-5521")
    await d.submit("/PettyCash/Create", "Click Post Voucher.", "Step 3")
    await d.say("Both are posted at once: the bank went down, the cash box went up by the top-up "
                "and down by the voucher, and the expense is recorded.", "Step 4")
    await d.scroll(400, 2)
    await d.clear_caption()
    await d.card("Done", "Petty cash topped up and spent", "Next: month-end.", 3)


async def v08_month_end(d: Director):
    await d.goto("/")
    await d.card("Video 8", "Month-end",
                 "Recognise the month's rent, run depreciation, post, and close the period.")
    await d.menu("setupDropdown", "/RevenueRecognition", "Open Revenue Recognition.", "Step 1")
    await d.say("Rent is billed in advance, but earned month by month. This screen moves each month's share into income.")
    await d.fill("input[name=asOfDate]", iso(MONTH_END), "Choose the last day of the month.", "Step 2")
    await d.click("form button[type=submit]:has-text('Refresh')", navigates=True)
    await d.click("form[action*='/RevenueRecognition/PostDue'] button[type=submit]",
                  "Click Post All Due Up To Selected Date.", "Step 2")

    await d.menu("operationsDropdown", "/FixedAssets", "Next, depreciation. Open Fixed Assets.", "Step 3")
    await d.fill("form[action*='RunMonthly'] input[name=postingDate]", iso(MONTH_END), "Choose the month end...", "Step 3")
    await d.submit("/FixedAssets/RunMonthly", "...and click Post Due Depreciation.", "Step 3")

    await d.goto(f"/Journal?fromDate={iso(MONTH_START)}&toDate={iso(MONTH_END)}&postedOnly=false",
                 "Both runs created draft journals. Review them here...", "Step 4")
    await d.click("form[action*='/Journal/PostAllDrafts'] button[type=submit]",
                  "...and click Post All Drafts.", "Step 4")

    await d.menu("setupDropdown", "/Closing", "Finally, close the period. Open Period Closing.", "Step 5")
    await d.say("Closed months are locked: nothing can be posted into them or reset out of them.")
    open_row = d.page.locator("tr", has=d.page.locator("button", has_text="Close Period")).first
    await d.click(open_row.locator("button", has_text="Close Period"),
                  "Close the oldest open month once its figures are final.", "Step 5")
    await d.say("That month is now closed and its reports are final.")
    await d.clear_caption()
    await d.card("Done", "The month is closed", "Next: the reports.", 3)


async def v09_reports(d: Director):
    await d.goto("/")
    await d.card("Video 9", "The reports",
                 "Every report reads the posted journals, so they always agree with each other.")
    await d.menu("reportingDropdown", "/Reports/TrialBalance", "Start with the Trial Balance.", "Report 1")
    await d.say("Every account with its balance. Total debits always equal total credits.")
    await d.scroll(500, 2)
    await d.menu("reportingDropdown", "/Reports/ProfitAndLoss", "Profit and Loss: income less expenses.", "Report 2")
    await d.scroll(400, 2)
    await d.menu("reportingDropdown", "/Reports/BalanceSheet", "The Balance Sheet: what the company owns and owes.", "Report 3")
    await d.scroll(500, 2)
    await d.menu("reportingDropdown", "/Reports/CashFlow", "The Cash Flow statement: where the cash came from and went.", "Report 4")
    await d.scroll(500, 2)
    await d.menu("reportingDropdown", "/Reports/VatReturn", "The VAT Return, box by box, ready for the tax portal.", "Report 5")
    await d.scroll(450, 2)
    await d.menu("reportingDropdown", "/TenantStatements", "A tenant's statement.", "Report 6")
    await d.fill("select[name=tenantId]", "Khalid", "Choose a tenant with arrears.")
    await d.fill("input[name=fromDate]", iso(TODAY.replace(month=1, day=1)))
    await d.click("form button[type=submit]:has-text('View Statement')", "View Statement.")
    await d.scroll(500, 2)
    await d.say("Every invoice, payment and cheque, with the running balance.")
    await d.menu("reportingDropdown", "/Reports/TenantHealth", "Tenant Health: who pays on time and who is behind.", "Report 7")
    await d.scroll(400, 2)
    await d.menu("reportingDropdown", "/Reports/OwnerTenancySchedule", "For managed buildings, the schedule you send the landlord.", "Report 8")
    await d.scroll(400, 2)
    await d.clear_caption()
    await d.card("Done", "That is the reporting", "Next: the tenant portal.", 3)


async def v10_portal(d: Director):
    await d.goto("/")
    await d.card("Video 10", "The tenant portal",
                 "Tenants see their invoices and raise requests online; staff answer them here.")
    await d.menu("masterDataDropdown", "/Tenants", "Open Master Data, then Tenants.", "Step 1")
    row = d.page.locator("tr", has_text=STATE["tenant"]).first
    await d.click(row.locator("a[href*='PortalAccess']"), "Click Portal Access for the tenant.", "Step 1")
    await d.fill("#NewUsername, input[name=newUsername]", STATE["tenant_email"], "Their sign-in name: usually their email.", "Step 2")
    await d.click("form[action*='IssuePortalAccess'] button[type=submit]", "Click to create the login.", "Step 2")
    password_box = d.page.locator("xpath=//div[normalize-space()='Temporary password']/following-sibling::div[1]").first
    temp = (await password_box.inner_text()).strip()
    await d.point(password_box, "Give the tenant their username and this temporary password. They must change it the first time.", "Step 2")

    await d.click("form[action*='/Auth/Logout'] button[type=submit]",
                  "To see the portal as the tenant does, sign out of the office system first.", "Step 3")
    await d.goto("/portal/login", "This is the tenant's own sign-in page. They sign in...", "Step 3")
    await d.fill("#Username", STATE["tenant_email"])
    await d.fill("#Password", temp)
    await d.submit("/portal/login", "Sign in.", "Step 3")
    if "/portal/password" in d.page.url:
        new_password = "Waha#" + TODAY.strftime("%Y") + "pass"
        if await d.page.locator("#CurrentPassword").count():
            await d.fill("#CurrentPassword", temp)
        await d.fill("#NewPassword", new_password, "...and choose their own password.", "Step 3")
        await d.fill("#ConfirmPassword", new_password)
        await d.submit("/portal/password", "Save new password.", "Step 3")
    await d.say("Their home page: what they owe, their contract and their requests.", seconds=3.5)
    await d.goto("/portal/requests/new", "They report a problem.", "Step 4")
    await d.fill("#requestType", "Maintenance")
    await d.fill("#Category", "Plumbing", "What kind of problem...", "Step 4")
    await d.fill("#Subject", "Bathroom tap leaking", "...a short title...")
    await d.fill("#Description", "The hot water tap in the main bathroom drips all the time.", "...and the details.")
    await d.fill("#Priority", "High")
    await d.submit("/portal/requests/new", "Send request.", "Step 4")
    await d.say("The tenant can follow the request and every reply from here.")
    await d.submit("/portal/logout", "The tenant signs out.")

    await d.goto("/Auth/Login", "Back in the office, staff sign in as usual...", "Step 5")
    await d.fill("#Username", ARGS.username)
    await d.fill("#Password", ARGS.password)
    await d.submit("/Auth/Login")
    await d.fill("select[name=companyId]", COMPANY, "...and open the company.", "Step 5")
    await d.menu("operationsDropdown", "/ServiceRequests", "The request is waiting in Tenant Requests.", "Step 5")
    row = d.page.locator("tr", has_text="Bathroom tap leaking").first
    await d.click(row.locator("a[href*='/ServiceRequests/Details']").first, "Open it.", "Step 5")
    await d.fill("textarea[name=body]", "Thank you. A plumber will visit tomorrow between 10 and 12.",
                 "Reply to the tenant...", "Step 6")
    await d.submit("/AddComment", "...and post the reply. The tenant sees it in the portal.", "Step 6")
    await d.fill("select[name=status]", "InProgress", "Then mark it In Progress.", "Step 7")
    await d.submit("/UpdateStatus", "Save update.", "Step 7")
    await d.clear_caption()
    await d.card("Done", "Request answered", "That completes the main tasks of the system.", 3.5)


VIDEOS = [
    ("01", "sign-in", "Signing in and finding your way", v01_sign_in),
    ("02", "master-data", "Adding a property, a unit and a tenant", v02_master_data),
    ("03", "lease", "Creating a lease", v03_lease),
    ("04", "invoice-and-receipt", "Posting an invoice and receiving the rent", v04_invoice_receipt),
    ("05", "cheques", "Post-dated cheques", v05_cheques),
    ("06", "supplier-bill", "Entering a supplier bill and paying it", v06_supplier),
    ("07", "petty-cash", "Petty cash", v07_petty_cash),
    ("08", "month-end", "Month-end", v08_month_end),
    ("09", "reports", "The reports", v09_reports),
    ("10", "tenant-portal", "The tenant portal", v10_portal),
]

ARGS = None
DIRECTOR = None


# ===========================================================================
#  SET-UP: a signed-in session inside the training company
# ===========================================================================

async def prepare(browser, state_file: pathlib.Path):
    """Signs in, makes sure the training company exists with sample data, switches to
    it, and saves the session so each video starts signed in."""
    context = await browser.new_context(viewport=SIZE)
    page = await context.new_page()
    site = ARGS.site

    await page.goto(site + "/Auth/Login")
    if "/Auth/Login" in page.url:
        await page.fill("#Username", ARGS.username)
        await page.fill("#Password", ARGS.password)
        await page.click("form[action*='/Auth/Login'] button[type=submit]")
        await page.wait_for_load_state("load")
    if "/Auth/Login" in page.url:
        sys.exit("Could not sign in. Check --username and --password (the demo owner account).")

    await page.goto(site + "/Companies")
    if "/Companies" not in page.url:
        sys.exit("The account is not the platform owner, so it cannot create the training company.")

    if await page.locator("tr", has_text=COMPANY).count() == 0:
        print(f"creating {COMPANY} and loading sample data (about a minute)...")
        await page.goto(site + "/Companies/Create")
        await page.fill("#Name", COMPANY)
        await page.fill("#BaseCurrency", "AED")
        await page.click("form[action*='/Companies/Create'] button[type=submit]")
        await page.wait_for_load_state("load")
        await page.goto(site + "/Companies")
        edit = await page.locator("tr", has_text=COMPANY).first.locator("a[href*='/Companies/Edit']").get_attribute("href")
        await page.goto(site + edit)
        page.on("dialog", lambda dialog: asyncio.ensure_future(dialog.accept()))
        await page.click("form[action*='LoadSampleData'] button[type=submit]", timeout=0)
        await page.wait_for_load_state("load", timeout=600_000)
        alerts = await page.locator(".alert").all_inner_texts()
        print("  " + " | ".join(a.strip() for a in alerts))

    # Switch into the training company.
    await page.goto(site + "/")
    switcher = page.locator("select[name=companyId]").first
    value = await switcher.evaluate(
        "(e, n) => { const o = [...e.options].find(o => o.text.trim() === n); return o ? o.value : null; }", COMPANY)
    if value is None:
        sys.exit(f"The owner account has no access to {COMPANY}.")
    if not await switcher.evaluate("(e, v) => e.value === v", value):
        async with page.expect_navigation():
            await switcher.select_option(value)
        await page.wait_for_load_state("load")
    current = await page.locator("select[name=companyId] option:checked").first.inner_text()
    if current.strip() != COMPANY:
        sys.exit(f"Could not switch to {COMPANY}; still in {current.strip()}.")

    # Video 2 adds this building; on a database where it already exists the videos
    # would show a duplicate, so start again from a fresh demo database instead.
    if "02" in ARGS.selected:
        await page.goto(site + "/Properties")
        if await page.locator("tr", has_text=STATE["property"]).count():
            sys.exit(f"'{STATE['property']}' already exists - these videos were recorded on this database before.\n"
                     "Rebuild the demo database (dotnet run --project Tools/DemoData), restart the app and run again.")

    await context.storage_state(path=str(state_file))
    await context.close()


async def record(browser, state_file, number, key, title, play):
    global DIRECTOR
    temp_dir = pathlib.Path(tempfile.mkdtemp(prefix="rem-video-"))
    signed_in = None if key == "sign-in" else str(state_file)
    context = await browser.new_context(
        viewport=SIZE, storage_state=signed_in,
        record_video_dir=str(temp_dir), record_video_size=SIZE)
    await context.add_init_script(OVERLAY_JS)
    page = await context.new_page()
    page.set_default_timeout(20_000)
    page.on("dialog", lambda dialog: asyncio.ensure_future(dialog.accept()))
    DIRECTOR = Director(page, ARGS.site, ARGS.pace)

    ok = True
    try:
        await play(DIRECTOR)
    except Exception as ex:  # keep going with the next video, but say what failed
        ok = False
        shot = OUT_DIR / f"{number}-{key}-FAILED.png"
        await page.screenshot(path=str(shot))
        print(f"  FAILED: {ex}\n  screenshot: {shot.relative_to(PROJECT)}  page: {page.url}")

    video = page.video
    await context.close()
    target = OUT_DIR / f"{number}-{key}.webm"
    if ok:
        shutil.move(await video.path(), target)
    shutil.rmtree(temp_dir, ignore_errors=True)
    return ok, target


# ===========================================================================
#  PUBLISHING into the help
# ===========================================================================

async def publish(browser, entries):
    """Copies recorded videos into wwwroot/help/videos, makes each one's cover picture
    (a still of its title card) and updates videos.json, which the help reads. A topic
    shows a video with <figure data-video="key"></figure>."""
    HELP_VIDEOS.mkdir(parents=True, exist_ok=True)
    manifest_file = HELP_VIDEOS / "videos.json"
    listed = {}
    if manifest_file.exists():
        listed = {v["key"]: v for v in json.loads(manifest_file.read_text(encoding="utf-8"))}

    page = await browser.new_page(viewport=SIZE)
    for entry in entries:
        source = OUT_DIR / entry["file"]
        if not source.exists():
            print(f"  {entry['file']} not found - record it first")
            continue

        video_file = f"{entry['key']}.webm"
        poster_file = f"{entry['key']}.jpg"
        shutil.copyfile(source, HELP_VIDEOS / video_file)

        # Play the file in the browser to read its length and take the cover picture.
        await page.set_content(
            "<body style='margin:0;background:#000'>"
            f"<video id=v muted style='width:{SIZE['width']}px;height:{SIZE['height']}px'></video></body>")
        await page.evaluate(
            "b64 => { document.getElementById('v').src = 'data:video/webm;base64,' + b64; }",
            base64.b64encode(source.read_bytes()).decode())
        seconds = await page.evaluate("""() => new Promise(resolve => {
            const v = document.getElementById('v');
            const done = () => isFinite(v.duration) ? resolve(v.duration)
                : (v.currentTime = 1e9, v.ontimeupdate = () => { v.ontimeupdate = null; resolve(v.duration); });
            v.readyState >= 1 ? done() : v.addEventListener('loadedmetadata', done); })""")
        await page.evaluate("t => new Promise(r => { const v = document.getElementById('v'); v.onseeked = () => r(); v.currentTime = t; })",
                            min(2.2, seconds / 2))
        await page.wait_for_timeout(200)
        await page.screenshot(path=str(HELP_VIDEOS / poster_file), type="jpeg", quality=82)

        listed[entry["key"]] = {
            "key": entry["key"], "number": entry["number"], "title": entry["title"],
            "file": video_file, "poster": poster_file, "seconds": int(round(seconds)),
            "version": int(source.stat().st_mtime)
        }
        print(f"  published {video_file} ({int(seconds) // 60}:{int(seconds) % 60:02d})")
    await page.close()

    manifest_file.write_text(json.dumps(sorted(listed.values(), key=lambda v: v["number"]), indent=2), encoding="utf-8")


async def launch(p):
    for channel in ("chrome", "msedge"):
        try:
            return await p.chromium.launch(channel=channel, headless=not ARGS.show)
        except Exception:
            continue
    sys.exit("Neither Google Chrome nor Microsoft Edge could be started.")


async def main():
    global ARGS
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--site", default="http://localhost:5199")
    parser.add_argument("--username", default="admin", help="the demo owner account")
    parser.add_argument("--password", default="Admin@123", help="the demo owner's password")
    parser.add_argument("--only", help="comma-separated video numbers or keys, e.g. 01,09")
    parser.add_argument("--pace", type=float, default=1.0, help="slower above 1, faster below 1")
    parser.add_argument("--show", action="store_true", help="show the browser while recording")
    parser.add_argument("--publish-only", action="store_true",
                        help="do not record; copy the videos already recorded into the help")
    ARGS = parser.parse_args()

    wanted = {x.strip() for x in ARGS.only.split(",")} if ARGS.only else None
    selected = [v for v in VIDEOS if not wanted or v[0] in wanted or v[1] in wanted]
    ARGS.selected = {v[0] for v in selected}
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    manifest_file = OUT_DIR / "videos.json"

    if ARGS.publish_only:
        recorded = [{"number": n, "key": k, "title": t, "file": f"{n}-{k}.webm"} for n, k, t, _ in selected]
        async with async_playwright() as p:
            browser = await launch(p)
            await publish(browser, recorded)
            await browser.close()
        print(f"done: published into {HELP_VIDEOS.relative_to(PROJECT)}")
        return

    host = urllib.parse.urlparse(ARGS.site).hostname
    if host not in ("localhost", "127.0.0.1", "::1"):
        sys.exit("Refusing to record from anything but localhost: the videos would show real data.")

    async with async_playwright() as p:
        browser = await launch(p)

        state_file = pathlib.Path(tempfile.gettempdir()) / "rem-video-session.json"
        await prepare(browser, state_file)

        manifest = []
        for number, key, title, play in selected:
            print(f"recording {number} {title}...")
            ok, path = await record(browser, state_file, number, key, title, play)
            if ok:
                print(f"  saved {path.relative_to(PROJECT)} ({path.stat().st_size // 1024} KB)")
                manifest.append({"number": number, "key": key, "title": title, "file": path.name})

        print("publishing into the help...")
        await publish(browser, manifest)
        await browser.close()

    existing = {}
    if manifest_file.exists():
        existing = {v["key"]: v for v in json.loads(manifest_file.read_text(encoding="utf-8"))}
    existing.update({v["key"]: v for v in manifest})
    manifest_file.write_text(json.dumps(sorted(existing.values(), key=lambda v: v["number"]), indent=2), encoding="utf-8")
    print(f"done: {len(manifest)} of {len(selected)} recorded and published into {HELP_VIDEOS.relative_to(PROJECT)}")


if __name__ == "__main__":
    asyncio.run(main())
