SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @AssetCode NVARCHAR(50) = 'FA-TEST-001';
DECLARE @CompanyId INT = NULL; -- optional: set this if the same asset code exists in multiple companies

DECLARE @FixedAssetId INT;

SELECT TOP (1) @FixedAssetId = fa.Id
FROM dbo.FixedAssets fa
WHERE fa.AssetCode = @AssetCode
  AND (@CompanyId IS NULL OR fa.CompanyId = @CompanyId)
ORDER BY fa.Id;

IF @FixedAssetId IS NULL
BEGIN
    RAISERROR('Fixed asset was not found for the supplied AssetCode / CompanyId.', 16, 1);
    RETURN;
END;

DECLARE @JournalEntriesToDelete TABLE
(
    Id INT PRIMARY KEY
);

INSERT INTO @JournalEntriesToDelete (Id)
SELECT DISTINCT x.Id
FROM
(
    SELECT fa.CapitalizationJournalEntryId AS Id
    FROM dbo.FixedAssets fa
    WHERE fa.Id = @FixedAssetId

    UNION ALL

    SELECT fac.JournalEntryId
    FROM dbo.FixedAssetAcquisitionCosts fac
    WHERE fac.FixedAssetId = @FixedAssetId

    UNION ALL

    SELECT fad.JournalEntryId
    FROM dbo.FixedAssetDepreciations fad
    WHERE fad.FixedAssetId = @FixedAssetId

    UNION ALL

    SELECT je.Id
    FROM dbo.JournalEntries je
    WHERE je.SourceType IN ('FixedAssetAcquisition', 'FixedAssetCapitalization')
      AND je.SourceId = @FixedAssetId

    UNION ALL

    SELECT je.Id
    FROM dbo.JournalEntries je
    INNER JOIN dbo.FixedAssetDepreciations fad
        ON je.SourceType = 'FixedAssetDepreciation'
       AND je.SourceId = fad.Id
    WHERE fad.FixedAssetId = @FixedAssetId
) x
WHERE x.Id IS NOT NULL;

UPDATE dbo.FixedAssets
SET CapitalizationJournalEntryId = NULL
WHERE Id = @FixedAssetId;

UPDATE dbo.FixedAssetDepreciations
SET JournalEntryId = NULL
WHERE FixedAssetId = @FixedAssetId;

UPDATE dbo.FixedAssetAcquisitionCosts
SET JournalEntryId = NULL
WHERE FixedAssetId = @FixedAssetId;

DELETE FROM dbo.FixedAssetDepreciations
WHERE FixedAssetId = @FixedAssetId;

DELETE FROM dbo.FixedAssetAcquisitionCosts
WHERE FixedAssetId = @FixedAssetId;

DELETE jel
FROM dbo.JournalEntryLines jel
INNER JOIN @JournalEntriesToDelete j
    ON j.Id = jel.JournalEntryId;

DELETE je
FROM dbo.JournalEntries je
INNER JOIN @JournalEntriesToDelete j
    ON j.Id = je.Id;

DELETE FROM dbo.FixedAssets
WHERE Id = @FixedAssetId;

SELECT 'Deleted Fixed Asset ID' AS [Message], @FixedAssetId AS [Value];
SELECT 'Deleted Journal Entries' AS [Message], COUNT(*) AS [Value]
FROM @JournalEntriesToDelete;
