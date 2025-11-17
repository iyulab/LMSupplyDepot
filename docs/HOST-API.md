# LMSupplyDepots Host API 가이드

LMSupplyDepots Host 애플리케이션의 REST API 사용법을 설명합니다.

## 🚀 시작하기

**서버 실행**: `LMSupplyDepots.HostApp` 프로젝트 실행
- HTTP: `http://localhost:5181`
- HTTPS: `https://localhost:5182`

## 📋 모델 관리

### 모델 목록 조회
**GET** `/api/models`
- 사용 가능한 모든 모델 목록 반환
- 쿼리 옵션: `?type=TextGeneration&search=qwen&loadedOnly=true`

### 로드된 모델만 조회
**GET** `/api/models/all-loaded`
- 현재 메모리에 로드된 모델만 반환

### 모델 상세 정보
**POST** `/api/show`
```json
{ "name": "모델ID_또는_별명" }
```

### 별명으로 모델 찾기
**GET** `/api/models/alias?alias=모델별명`

## 🔧 모델 로딩

### 모델 로드
**POST** `/api/models/load`
```json
{
  "model": "hf:unsloth/Qwen3-30B-A3B-GGUF/Qwen3-30B-A3B-UD-IQ1_S",
  "parameters": { "gpuLayers": 35 }
}
```

### 모델 언로드
**POST** `/api/models/unload`
```json
{ "model": "모델ID_또는_별명" }
```

### 모델 상태 확인
**GET** `/api/models/{modelKey}/status`
- 특정 모델의 런타임 상태 조회

**GET** `/api/models/status`
- 모든 모델의 상태 조회

## 🤖 텍스트 생성 (OpenAI 호환)

### 채팅 완료
**POST** `/v1/chat/completions`
```json
{
  "model": "모델ID_또는_별명",
  "messages": [
    { "role": "user", "content": "안녕하세요!" }
  ],
  "max_tokens": 256,
  "temperature": 0.7,
  "stream": false
}
```

### 스트리밍 응답
동일한 엔드포인트에서 `"stream": true` 설정

### 사용 가능한 모델 목록 (OpenAI 형식)
**GET** `/v1/models`

### 임베딩 생성 (OpenAI 호환)
**POST** `/v1/embeddings`
```json
{
  "model": "모델ID_또는_별명",
  "input": "텍스트 또는 문자열 배열",
  "encoding_format": "float"
}
```

## 📁 파일 관리

### 컬렉션 목록
**GET** `/api/collections`

### 컬렉션 검색
**GET** `/api/collections/discover`
- 사용 가능한 컬렉션 검색

### 컬렉션 정보
**GET** `/api/collections/info`
- 특정 컬렉션의 상세 정보

## 📥 다운로드 관리

### 다운로드 목록
**GET** `/api/downloads`
- 진행 중인 다운로드 목록 조회

### 다운로드 상태
**GET** `/api/downloads/status`
- 모든 다운로드의 상태 조회

### 다운로드 시작
**POST** `/api/downloads/start`
```json
{
  "model": "모델ID"
}
```

**모델 ID 형식 (HuggingFace)**:
- 전체 저장소: `hf:owner/repo`
- 특정 아티팩트: `hf:owner/repo/artifact-name`

**아티팩트 이름 규칙**:
- `.gguf` 확장자는 선택사항입니다 (자동으로 처리됨)
- ✅ 올바른 형식: `hf:bartowski/Phi-4-GGUF/Phi-4-Q2_K` (확장자 없음)
- ✅ 올바른 형식: `hf:bartowski/Phi-4-GGUF/Phi-4-Q2_K.gguf` (확장자 포함)
- 두 형식 모두 동일하게 처리됩니다

**예시**:
```json
// 확장자 없이
{ "model": "hf:bartowski/microsoft_Phi-4-mini-instruct-GGUF/Phi-4-mini-instruct-Q2_K" }

// 확장자 포함 (v0.3.1+ 지원)
{ "model": "hf:bartowski/microsoft_Phi-4-mini-instruct-GGUF/Phi-4-mini-instruct-Q2_K.gguf" }
```

### 다운로드 일시정지
**POST** `/api/downloads/pause`
```json
{
  "model": "모델ID"
}
```

### 다운로드 재개
**POST** `/api/downloads/resume`
```json
{
  "model": "모델ID"
}
```

### 다운로드 취소
**POST** `/api/downloads/cancel`
```json
{
  "model": "모델ID"
}
```

## 🔧 시스템 관리

### 헬스 체크
**GET** `/api/health`
- 서비스 상태 및 버전 정보 조회

### 시스템 설정
**GET** `/api/config`
- 현재 시스템 설정 정보 조회

### 별명 설정
**PUT** `/api/alias`
```json
{
  "name": "긴_모델_ID",
  "alias": "짧은별명"
}
```

### 모델 삭제
**DELETE** `/api/delete`
```json
{ "name": "모델ID_또는_별명" }
```

## 📊 상태 확인

### 다운로드 여부 확인
**GET** `/api/models/downloaded?model=모델ID`

### 로드 여부 확인
**GET** `/api/models/is-loaded?model=모델ID`

## 🛠️ 디버그 및 개발

### 등록된 서비스 조회
**GET** `/api/debug/services`
- 현재 등록된 모든 서비스 목록 조회

## 💡 사용 팁

1. **별명 활용**: 긴 모델 ID 대신 짧은 별명을 설정하여 사용
2. **스트리밍**: 실시간 응답이 필요한 경우 `stream: true` 사용
3. **온도 조절**: 창의적 응답은 0.8~1.0, 정확한 답변은 0.1~0.3
4. **토큰 제한**: `max_tokens`로 응답 길이 제어
5. **모델 상태**: 추론 전 모델 로드 상태 확인 권장
6. **임베딩**: 텍스트 벡터화는 `/v1/embeddings` 엔드포인트 사용
7. **다운로드 관리**: 대용량 모델은 다운로드 API로 진행 상황 모니터링
8. **reasoning_tokens**: o1-style 모델 사용 시 추론 토큰 수 자동 추적

## ⚠️ 주의사항

- 대용량 모델 로딩 시 충분한 메모리(RAM) 확보 필요
- GPU 사용 시 `gpuLayers` 파라미터로 GPU 레이어 수 조절
- 동시 다중 모델 로딩 시 메모리 사용량 모니터링 권장
- 다운로드 중인 모델은 완료 후 로드 가능
- 임베딩 생성 시 모델이 임베딩을 지원하는지 확인 필요
- reasoning/thinking 기능은 호환 모델에서만 동작