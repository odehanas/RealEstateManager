using RealEstateManager.Domain;

namespace RealEstateManager.Models
{
    public class ApprovalReviewViewModel
    {
        public ApprovalRequest Request { get; set; } = new();

        /// <summary>Every submission of this document, newest first.</summary>
        public List<ApprovalRequest> History { get; set; } = new();

        /// <summary>Why the current user may not decide this one (four-eyes rule), or null.</summary>
        public string? FourEyesBlock { get; set; }

        public string DocumentUrl { get; set; } = "#";

        // ---- For a contract ----------------------------------------------------
        public Lease? Lease { get; set; }

        /// <summary>The rent invoices the contract will create once approved.</summary>
        public List<CustomerInvoice> Instalments { get; set; } = new();
    }
}
