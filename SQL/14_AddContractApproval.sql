-- ============================================================================
-- 14_AddContractApproval.sql
--
-- Contracts in two levels: one person creates a contract, a second person
-- approves it before it takes effect.
--
-- Adds:
--   Companies.RequireContractApproval   the company's on/off switch (starts OFF)
--   Leases.CreatedByAppUserId            who created a contract - who may not approve it
--   ApprovalRequests                     every submission, approval and return, with who and why
--
-- Nothing in here changes existing contracts or figures. Every existing contract
-- stays Active. Approval stays OFF for every company until an approver switches it
-- on in Master Data > Approvals.
--
-- HOW TO RUN in site4now SQL Studio: run ONE numbered statement at a time, top
-- to bottom. The console executes only the first batch, so pasting the whole
-- file runs statement 1 and nothing else.
--
-- Run statements 1 to 5 BEFORE deploying the new build. The application reads
-- these columns and the table, and its screens will error without them.
-- ============================================================================


-- 1. The company's switch. Off for every company, so nothing changes until someone turns it on.
ALTER TABLE [Companies] ADD [RequireContractApproval] bit NOT NULL CONSTRAINT [DF_Companies_RequireContractApproval] DEFAULT (0);


-- 2. Who created each contract. Empty for existing contracts, which are already active.
ALTER TABLE [Leases] ADD [CreatedByAppUserId] int NULL;


-- 3. The approval requests.
CREATE TABLE [ApprovalRequests] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [CompanyId] int NOT NULL,
    [DocumentType] nvarchar(50) NOT NULL,
    [DocumentId] int NOT NULL,
    [DocumentNumber] nvarchar(100) NOT NULL,
    [Summary] nvarchar(300) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [RequestedByAppUserId] int NOT NULL,
    [RequestedByName] nvarchar(200) NOT NULL,
    [RequestedAt] datetime2 NOT NULL,
    [DocumentCreatedByAppUserId] int NULL,
    [DecidedByAppUserId] int NULL,
    [DecidedByName] nvarchar(200) NULL,
    [DecidedAt] datetime2 NULL,
    [DecisionComment] nvarchar(1000) NULL,
    CONSTRAINT [PK_ApprovalRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ApprovalRequests_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id])
);


-- 4. Finding what is waiting in a company.
CREATE INDEX [IX_ApprovalRequests_CompanyId_Status] ON [ApprovalRequests] ([CompanyId], [Status]);


-- 5. Finding the history of one contract.
CREATE INDEX [IX_ApprovalRequests_DocumentType_DocumentId] ON [ApprovalRequests] ([DocumentType], [DocumentId]);


-- 6. CHECK (changes nothing): the new columns and table exist. Expect three rows.
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE (TABLE_NAME = 'Companies' AND COLUMN_NAME = 'RequireContractApproval')
   OR (TABLE_NAME = 'Leases' AND COLUMN_NAME = 'CreatedByAppUserId')
   OR (TABLE_NAME = 'ApprovalRequests' AND COLUMN_NAME = 'DecisionComment');


-- 7. CHECK (changes nothing): a contract's status must hold "PendingApproval" (15 characters).
-- Expect -1 (unlimited) or a number of 15 or more.
SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Leases' AND COLUMN_NAME = 'Status';
