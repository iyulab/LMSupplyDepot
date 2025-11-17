# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.1] - 2025-11-17

### Fixed
- **HuggingFace double extension bug**: Fixed issue where artifact names with `.gguf` extension resulted in duplicate extensions
  - Root cause: `HuggingFaceHelper.FindArtifactFiles()` and related methods unconditionally appended `.gguf` extension
  - Impact: Users providing artifact names like `Phi-4-mini-instruct-Q2_K.gguf` experienced 404 download failures
  - Example: `Phi-4-mini-instruct-Q2_K.gguf` → incorrectly became `Phi-4-mini-instruct-Q2_K.gguf.gguf`
  - Solution: Added `EnsureGgufExtension()` and `RemoveGgufExtension()` helper methods for idempotent extension handling
  - Locations fixed: `HuggingFaceHelper.cs`, `HuggingFaceDownloader.Download.cs`, `HuggingFaceDownloader.Helpers.cs`
- **Test infrastructure bug fixes**:
  - Fixed PowerShell script parameter conflict: Renamed `$Verbose` to `$DetailedOutput` to avoid built-in parameter collision
  - Fixed `HuggingFaceDownloaderIntegrationTests` constructor: API token now accepts `null` instead of empty string for public models
  - All 13 LocalIntegration tests now pass successfully (6 HuggingFace + 7 LLamaEngine tests)

### Changed
- **Testability improvements**: Refactored `HuggingFaceDownloader` for dependency injection and testability
  - Refactored `HuggingFaceDownloader` to use `IHuggingFaceClient` interface instead of concrete class
  - Added DI support in `HuggingFaceExtensions` with automatic client factory registration
  - Maintained backward compatibility with legacy constructor (marked as obsolete)
  - Enables mock-based unit testing without external API dependencies

### Added
- **Test infrastructure**: Comprehensive test categorization and CICD optimization
  - Three-tier test categories: `Unit` (fast, mocked), `Integration` (local resources), `LocalIntegration` (external APIs)
  - Integration tests for HuggingFace download scenarios with real API calls
  - Test scripts: `run-all-tests.ps1` (all tests) and `run-cicd-tests.ps1` (fast tests only)
  - CICD workflow excludes `LocalIntegration` tests for faster pipeline execution (181 tests in ~5 seconds)
  - Documentation: Added `BUILD.md` with comprehensive build and test guidelines

## [0.3.0] - 2025-11-17

### 🔥 Breaking Changes

**Simplified configuration model** - Removed the confusing dual-concept of `DataPath` and `ModelsDirectory`

**Before (v0.2.x):**
```csharp
var options = new LMSupplyDepotOptions
{
    DataPath = @"C:\Users\xxx\AppData\Local\LMSupplyDepots"
    // Models stored at: DataPath + "\models"
};
```

**After (v0.3.0):**
```csharp
var options = new LMSupplyDepotOptions
{
    ModelsDirectory = @"C:\Users\xxx\.filer\models"  // Direct path
};
```

### Fixed
- **Model discovery bug**: Eliminated automatic `\models` subdirectory appending that caused double path issue
  - Root cause: `FileSystemHelper` auto-appended `\models` to all paths
  - Impact: Users providing paths ending with `\models` experienced discovery failures
  - Example: `C:\Users\xxx\.filer\models` → incorrectly became `C:\Users\xxx\.filer\models\models`

### Changed
- **Removed `DataPath` property** from `ModelHubOptions` and `LMSupplyDepotOptions`
- **Renamed to `ModelsDirectory`** - clearer, single-purpose property
- **Default value updated**: Now `%LocalAppData%/LMSupplyDepots/models` (includes "models" in default)
- **Environment variable**: `LMSupplyDepots__ModelsDirectory` (was: `LMSupplyDepots__DataPath`)
- **API Response**: `GET /api/system/config` now returns `ModelsDirectory` instead of `DataPath`

### Migration Guide

#### Code Changes Required

**1. Update Options Configuration**
```csharp
// Before
var options = new LMSupplyDepotOptions
{
    DataPath = @"C:\data\LMSupplyDepots"
};

// After
var options = new LMSupplyDepotOptions
{
    ModelsDirectory = @"C:\data\LMSupplyDepots\models"
};
```

**2. Update Environment Variables**
```bash
# Before
set LMSupplyDepots__DataPath=C:\data\LMSupplyDepots

# After
set LMSupplyDepots__ModelsDirectory=C:\data\LMSupplyDepots\models
```

**3. Update appsettings.json**
```json
{
  "LMSupplyDepots": {
    "ModelsDirectory": "C:\\data\\models"
  }
}
```

#### For Existing Users

If you were using the **default path**, no action needed - models will be found at the same location.

If you **explicitly set DataPath**:
- Add `\models` to your path value
- Or set `ModelsDirectory` to your existing models location

#### For Integrations (like Filer)

Remove the workaround and use explicit configuration:

```csharp
// Remove this workaround
var parentPath = Path.GetDirectoryName(_currentModelsPath);
["LMSupplyDepots__DataPath"] = parentPath

// Use this instead
["LMSupplyDepots__ModelsDirectory"] = _currentModelsPath
```

### Technical Details

**Refactored Components:**
- `FileSystemHelper`: All methods now use `modelsPath` parameter directly (no auto-append)
- `FileSystemModelRepository`: Uses `ModelsDirectory` property directly
- `DownloadManager`: Updated to use models directory without path manipulation
- `HuggingFaceDownloader`: Simplified path handling
- `SystemController`: API now exposes `ModelsDirectory` instead of `DataPath`

**Improved Logging:**
```
FileSystemModelRepository initialized with ModelsDirectory: C:\Users\xxx\.filer\models
```

### Benefits

1. **✅ Single Responsibility**: One property, one purpose - where models are stored
2. **✅ No Surprises**: Path behavior is predictable and documented
3. **✅ Clearer Intent**: `ModelsDirectory` vs ambiguous `DataPath`
4. **✅ Simpler Code**: Removed `GetModelsDirectory()` helper and conditional logic
5. **✅ Better Errors**: Clearer error messages about model paths

### Rationale

This is a **breaking change in 0.x**, but necessary for long-term maintainability:

- **Design Flaw**: The `DataPath` + auto-append pattern violated the principle of least surprise
- **User Confusion**: Users naturally expected `DataPath` to be the models directory
- **Code Complexity**: Conditional logic (`GetModelsDirectory()`) added unnecessary complexity
- **0.x Development**: Perfect time for breaking changes before 1.0 stabilization

## [0.2.2] - Earlier releases

See git history for earlier changes.
