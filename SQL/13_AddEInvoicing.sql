-- ============================================================================
-- 13_AddEInvoicing.sql
--
-- UAE electronic invoicing (Ministerial Decisions 243 and 244 of 2025) and
-- customer credit notes.
--
-- Adds:
--   Companies          the e-invoicing identity (TIN, trade licence, emirate, IBAN),
--                      the on/off switch, revenue band, start date and provider
--   Tenants            customer type (individual / business / government), TIN,
--                      registration details and a structured address
--   Vendors            TIN, to match supplier e-invoices to the vendor
--   CustomerInvoices   rent period, credited amount and e-invoice tracking
--   CustomerCreditNotes   new table - credit notes against customer invoices
--   ReceivedEInvoices     new table - e-invoices received from suppliers
--
-- Nothing here touches existing amounts or journals. E-invoicing stays OFF for
-- every company until it is switched on in System Setup > E-Invoicing Settings,
-- so the application behaves exactly as before until then.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run every statement BEFORE deploying the new build. The application reads
-- these columns and will error without them.
-- ============================================================================


-- 1. Company e-invoicing identity, switch and provider.
ALTER TABLE [Companies] ADD
    [TaxIdentificationNumber] nvarchar(20) NULL,
    [TradeLicenseNumber] nvarchar(60) NULL,
    [LegalRegistrationType] nvarchar(10) NOT NULL CONSTRAINT [DF_Companies_LegalRegistrationType] DEFAULT ('TL'),
    [LicensingAuthority] nvarchar(200) NULL,
    [Emirate] nvarchar(10) NULL,
    [CountryCode] nvarchar(2) NOT NULL CONSTRAINT [DF_Companies_CountryCode] DEFAULT ('AE'),
    [BankIban] nvarchar(40) NULL,
    [BankAccountName] nvarchar(200) NULL,
    [EInvoicingEnabled] bit NOT NULL CONSTRAINT [DF_Companies_EInvoicingEnabled] DEFAULT (0),
    [EInvoicingRevenueBand] nvarchar(20) NULL,
    [EInvoicingStartDate] datetime2 NULL,
    [EInvoicingProviderName] nvarchar(200) NULL,
    [EInvoicingProviderAppointedOn] datetime2 NULL,
    [EInvoicePaymentMeansCode] nvarchar(10) NOT NULL CONSTRAINT [DF_Companies_EInvoicePaymentMeansCode] DEFAULT ('30');


-- 2. Tenant customer type, identifiers and structured address. The existing
--    Address column becomes the street address.
ALTER TABLE [Tenants] ADD
    [PartyType] nvarchar(20) NOT NULL CONSTRAINT [DF_Tenants_PartyType] DEFAULT ('Individual'),
    [TaxIdentificationNumber] nvarchar(20) NULL,
    [LegalRegistrationType] nvarchar(10) NULL,
    [LicensingAuthority] nvarchar(200) NULL,
    [PassportIssuingCountry] nvarchar(2) NULL,
    [City] nvarchar(100) NULL,
    [Emirate] nvarchar(10) NULL,
    [CountryCode] nvarchar(2) NOT NULL CONSTRAINT [DF_Tenants_CountryCode] DEFAULT ('AE');


-- 3. A tenant holding a trade licence or a TRN is almost certainly a business,
--    whose invoices are in scope for e-invoicing. Everyone else stays an
--    individual. Review the result in System Setup > E-Invoicing Settings,
--    which lists anyone who looks misclassified.
UPDATE [Tenants]
SET [PartyType] = 'Business'
WHERE ([TradeLicenseNumber] IS NOT NULL AND LTRIM(RTRIM([TradeLicenseNumber])) <> '')
   OR ([TrnNumber] IS NOT NULL AND LTRIM(RTRIM([TrnNumber])) <> '');


-- 4. Vendor TIN, used to match a supplier's e-invoice to the vendor record.
ALTER TABLE [Vendors] ADD
    [TaxIdentificationNumber] nvarchar(20) NULL;


-- 5. Invoice rent period, credit notes and e-invoice tracking.
ALTER TABLE [CustomerInvoices] ADD
    [PeriodStart] datetime2 NULL,
    [PeriodEnd] datetime2 NULL,
    [CreditedAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_CustomerInvoices_CreditedAmount] DEFAULT (0),
    [EInvoiceStatus] nvarchar(20) NOT NULL CONSTRAINT [DF_CustomerInvoices_EInvoiceStatus] DEFAULT ('NotRequired'),
    [EInvoiceUuid] uniqueidentifier NULL,
    [EInvoiceXml] nvarchar(max) NULL,
    [EInvoiceGeneratedAt] datetime2 NULL,
    [EInvoiceSubmittedAt] datetime2 NULL,
    [EInvoiceResponseAt] datetime2 NULL,
    [EInvoiceProviderReference] nvarchar(200) NULL,
    [EInvoiceRejectionReason] nvarchar(2000) NULL;


-- 6. The e-invoice monitor lists what is still to be sent.
CREATE INDEX [IX_CustomerInvoices_CompanyId_EInvoiceStatus]
    ON [CustomerInvoices] ([CompanyId], [EInvoiceStatus]);


-- 7. Credit notes against customer invoices.
CREATE TABLE [CustomerCreditNotes] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [CustomerInvoiceId] int NOT NULL,
    [CreditNoteNumber] nvarchar(40) NOT NULL,
    [CreditNoteDate] datetime2 NOT NULL,
    [ReasonCode] nvarchar(20) NOT NULL,
    [Reason] nvarchar(500) NOT NULL,
    [VatCategory] nvarchar(20) NOT NULL,
    [VatRate] decimal(9,4) NOT NULL,
    [Subtotal] decimal(18,2) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [DebitAccountId] int NULL,
    [Status] nvarchar(20) NOT NULL,
    [JournalEntryId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedByAppUserId] int NULL,
    [EInvoiceStatus] nvarchar(20) NOT NULL,
    [EInvoiceUuid] uniqueidentifier NULL,
    [EInvoiceXml] nvarchar(max) NULL,
    [EInvoiceGeneratedAt] datetime2 NULL,
    [EInvoiceSubmittedAt] datetime2 NULL,
    [EInvoiceResponseAt] datetime2 NULL,
    [EInvoiceProviderReference] nvarchar(200) NULL,
    [EInvoiceRejectionReason] nvarchar(2000) NULL,
    CONSTRAINT [PK_CustomerCreditNotes] PRIMARY KEY ([Id])
);


-- 8. The company the credit note belongs to.
ALTER TABLE [CustomerCreditNotes] ADD CONSTRAINT [FK_CustomerCreditNotes_Companies_CompanyId]
    FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]);


-- 9. The invoice it reduces.
ALTER TABLE [CustomerCreditNotes] ADD CONSTRAINT [FK_CustomerCreditNotes_CustomerInvoices_CustomerInvoiceId]
    FOREIGN KEY ([CustomerInvoiceId]) REFERENCES [CustomerInvoices]([Id]);


-- 10. The account the net amount is reversed into, when not the invoice's own.
ALTER TABLE [CustomerCreditNotes] ADD CONSTRAINT [FK_CustomerCreditNotes_Accounts_DebitAccountId]
    FOREIGN KEY ([DebitAccountId]) REFERENCES [Accounts]([Id]);


-- 11. Its journal entry.
ALTER TABLE [CustomerCreditNotes] ADD CONSTRAINT [FK_CustomerCreditNotes_JournalEntries_JournalEntryId]
    FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries]([Id]);


-- 12. Credit note numbers are one sequence per company: CN-2026-000001.
CREATE UNIQUE INDEX [IX_CustomerCreditNotes_CompanyId_CreditNoteNumber]
    ON [CustomerCreditNotes] ([CompanyId], [CreditNoteNumber]);


-- 13. The e-invoice monitor lists credit notes still to be sent.
CREATE INDEX [IX_CustomerCreditNotes_CompanyId_EInvoiceStatus]
    ON [CustomerCreditNotes] ([CompanyId], [EInvoiceStatus]);


-- 14. Look-ups from the invoice to its credit notes.
CREATE INDEX [IX_CustomerCreditNotes_CustomerInvoiceId]
    ON [CustomerCreditNotes] ([CustomerInvoiceId]);


-- 15. Supporting index for the account link.
CREATE INDEX [IX_CustomerCreditNotes_DebitAccountId]
    ON [CustomerCreditNotes] ([DebitAccountId]);


-- 16. Supporting index for the journal link.
CREATE INDEX [IX_CustomerCreditNotes_JournalEntryId]
    ON [CustomerCreditNotes] ([JournalEntryId]);


-- 17. E-invoices received from suppliers, kept exactly as received.
CREATE TABLE [ReceivedEInvoices] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [Uuid] nvarchar(100) NOT NULL,
    [DocumentTypeCode] nvarchar(10) NOT NULL,
    [DocumentNumber] nvarchar(100) NOT NULL,
    [IssueDate] datetime2 NOT NULL,
    [DueDate] datetime2 NULL,
    [SupplierName] nvarchar(300) NOT NULL,
    [SupplierTrn] nvarchar(30) NULL,
    [SupplierTin] nvarchar(20) NULL,
    [VendorId] int NULL,
    [Currency] nvarchar(3) NOT NULL,
    [NetAmount] decimal(18,2) NOT NULL,
    [VatAmount] decimal(18,2) NOT NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [Xml] nvarchar(max) NOT NULL,
    [FileName] nvarchar(260) NULL,
    [Status] nvarchar(20) NOT NULL,
    [VendorBillId] int NULL,
    [ReceivedAt] datetime2 NOT NULL,
    [ReceivedByAppUserId] int NULL,
    CONSTRAINT [PK_ReceivedEInvoices] PRIMARY KEY ([Id])
);


-- 18. The company that received it.
ALTER TABLE [ReceivedEInvoices] ADD CONSTRAINT [FK_ReceivedEInvoices_Companies_CompanyId]
    FOREIGN KEY ([CompanyId]) REFERENCES [Companies]([Id]);


-- 19. The vendor it was matched to.
ALTER TABLE [ReceivedEInvoices] ADD CONSTRAINT [FK_ReceivedEInvoices_Vendors_VendorId]
    FOREIGN KEY ([VendorId]) REFERENCES [Vendors]([Id]);


-- 20. The vendor bill it was booked as.
ALTER TABLE [ReceivedEInvoices] ADD CONSTRAINT [FK_ReceivedEInvoices_VendorBills_VendorBillId]
    FOREIGN KEY ([VendorBillId]) REFERENCES [VendorBills]([Id]);


-- 21. The same supplier document can never be received twice.
CREATE UNIQUE INDEX [IX_ReceivedEInvoices_CompanyId_Uuid]
    ON [ReceivedEInvoices] ([CompanyId], [Uuid]);


-- 22. Supporting index for the vendor link.
CREATE INDEX [IX_ReceivedEInvoices_VendorId]
    ON [ReceivedEInvoices] ([VendorId]);


-- 23. Supporting index for the bill link.
CREATE INDEX [IX_ReceivedEInvoices_VendorBillId]
    ON [ReceivedEInvoices] ([VendorBillId]);


-- 24. VERIFY - the new columns, plus one row for each new table. Expect 36 rows.
SELECT t.name AS TableName, c.name AS ColumnName
FROM sys.columns c
INNER JOIN sys.tables t ON t.object_id = c.object_id
WHERE (t.name = 'Companies' AND c.name IN ('TaxIdentificationNumber','TradeLicenseNumber','LegalRegistrationType','LicensingAuthority','Emirate','CountryCode','BankIban','BankAccountName','EInvoicingEnabled','EInvoicingRevenueBand','EInvoicingStartDate','EInvoicingProviderName','EInvoicingProviderAppointedOn','EInvoicePaymentMeansCode'))
   OR (t.name = 'Tenants' AND c.name IN ('PartyType','TaxIdentificationNumber','LegalRegistrationType','LicensingAuthority','PassportIssuingCountry','City','Emirate','CountryCode'))
   OR (t.name = 'Vendors' AND c.name = 'TaxIdentificationNumber')
   OR (t.name = 'CustomerInvoices' AND c.name IN ('PeriodStart','PeriodEnd','CreditedAmount','EInvoiceStatus','EInvoiceUuid','EInvoiceXml','EInvoiceGeneratedAt','EInvoiceSubmittedAt','EInvoiceResponseAt','EInvoiceProviderReference','EInvoiceRejectionReason'))
   OR (t.name IN ('CustomerCreditNotes','ReceivedEInvoices') AND c.name = 'Id')
ORDER BY t.name, c.name;


-- 25. REVIEW - tenants now classed as businesses. Their invoices will need an
--     e-invoice once e-invoicing is switched on. Correct any that are wrong
--     from Master Data > Tenants.
SELECT [Code], [FullName], [TradeLicenseNumber], [TrnNumber], [PartyType]
FROM [Tenants]
WHERE [PartyType] = 'Business'
ORDER BY [Code];
