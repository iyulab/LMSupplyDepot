# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2025-11-17

### Added
- **ModelsDirectory option** in `ModelHubOptions` and `LMSupplyDepotOptions` for explicit model storage path configuration
  - Allows direct specification of model directory without automatic `\models` subdirectory appending
  - Supports environment variable: `LMSupplyDepots__ModelsDirectory`
  - Backward compatible: defaults to `{DataPath}/models` when not set

### Fixed
- **Model discovery bug**: Fixed issue where `DataPath` automatically appended `\models` subdirectory, causing model discovery to fail when users provided paths already ending with `\models` (e.g., `C:\Users\xxx\.filer\models` → incorrectly used as `C:\Users\xxx\.filer\models\models`)
- Improved logging to show actual models directory being used for better debugging

### Changed
- `FileSystemHelper` methods now use `modelsPath` parameter directly instead of auto-appending `\models`
- `FileSystemModelRepository` now computes effective models directory using `ModelHubOptions.GetModelsDirectory()`
- `DownloadManager` and related services updated to use models directory directly
- Renamed `DownloadStateHelper.FindDownloadStatesInDataPath` → `FindDownloadStatesInModelsPath` for clarity

### Migration Guide
For integrations experiencing the double `\models` path issue:

**Before (workaround):**
```csharp
// Pass parent directory to compensate for auto-append
var options = new LMSupplyDepotOptions
{
    DataPath = Path.GetDirectoryName(modelsPath) // C:\Users\xxx\.filer
};
```

**After (recommended):**
```csharp
// Directly specify models directory
var options = new LMSupplyDepotOptions
{
    ModelsDirectory = modelsPath // C:\Users\xxx\.filer\models
};
```

**Or via environment variable:**
```bash
set LMSupplyDepots__ModelsDirectory=C:\Users\xxx\.filer\models
```

## [0.2.2] - Earlier releases

See git history for earlier changes.
