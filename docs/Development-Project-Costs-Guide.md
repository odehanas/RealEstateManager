# Development Project Costs, VAT and WIP — Finance Guide

How contractor, subcontractor and supplier costs reach a development project, how VAT is treated, and how the
finance team reports and reconciles project spend and work in progress (WIP).

## 1. How the pieces fit

```
Property (the plot, title deed, permits)
   └── Development project  DEV-0002  (intended use → WIP account)
          ├── Budget by cost category (land, design, permits, construction, infrastructure, supervision, finance, other)
          ├── Vendor bills linked to the project  ──► bill lines tagged project + category
          ├── Manual journals / petty cash lines tagged project + category
          └── Completion → transfer out of WIP to inventory, investment property or fixed assets
```

| Intended use | WIP account | Standard |
|---|---|---|
| For sale | 13100 Properties Under Development – For Sale | inventory (IAS 2) |
| For rental | 15500 Investment Property Under Construction | investment property (IAS 40) |
| Own use | 15600 Capital Work in Progress | property, plant & equipment (IAS 16) |

Every cost line carries **two tags**: the project and a cost category. The project's cost is read straight from the
ledger (journal lines on its WIP account carrying its tag), so the project figures and the general ledger can never disagree.

## 2. Entering contractor and subcontractor bills

1. **Operations ▸ Vendor Bills ▸ New.**
2. Choose the vendor (contractor, subcontractor, consultant or supplier) and their invoice number.
3. Choose **Development project** at the top of the bill, and the **Cost category** for its lines (e.g. Construction).
4. Enter the lines as usual. Every line without its own project tag becomes a cost of that project and is posted to the
   project's WIP account, whatever expense account was typed. A line can carry its own category if a bill mixes, say,
   construction and supervision.
5. A bill linked to one project cannot carry lines of another project — split such invoices into one bill per project.
6. Save (draft = *committed* cost), review, then **Post**.

A bill that is not for a project leaves "Development project" empty; individual lines can still be tagged to a project
(the line's "Development cost" choice), which is how a mixed invoice — part maintenance, part project — is handled.

## 3. VAT: cost, recovery and separation

Each project has **Input VAT on this project's costs** (Development Projects ▸ Edit):

| Setting | When to use | Effect on a 100,000 + 5,000 VAT bill line |
|---|---|---|
| **Taxable – fully recoverable** | Commercial buildings; new residential whose *first supply* (sale or first lease within 3 years of completion) is zero-rated | WIP 100,000 · VAT Recoverable 5,000 (claimed on the return) |
| **Exempt – not recoverable** | Residential that will be let or resold as an exempt supply | WIP 105,000 (VAT becomes cost) · nothing claimed |
| **Residual – apportioned** | Mixed-use buildings | WIP 100,000 + the irrecoverable share · VAT Recoverable the recoverable share (provisional rate, annual wash-up) |
| *Decide on each bill line* | Only while the use is undecided | each line's own choice |

The setting is applied to every bill line of the project, so a clerk cannot claim VAT the project cannot support.
Each bill line keeps the split — net, VAT, recoverable, non-recoverable — so the report can show the three apart:

* **Net cost** – the supplier's price;
* **VAT in cost** – irrecoverable VAT capitalised into WIP;
* **VAT recovered** – sitting in VAT Recoverable and claimed on the VAT return (not part of the project's cost).

Buildings above the capital-asset threshold are also followed under **Operations ▸ Capital Assets Scheme (VAT)**, which
adjusts recovery over the adjustment period when the use changes.

## 4. Reports

| Report | Where | Use it for |
|---|---|---|
| **Project Costs & WIP** | Reporting ▸ Project Costs & WIP (also from each project) | Monthly project pack: cost by project, VAT split, committed, paid/unpaid, WIP reconciliation; one project in detail by category, month, vendor and bill |
| Project details | Operations ▸ Development Projects ▸ project | Budget against actual by category, the project's ledger |
| General Ledger | Reporting ▸ General Ledger, account 13100/15500/15600 | Every posting on the WIP account |
| Build Your Own Report | Reporting ▸ Build Your Own Report, source **Vendor bill lines** or **Journal lines** | Ad-hoc cuts, e.g. VAT in cost by project, spend by subcontractor by month, cost by category over time |
| Vendor Statement | Reporting ▸ Vendor Statement | A subcontractor's account across projects |

Columns of the Project Costs & WIP report:

* **Budget** – the project's budget lines.
* **Bills: net / Bills: VAT in cost** – from posted bill lines tagged to the project.
* **Other costs** – posted to the WIP account by journals, petty cash, finance-cost capitalisation.
* **Cost to date** = net + VAT in cost + other costs.
* **VAT recovered** – shown for completeness; not in the cost.
* **Transferred out / WIP balance** – cost moved out on completion, and what remains in WIP.
* **Committed** – draft bills and journals tagged to the project.
* **Budget left** = budget − cost to date − committed (red when over).
* **Unpaid to suppliers** – posted project bills less payments (payments on a mixed bill are shared in proportion).

## 5. Month-end reconciliation checklist

1. **Post or explain drafts.** Committed costs on the report are drafts; post them or note why they wait.
2. **Reconcile each WIP account** (section 2 of the report): *Balance in the ledger* must equal *Sum of project WIP balances*.
   * **Postings not tagged to a project** are listed with entry numbers — reverse and re-enter them tagged, or move them
     with a manual journal (credit the WIP account untagged, debit it tagged to the right project and category).
   * **Still unexplained** is usually an opening balance typed onto the WIP account; allocate it the same way.
   * Target: *Still unexplained = 0.00 ✓* for every WIP account.
3. **Check the VAT split**: VAT recovered on project bills should agree to the input tax claimed on the VAT return for
   those bills; VAT in cost should only appear on Exempt/Residual projects.
4. **Budget review**: categories with *Left* in red are over budget including commitments.
5. **Payables**: *Unpaid to suppliers* per project agrees to the vendor statements of the contractors concerned.
6. **On completion**: run the completion transfer from the project; its WIP balance should then be zero (or only late
   invoices/retention, which still post to the project after completion).
7. Close the month once 1–6 are clean.

## 6. Good practice

* Set the project's VAT treatment **before** the first bill.
* Give every contractor and subcontractor their own vendor record; mark the main contractor on the project.
* One bill per project; use cost categories consistently so budget against actual is meaningful.
* Never post project costs to an expense account and "fix it later" — the WIP account and tag are what make the
  project cost, the capitalised asset and the VAT position right.
