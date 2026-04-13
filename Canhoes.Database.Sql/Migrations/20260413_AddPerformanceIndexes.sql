-- Migration: Add performance indexes for common query patterns
-- Date: 2026-04-13
-- Reason: Queries frequently filter by EventId + Status/IsActive combinations

-- 1. GalaMeasureEntity: composite index for EventId + IsActive
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Measures_EventId_IsActive' 
    AND object_id = OBJECT_ID('GalaMeasure')
)
BEGIN
    CREATE INDEX IX_Measures_EventId_IsActive ON GalaMeasure(EventId, IsActive);
    PRINT 'Created index IX_Measures_EventId_IsActive';
END

-- 2. NomineeEntity: composite index for EventId + Status + CategoryId
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Nominees_EventId_Status_CategoryId' 
    AND object_id = OBJECT_ID('Nominees')
)
BEGIN
    CREATE INDEX IX_Nominees_EventId_Status_CategoryId ON Nominees(EventId, Status, CategoryId);
    PRINT 'Created index IX_Nominees_EventId_Status_CategoryId';
END

-- 3. AwardCategoryEntity: composite index for EventId + IsActive
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_AwardCategories_EventId_IsActive' 
    AND object_id = OBJECT_ID('AwardCategories')
)
BEGIN
    CREATE INDEX IX_AwardCategories_EventId_IsActive ON AwardCategories(EventId, IsActive);
    PRINT 'Created index IX_AwardCategories_EventId_IsActive';
END
GO
