-- Migration: Drop ContentBytes from HubPostMedia table
-- Date: 2026-04-13
-- Reason: BLOBs in SQL Server cause memory pressure and OOM risk.
--         Media files are now stored on filesystem (wwwroot/uploads/hub/).
--         DB only tracks URLs.

IF EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID('HubPostMedia') 
    AND name = 'ContentBytes'
)
BEGIN
    ALTER TABLE HubPostMedia
    DROP COLUMN ContentBytes;
    
    PRINT 'ContentBytes column dropped from HubPostMedia.';
END
ELSE
BEGIN
    PRINT 'ContentBytes column does not exist in HubPostMedia. No action needed.';
END
GO
