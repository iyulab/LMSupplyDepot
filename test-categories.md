# Test Categories Guide

본 문서는 LMSupplyDepots 프로젝트의 테스트 분류 기준과 실행 방법을 설명합니다.

## 테스트 분류

### CI/CD에서 실행 가능한 테스트

#### `Unit`
- **설명**: 단위 테스트, 외부 의존성 없이 실행 가능
- **특징**: 빠르고 안정적, Mock 사용
- **예시**:
```csharp
[Test]
[Category("Unit")]
public void CalculateTokenCount_ShouldReturnCorrectCount()
{
    // Pure unit test logic
}
```

#### `MockTests`
- **설명**: Mock 객체를 사용한 통합 테스트
- **특징**: 실제 외부 서비스 대신 Mock 사용
- **예시**:
```csharp
[Test]
[Category("MockTests")]
public void OpenAIClient_ShouldCallCorrectEndpoint()
{
    var mockClient = new Mock<IOpenAIClient>();
    // Mock setup and test
}
```

#### `ConfigurationTests`
- **설명**: 설정 로드 및 검증 테스트
- **특징**: 설정 파일 파싱, 유효성 검사
- **예시**:
```csharp
[Test]
[Category("ConfigurationTests")]
public void Configuration_ShouldLoadCorrectly()
{
    // Configuration loading test
}
```

### 로컬에서만 실행 가능한 테스트

#### `RequiresModel`
- **설명**: 실제 AI 모델이 필요한 테스트
- **요구사항**:
  - 모델 파일 다운로드 (수GB 크기)
  - 충분한 메모리 및 저장 공간
- **예시**:
```csharp
[Test]
[Category("RequiresModel")]
public void LlamaModel_ShouldGenerateText()
{
    var model = ModelLoader.LoadModel("tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf");
    var result = model.Generate("Hello");
    Assert.IsNotEmpty(result);
}
```

#### `RequiresApiKey`
- **설명**: 실제 API 키가 필요한 테스트
- **요구사항**:
  - `OPENAI_API_KEY` 환경 변수
  - `HUGGINGFACE_API_TOKEN` 환경 변수
- **예시**:
```csharp
[Test]
[Category("RequiresApiKey")]
public void OpenAI_ShouldCallRealAPI()
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    Assume.That(apiKey, Is.Not.Null, "OPENAI_API_KEY required");

    var client = new OpenAIClient(apiKey);
    // Real API call test
}
```

#### `RequiresNetwork`
- **설명**: 네트워크 연결이 필요한 테스트
- **특징**: 실제 HTTP 요청, 모델 다운로드
- **예시**:
```csharp
[Test]
[Category("RequiresNetwork")]
public void ModelHub_ShouldDownloadModel()
{
    var downloader = new ModelDownloader();
    var result = await downloader.DownloadAsync("microsoft/DialoGPT-small");
    Assert.IsTrue(result.Success);
}
```

#### `RequiresLargeMemory`
- **설명**: 대용량 메모리가 필요한 테스트
- **요구사항**: 8GB+ RAM
- **특징**: 대형 모델 로드, 배치 처리
- **예시**:
```csharp
[Test]
[Category("RequiresLargeMemory")]
public void LargeModel_ShouldLoadInMemory()
{
    // 4GB+ 모델 로드 테스트
}
```

#### `RequiresGpu`
- **설명**: GPU가 필요한 테스트
- **요구사항**: CUDA 호환 GPU
- **특징**: GPU 가속 추론 테스트
- **예시**:
```csharp
[Test]
[Category("RequiresGpu")]
public void CudaInference_ShouldUseGpu()
{
    Assume.That(CudaHelper.IsAvailable(), "CUDA GPU required");
    // GPU inference test
}
```

#### `Integration`
- **설명**: 실제 시스템 통합 테스트
- **특징**: 여러 컴포넌트 연동, E2E 시나리오
- **예시**:
```csharp
[Test]
[Category("Integration")]
public void Pipeline_ShouldProcessCompleteWorkflow()
{
    // 전체 워크플로우 통합 테스트
}
```

## 테스트 실행 방법

### 로컬 환경에서 전체 테스트 실행

#### Windows (PowerShell)
```powershell
.\scripts\test-local.ps1
```

#### Linux/macOS (Bash)
```bash
./scripts/test-local.sh
```

#### 옵션
- `--filter="TestName"`: 특정 테스트만 실행
- `--verbose`: 상세 출력
- `--skip-model-download`: 모델 다운로드 건너뛰기

### CI/CD 환경에서 테스트 실행

#### PowerShell
```powershell
.\scripts\test-ci.ps1
```

#### 직접 실행
```bash
dotnet test --filter "Category!=RequiresModel&Category!=RequiresApiKey&Category!=RequiresNetwork&Category!=RequiresLargeMemory&Category!=RequiresGpu&Category!=LocalOnly"
```

### 특정 카테고리만 실행

```bash
# 단위 테스트만 실행
dotnet test --filter "Category=Unit"

# API 키가 필요한 테스트만 실행
dotnet test --filter "Category=RequiresApiKey"

# 여러 카테고리 실행
dotnet test --filter "Category=Unit|Category=MockTests"
```

## 환경 설정

### 필수 환경 변수

```bash
# OpenAI API 테스트용
export OPENAI_API_KEY="your-openai-api-key"

# HuggingFace API 테스트용
export HUGGINGFACE_API_TOKEN="your-huggingface-token"

# 테스트 환경 설정
export ASPNETCORE_ENVIRONMENT="Test"
export DOTNET_ENVIRONMENT="Test"
```

### 모델 저장 경로

기본적으로 모델은 다음 경로에 저장됩니다:
- Windows: `D:\data\LMSupplyDepot\models`
- Linux/macOS: `./models`

환경 변수로 변경 가능:
```bash
export LM_SUPPLY_DEPOT_MODEL_PATH="/custom/model/path"
```

## CI/CD 파이프라인 통합

### GitHub Actions

```yaml
name: CI Tests
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Run CI Tests
        run: ./scripts/test-ci.ps1

      - name: Upload test results
        uses: actions/upload-artifact@v3
        if: always()
        with:
          name: test-results
          path: TestResults/
```

### Azure DevOps

```yaml
trigger:
- main

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: UseDotNet@2
  inputs:
    version: '9.0.x'

- script: dotnet restore
  displayName: 'Restore packages'

- script: |
    chmod +x ./scripts/test-ci.ps1
    ./scripts/test-ci.ps1
  displayName: 'Run CI Tests'

- task: PublishTestResults@2
  inputs:
    testResultsFormat: 'VSTest'
    testResultsFiles: '**/*.trx'
    searchFolder: 'TestResults'
```

## 로컬 개발 워크플로우

1. **개발 시**: 단위 테스트 위주로 실행
   ```bash
   dotnet test --filter "Category=Unit"
   ```

2. **커밋 전**: CI 테스트 실행
   ```bash
   .\scripts\test-ci.ps1
   ```

3. **릴리스 전**: 전체 로컬 테스트 실행
   ```bash
   .\scripts\test-local.ps1
   ```

4. **성능 테스트**: GPU/대용량 메모리 테스트
   ```bash
   dotnet test --filter "Category=RequiresGpu|Category=RequiresLargeMemory"
   ```