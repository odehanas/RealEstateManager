using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The cross reference behind every report line: from a journal entry's source
    /// (SourceType + SourceId) to the page of the original document. Sources with no page
    /// of their own (manual journals, petty cash, revenue recognition, ...) return null and
    /// the journal entry page stands in for them.
    /// </summary>
    public sealed class DocumentLinkService
    {
        private readonly ApplicationDbContext _db;

        public DocumentLinkService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>The page (controller, action, id) of the source document, if it has one.</summary>
        public async Task<(string Controller, string Action, int Id)?> FindAsync(string? sourceType, int? sourceId)
        {
            if (sourceId is not int id || string.IsNullOrWhiteSpace(sourceType))
                return null;

            return await TargetAsync(sourceType, id);
        }

        /// <summary>True when the source has a document page of its own.</summary>
        public static bool HasDocumentPage(string? sourceType) => sourceType is
            "VendorBill" or "VendorPayment" or VendorClearingService.AdvanceSourceType or
            "CustomerInvoice" or "CustomerCreditNote" or
            "OwnerSettlement" or "OwnerCommissionInvoice" or
            "PdcReceiptMemo" or "PdcClearance" or
            "FixedAssetAcquisition" or "FixedAssetCapitalization" or "FixedAssetDepreciation" or
            "DevelopmentCost" or "DevelopmentTransfer" or
            "UnitSaleReceipt" or "UnitSaleHandover" or
            "PayrollRun" or "PayrollPayment" or
            CapitalAssetVatService.SourceType;

        private async Task<(string Controller, string Action, int Id)?> TargetAsync(string sourceType, int id)
        {
            switch (sourceType)
            {
                case "VendorBill": return ("VendorBills", "Details", id);
                case "VendorPayment": return ("VendorPayments", "Details", id);
                case "CustomerInvoice": return ("CustomerInvoices", "TaxInvoice", id);
                case "CustomerCreditNote": return ("CustomerCreditNotes", "Document", id);
                case "OwnerSettlement": return ("OwnerSettlements", "Print", id);
                case "OwnerCommissionInvoice": return ("OwnerSettlements", "CommissionInvoice", id);
                case "PdcReceiptMemo":
                case "PdcClearance": return ("PdcReceipts", "Edit", id);
                case "FixedAssetAcquisition":
                case "FixedAssetCapitalization": return ("FixedAssets", "Acquisition", id);
                case "DevelopmentCost": return ("DevelopmentProjects", "Details", id);
                case "PayrollRun": return ("Payroll", "Run", id);
                case "UnitSaleHandover": return ("UnitSales", "Details", id);

                // The source id is a child record: open its parent document.
                case "FixedAssetDepreciation":
                    return Page("FixedAssets", "Depreciation", await _db.FixedAssetDepreciations.AsNoTracking()
                        .Where(x => x.Id == id).Select(x => (int?)x.FixedAssetId).FirstOrDefaultAsync());
                case "DevelopmentTransfer":
                    return Page("DevelopmentProjects", "Details", await _db.DevelopmentTransfers.AsNoTracking()
                        .Where(x => x.Id == id).Select(x => (int?)x.DevelopmentProjectId).FirstOrDefaultAsync());
                case "UnitSaleReceipt":
                    return Page("UnitSales", "Details", await _db.UnitSaleReceipts.AsNoTracking()
                        .Where(x => x.Id == id).Select(x => (int?)x.UnitSaleId).FirstOrDefaultAsync());
                case "PayrollPayment":
                    return Page("Payroll", "Run", await _db.PayrollPayments.AsNoTracking()
                        .Where(x => x.Id == id).Select(x => (int?)x.PayrollRunId).FirstOrDefaultAsync());
                case CapitalAssetVatService.SourceType:
                    return Page("CapitalAssetVat", "Details", await _db.CapitalAssetVatAdjustments.AsNoTracking()
                        .Where(x => x.Id == id).Select(x => (int?)x.CapitalAssetVatRecordId).FirstOrDefaultAsync());
                case VendorClearingService.AdvanceSourceType:
                    return Page("VendorPayments", "Details", await _db.VendorClearings.AsNoTracking()
                        .Where(x => x.Id == id).Select(x => x.AdvancePaymentId).FirstOrDefaultAsync());

                default: return null;
            }
        }

        private static (string Controller, string Action, int Id)? Page(string controller, string action, int? id) =>
            id is int value ? (controller, action, value) : null;
    }
}
