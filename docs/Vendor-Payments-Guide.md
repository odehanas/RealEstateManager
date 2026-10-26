# Vendor Payments, Clearing and Advances — Finance Guide

Vendor payments follow SAP's open-item method: money paid to a vendor always settles specific open items, every
settlement carries a clearing document number, and money paid before a bill (an advance / down payment) is kept on a
separate account until it is applied to bills.

| SAP | RealEstateManager | Entry |
|---|---|---|
| F-53 outgoing payment, open items selected | **Operations ▸ Vendor Payments ▸ New Vendor Payment** | Dr Accounts Payable (per bill) · Cr Bank |
| Partial payment (invoice stays open) | Lower the "Pay now" amount on a bill | same |
| Clearing document number | `CLR-yyyy-nnnnnn`, on the payment and on each bill it fully settles | — |
| F-48 down payment, special G/L indicator **A** | **Pay an advance** | Dr 11550 Advances to Suppliers · Cr Bank |
| F-54 clear down payment against invoice | **Apply advance to bills** | Dr Accounts Payable · Cr 11550 Advances to Suppliers |
| FBL1N open items / special G/L items shown separately | Vendor Statement: open bills, and an *Advances* section | — |

## Paying bills

1. Choose the **vendor** — only their posted bills with something open are listed, oldest due first, overdue in red.
2. Tick the bills to pay. Each defaults to its full open amount; lower it for a part payment.
3. Choose **Paid from** (bank or cash account), method, cheque/transfer reference, date.
4. **Save and Post.** The payment gets its number (VPAY-…) and a clearing document (CLR-…).
   * A bill paid in full shows *Cleared by CLR-…* with the date.
   * A part-paid bill stays open for the balance, with the payment linked.
5. With payment approval switched on, the payment waits in Payment Approvals; its amounts are held against the bills
   (shown as "pending") so they cannot be paid twice meanwhile.

A payment must settle at least one open bill. Money paid before there is a bill is an **advance**.

## Advances (down payments)

1. **Pay an advance**: choose the vendor and the amount. It posts to **11550 Advances to Suppliers**, tagged to the
   vendor — an asset, *not* a reduction of Accounts Payable — so payables are never understated by unbilled advances.
2. When the vendor's bills arrive and are posted, **Apply advance to bills**: choose the advance and the bills, and how
   much of each to settle. No money moves; the entry is Dr Accounts Payable / Cr Advances to Suppliers with its own
   clearing number. Pay any remainder of the bills with a normal payment.
3. A contractor's mobilisation advance recovered over several progress bills: apply part of the advance to each bill as
   it is certified.

**VAT:** in the UAE an advance can be a tax point for the supplier. If the supplier issues a *tax invoice* for the
advance, record it as a vendor bill (so its input VAT is recovered) and pay that bill; use the advance function for
advances without a tax invoice.

## Corrections

* A payment or an advance application is corrected by **Reset to Draft** in Journal Inquiry: the bills reopen, the
  clearing document is marked *Reversed*, and the advance's open amount is restored. Post again when corrected.
* An advance that has been applied cannot be reset until its applications are reset.
* A bill settled by any clearing document cannot be reset until those payments/applications are reset.

## Month-end checks

1. **11550 Advances to Suppliers** in the trial balance = total *Open advances* on the vendor statements.
2. **Accounts Payable** = total of open bill balances per vendor.
3. Review long-open advances: apply them, or chase the vendor for the goods, service or refund.
4. Build your own lists in **Reporting ▸ Build Your Own Report** with the source *Vendor clearing documents*.
