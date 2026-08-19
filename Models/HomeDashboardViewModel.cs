namespace RealEstateManager.Models
{
    public class HomeDashboardViewModel
    {
        public DateTime Today { get; set; } = DateTime.Today;

        public int PropertiesCount { get; set; }
        public int OwnedPropertiesCount { get; set; }
        public int ManagedPropertiesCount { get; set; }
        public int ActiveUnitsCount { get; set; }
        public int OccupiedUnitsCount { get; set; }
        public int VacantUnitsCount { get; set; }
        public int UnderMaintenanceUnitsCount { get; set; }
        public int ReservedUnitsCount { get; set; }
        public int ActiveLeasesCount { get; set; }
        public int ActiveTenantsCount { get; set; }

        public int OpenPeriodsCount { get; set; }
        public string CurrentPeriodName { get; set; } = "Not Available";

        public int OutstandingInvoicesCount { get; set; }
        public decimal OutstandingReceivables { get; set; }
        public int OverdueInvoicesCount { get; set; }
        public decimal OverdueReceivables { get; set; }

        public decimal ThisMonthInvoices { get; set; }
        public decimal ThisMonthReceipts { get; set; }
        public decimal MonthlyCollectionRate { get; set; }
        public decimal OverdueReceivablesRate { get; set; }

        public int UnclearedPdcCount { get; set; }
        public decimal UnclearedPdcAmount { get; set; }
        public bool PdcDataAvailable { get; set; } = true;

        public decimal OccupancyRate { get; set; }
        public decimal VacancyRate { get; set; }
        public decimal ManagedPortfolioRate { get; set; }

        public bool CashKpiAvailable { get; set; }
        public string CashAccountName { get; set; } = "Not Configured";
        public decimal CashBalance { get; set; }

        public bool LandlordKpiAvailable { get; set; }
        public string DueToLandlordsAccountName { get; set; } = "Not Configured";
        public decimal DueToLandlordsBalance { get; set; }

        public List<HomeDashboardManagedPropertyItemViewModel> ManagedPropertyDrilldown { get; set; } = new();
        public List<HomeDashboardInvoiceItemViewModel> RecentInvoices { get; set; } = new();
        public List<HomeDashboardReceiptItemViewModel> RecentReceipts { get; set; } = new();
        public List<HomeDashboardPdcItemViewModel> RecentPdcReceipts { get; set; } = new();
    }

    public class HomeDashboardManagedPropertyItemViewModel
    {
        public int PropertyId { get; set; }
        public string PropertyCode { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public int UnitsCount { get; set; }
        public int OccupiedUnitsCount { get; set; }
        public int ActiveLeasesCount { get; set; }
        public decimal OutstandingReceivables { get; set; }
        public decimal OccupancyRate { get; set; }
    }

    public class HomeDashboardInvoiceItemViewModel
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime InvoiceDate { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string LeaseDisplay { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class HomeDashboardReceiptItemViewModel
    {
        public string ReceiptNumber { get; set; } = string.Empty;
        public DateTime ReceiptDate { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
    }

    public class HomeDashboardPdcItemViewModel
    {
        public string ChequeNumber { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string SettlementMode { get; set; } = string.Empty;
    }
}
