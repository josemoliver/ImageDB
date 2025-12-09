-- Migration: Add PixelHash column to Image table
-- Date: 2025-12-08
-- Purpose: Store hash of decoded pixel data to detect actual image content changes
--          vs metadata-only changes, enabling intelligent thumbnail regeneration

-- Add PixelHash column to existing databases
ALTER TABLE Image ADD COLUMN PixelHash TEXT;

-- Optional: Create index for faster lookups if needed
-- CREATE INDEX idx_image_pixelhash ON Image(PixelHash);

-- Note: Existing records will have NULL PixelHash until next scan/update
-- The application will compute and populate this field automatically
