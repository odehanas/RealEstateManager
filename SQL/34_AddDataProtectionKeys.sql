-- ============================================================================
-- 34_AddDataProtectionKeys.sql
--
-- The keys that protect sign-in cookies and form (anti-forgery) tokens. Until
-- now they lived only in the server's memory, so every republish or application
-- pool recycle made new ones: everyone was signed out and any form open at the
-- time was refused with "400 Bad Request". Kept here, they survive restarts.
--
-- Adds:
--   DataProtectionKeys   the key ring (the shape ASP.NET Core's EF key store expects)
--
-- Applied automatically when the application starts (see SQL/README.md). Each
-- statement is skipped when its object is already there.
-- ============================================================================


-- 1. The key ring.
IF OBJECT_ID('DataProtectionKeys', 'U') IS NULL CREATE TABLE [DataProtectionKeys] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [FriendlyName] nvarchar(max) NULL,
    [Xml] nvarchar(max) NULL,
    CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY ([Id])
);
