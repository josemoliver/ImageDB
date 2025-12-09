-- Migration: Add performance indexes to Region table
-- Date: 2025-12-08
-- Purpose: Optimize region lookups by ImageId for smart thumbnail caching

-- Critical: Index on ImageId for fast retrieval of all regions for an image
CREATE INDEX IF NOT EXISTS idx_region_imageid ON Region(ImageId);

-- Optional: Composite index for coordinate-based lookups (future optimization)
-- CREATE INDEX IF NOT EXISTS idx_region_coordinates ON Region(ImageId, RegionAreaH, RegionAreaW, RegionAreaX, RegionAreaY);

-- Optional: Index on region name for search/filter operations
-- CREATE INDEX IF NOT EXISTS idx_region_name ON Region(ImageId, RegionName);

-- Verify indexes were created
-- SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='Region';
