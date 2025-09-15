# OpenAI SDK 마이그레이션 가이드 v2.2.0 → v2.4.0

## 개요

이 가이드는 LMSupplyDepots 프로젝트의 OpenAI SDK를 v2.2.0에서 v2.4.0으로 업그레이드할 때 필요한 변경 사항을 설명합니다.

## 주요 변경 사항

### 1. VectorStore API 변경

#### 제거된 타입들

| v2.2.0 (이전) | v2.4.0 (현재) | 설명 |
|---------------|---------------|------|
| `VectorStoreFileAssociation` | `VectorStoreFile` | 벡터 스토어 파일 연결 정보 |
| `VectorStoreBatchFileJob` | 제거됨 | 배치 파일 작업은 다른 방식으로 처리 |

#### 메서드 이름 변경

| v2.2.0 (이전) | v2.4.0 (현재) |
|---------------|---------------|
| `AddFileToStoreAsync(storeId, fileId)` | `CreateFileAsync(storeId, fileId)` |
| `RemoveFileFromStoreAsync(storeId, fileId)` | `DeleteFileAsync(storeId, fileId)` |
| `GetFilesInStoreAsync(storeId)` | `GetFilesAsync(storeId)` |

#### 메서드 시그니처 변경

**이전 (v2.2.0)**:
```csharp
public async Task<VectorStoreFileAssociation> AddFileToStoreAsync(
    string vectorStoreId,
    string fileId,
    bool waitUntilCompleted = false)
{
    var result = await _vectorStoreClient.AddFileToStoreAsync(
        vectorStoreId,
        fileId,
        waitUntilCompleted: waitUntilCompleted);
    return result.Value;
}
```

**현재 (v2.4.0)**:
```csharp
public async Task<VectorStoreFile> AddFileToVectorStoreAsync(
    string vectorStoreId,
    string fileId)
{
    var result = await _vectorStoreClient.CreateFileAsync(vectorStoreId, fileId);
    return result.Value;
}
```

### 2. 변경된 기능들

#### 파일 추가 기능
```csharp
// 이전
var association = await vectorStoreAPI.AddFileToStoreAsync(storeId, fileId, true);

// 현재
var vectorStoreFile = await vectorStoreAPI.AddFileToVectorStoreAsync(storeId, fileId);
```

#### 파일 제거 기능
```csharp
// 이전
var result = await vectorStoreAPI.RemoveFileFromStoreAsync(storeId, fileId);

// 현재
var result = await vectorStoreAPI.RemoveFileFromVectorStoreAsync(storeId, fileId);
```

#### 파일 목록 조회
```csharp
// 이전
var files = await vectorStoreAPI.GetFilesInStoreAsync(storeId);

// 현재
var files = await vectorStoreAPI.GetVectorStoreFilesAsync(storeId);
```

### 3. 배치 작업 처리

#### 이전 방식
```csharp
// VectorStoreBatchFileJob을 사용한 배치 처리
var batchJob = await vectorStoreAPI.CreateBatchFileJobAsync(storeId, fileIds);
var status = await vectorStoreAPI.GetBatchJobStatusAsync(batchJob.Id);
```

#### 현재 방식
```csharp
// 개별 파일 처리로 변경 (배치 작업은 클라이언트에서 직접 관리)
var tasks = fileIds.Select(fileId =>
    vectorStoreAPI.AddFileToVectorStoreAsync(storeId, fileId));
var results = await Task.WhenAll(tasks);
```

## 마이그레이션 체크리스트

### ✅ 완료된 작업

- [x] **Directory.Packages.props 업데이트**: OpenAI SDK 버전을 2.4.0으로 업데이트
- [x] **VectorStoreAPI.cs 수정**: 새로운 메서드 이름과 반환 타입 적용
- [x] **보안 취약점 해결**: Newtonsoft.Json 버전 충돌 문제 해결
- [x] **빌드 오류 수정**: 컴파일러 오류 모두 해결

### 🔄 추가 확인 필요

- [ ] **테스트 케이스 업데이트**: VectorStore 관련 테스트의 예상 결과 확인
- [ ] **문서화**: API 문서의 메서드 시그니처 업데이트
- [ ] **성능 테스트**: 배치 작업 성능 비교 (순차 처리 vs 병렬 처리)

## 주의사항

### 1. 배치 작업 성능
- 이전 버전의 배치 API가 제거되어 개별 파일 처리로 변경됨
- 대량의 파일을 처리할 때는 적절한 동시성 제어 필요
- `SemaphoreSlim`을 사용하여 동시 요청 수 제한 권장

### 2. 에러 처리
- 새로운 API의 에러 응답 형식 확인 필요
- 개별 파일 처리 시 일부 실패에 대한 복원력 있는 처리 구현

### 3. Rate Limiting
- OpenAI API의 rate limit 고려
- 배치 작업을 개별 요청으로 처리할 때 요청 간격 조절

## 예제 코드

### 완전한 마이그레이션 예제

```csharp
// 이전 버전 (v2.2.0)
public async Task<bool> MigrateVectorStoreOld(string storeId, IEnumerable<string> fileIds)
{
    try
    {
        var batchJob = await _vectorStoreClient.CreateBatchFileJobAsync(storeId, fileIds, waitUntilCompleted: true);
        return batchJob.Status == "completed";
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to migrate vector store");
        return false;
    }
}

// 현재 버전 (v2.4.0)
public async Task<bool> MigrateVectorStoreNew(string storeId, IEnumerable<string> fileIds)
{
    const int maxConcurrentRequests = 5;
    using var semaphore = new SemaphoreSlim(maxConcurrentRequests);

    var tasks = fileIds.Select(async fileId =>
    {
        await semaphore.WaitAsync();
        try
        {
            var result = await _vectorStoreClient.CreateFileAsync(storeId, fileId);
            return new { FileId = fileId, Success = true, File = result.Value };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add file {FileId} to vector store {StoreId}", fileId, storeId);
            return new { FileId = fileId, Success = false, File = (VectorStoreFile?)null };
        }
        finally
        {
            semaphore.Release();
        }
    });

    var results = await Task.WhenAll(tasks);
    var successCount = results.Count(r => r.Success);

    _logger.LogInformation(
        "Vector store migration completed. {SuccessCount}/{TotalCount} files processed successfully",
        successCount, results.Length);

    return successCount == results.Length;
}
```

## 테스트 방법

### 1. 단위 테스트 실행
```bash
# VectorStore 관련 테스트만 실행
dotnet test --filter "Category=Unit&FullyQualifiedName~VectorStore"
```

### 2. 통합 테스트 (API 키 필요)
```bash
# API 키가 설정된 환경에서 실행
export OPENAI_API_KEY="your-api-key"
dotnet test --filter "Category=RequiresApiKey&FullyQualifiedName~VectorStore"
```

### 3. 로컬 테스트 스크립트
```powershell
# 전체 로컬 테스트 실행
.\scripts\test-local.ps1 --filter="*VectorStore*" --verbose
```

## 추가 참고 자료

- [OpenAI .NET SDK v2.4.0 Release Notes](https://github.com/openai/openai-dotnet/releases/tag/v2.4.0)
- [OpenAI API 문서 - Vector Stores](https://platform.openai.com/docs/api-reference/vector-stores)
- [LMSupplyDepots 테스트 카테고리 가이드](../test-categories.md)

## 문의사항

마이그레이션 과정에서 문제가 발생하면 다음을 확인해보세요:

1. **빌드 오류**: `dotnet build`로 컴파일 오류 확인
2. **패키지 충돌**: `dotnet list package --outdated` 및 `--vulnerable` 옵션 확인
3. **테스트 실패**: 관련 테스트 케이스의 예상 결과 업데이트 필요 여부 확인

---

**마이그레이션 완료일**: 2025년 9월 15일
**담당자**: AI Assistant
**검토 상태**: ✅ 완료