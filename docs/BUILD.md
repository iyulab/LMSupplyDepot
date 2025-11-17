# Build and Test Guide

## Prerequisites

- **.NET 10.0 SDK** or later
- **Visual Studio 2022** or **Rider** (optional, for IDE support)
- **Git** for version control

## Building the Solution

### Quick Build
```bash
cd src
dotnet build LMSupplyDepots.sln --configuration Release
```

### Clean Build
```bash
cd src
dotnet clean LMSupplyDepots.sln
dotnet restore LMSupplyDepots.sln
dotnet build LMSupplyDepots.sln --configuration Release
```

### Build Specific Project
```bash
cd src
dotnet build LMSupplyDepots.Host/LMSupplyDepots.Host.csproj --configuration Release
```

## Testing Strategy

### Test Categories

LMSupplyDepots uses a three-tier test categorization strategy optimized for both local development and CICD pipelines:

| Category | Description | External Dependencies | Speed | CICD Execution |
|----------|-------------|----------------------|-------|----------------|
| **Unit** | Fast, isolated tests with mocks/stubs | None | < 100ms | ✅ Yes |
| **Integration** | Component integration with local resources | Local only (filesystem, in-memory DB) | < 5s | ✅ Yes |
| **LocalIntegration** | End-to-end tests with real external APIs | External APIs (HuggingFace, etc.) | > 5s | ❌ No (local only) |

### Running Tests

#### CICD Tests (Fast - Recommended for Pre-Commit)
Runs **Unit** and **Integration** tests only, excluding slow LocalIntegration tests:

```powershell
# PowerShell
.\scripts\run-cicd-tests.ps1

# Or directly with dotnet
cd src
dotnet test LMSupplyDepots.sln --configuration Release --filter "Category!=LocalIntegration"
```

**When to use:**
- Before committing code
- During pull request reviews
- In CICD pipelines
- Quick validation during development

#### All Tests (Including LocalIntegration)
Runs **all tests** including real API integration tests:

```powershell
# PowerShell
.\scripts\run-all-tests.ps1

# Or directly with dotnet
cd src
dotnet test LMSupplyDepots.sln --configuration Release
```

**When to use:**
- Before releasing new versions
- Testing HuggingFace integration changes
- Comprehensive validation
- Manual QA testing

**Requirements:**
- Optional: `HUGGINGFACE_API_TOKEN` environment variable for private model testing
- Network connectivity for external API calls

#### Running Specific Test Categories

**Unit tests only:**
```bash
dotnet test --filter "Category=Unit"
```

**Integration tests only:**
```bash
dotnet test --filter "Category=Integration"
```

**LocalIntegration tests only:**
```bash
dotnet test --filter "Category=LocalIntegration"
```

**Specific test class:**
```bash
dotnet test --filter "FullyQualifiedName~HuggingFaceHelperExtensionTests"
```

### Test Organization

```
src/
├── LMSupplyDepots.External.HuggingFace.Tests/   # Unit tests for External library
├── LMSupplyDepots.External.LLamaEngine.Tests/   # Unit tests for LLama integration
└── LMSupplyDepots.Host.Tests/                   # Host application tests
    ├── Controllers/                             # API controller tests
    ├── Services/                                # Service layer tests
    └── HuggingFace/
        ├── HuggingFaceHelperExtensionTests.cs          # [Unit] Helper method tests
        └── HuggingFaceDownloaderIntegrationTests.cs    # [LocalIntegration] Real API tests
```

### Writing New Tests

#### Unit Test Example
```csharp
[Fact]
[Trait("Category", "Unit")]
public void MyMethod_ValidInput_ReturnsExpectedResult()
{
    // Arrange
    var input = "test";

    // Act
    var result = MyClass.MyMethod(input);

    // Assert
    Assert.Equal("expected", result);
}
```

#### LocalIntegration Test Example
```csharp
[Fact]
[Trait("Category", "LocalIntegration")]
[Trait("Priority", "High")]
public async Task DownloadModel_WithRealAPI_DownloadsSuccessfully()
{
    // Arrange
    var downloader = CreateRealDownloader();

    // Act
    var result = await downloader.DownloadModelAsync(...);

    // Assert
    Assert.NotNull(result);
}
```

### Test Guidelines

1. **Categorize all tests**: Use `[Trait("Category", "...")]` attribute
2. **Keep Unit tests fast**: < 100ms per test
3. **Use descriptive names**: `MethodName_Scenario_ExpectedOutcome`
4. **Arrange-Act-Assert pattern**: Organize test code clearly
5. **Clean up resources**: Implement `IDisposable` for integration tests
6. **No flaky tests**: Tests must be deterministic and reliable
7. **Document complex tests**: Add XML comments explaining test purpose

## CICD Integration

### GitHub Actions Workflow

The `.github/workflows/build-test.yml` workflow automatically:
1. Builds the solution
2. Runs CICD tests (Unit + Integration)
3. Excludes LocalIntegration tests (external API calls)

```yaml
# Excerpt from build-test.yml
- name: Run tests
  run: |
    dotnet test src/LMSupplyDepots.sln \
      --configuration Release \
      --no-build \
      --filter "Category!=LocalIntegration"
```

### Adding Test Workflows

To add a new test workflow:
1. Create test class with appropriate `[Trait("Category", "...")]`
2. Tests run automatically in CICD if not marked `LocalIntegration`
3. No workflow changes needed (automatic discovery)

## Troubleshooting

### Build Failures

**Issue**: Missing .NET SDK
```
Solution: Install .NET 10.0 SDK from https://dotnet.microsoft.com/download
```

**Issue**: NuGet restore failures
```bash
# Clear NuGet cache
dotnet nuget locals all --clear
dotnet restore src/LMSupplyDepots.sln --force
```

### Test Failures

**Issue**: LocalIntegration tests fail with 401 Unauthorized
```
Solution: Set HUGGINGFACE_API_TOKEN environment variable or use public models only
```

**Issue**: Test discovery fails
```bash
# Rebuild test projects
dotnet clean src/LMSupplyDepots.sln
dotnet build src/LMSupplyDepots.sln --configuration Release
```

**Issue**: Tests timeout
```
Solution: LocalIntegration tests may take several minutes for large model downloads
```

## Performance Benchmarks

### Build Times (Release, clean build)
- Full solution: ~30-40s
- Single project: ~5-10s

### Test Execution Times
- CICD tests (Unit + Integration): ~5-15s
- All tests (including LocalIntegration): ~2-10 minutes (depends on network)

## Next Steps

- [Development Tasks](TASKS.md) - Current development status
- [Host API Guide](HOST-API.md) - REST API documentation
- [Contributing](../README.md#contributing) - How to contribute
