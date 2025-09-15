# LMSupplyDepot API Proxy

Filer 호스트 애플리케이션에서 관리하는 LMSupplyDepot 프로세스에 대한 프록시 API입니다.

## 개요

이 API는 로컬에서 실행되는 LMSupplyDepot 서비스를 `/lm/*` 경로를 통해 프록시합니다. LMSystemService가 LMSupplyDepot 프로세스의 생명주기를 관리하고, ProxyManagerService가 YARP 리버스 프록시를 통해 요청을 전달합니다.

## 지원 경로

### `/lm/api/*` - 기본 API 엔드포인트
- **소스**: `/api/*` 경로로 프록시
- **용도**: LMSupplyDepot의 기본 API 엔드포인트

### `/lm/v1/*` - v1 표준 엔드포인트  
- **소스**: `/v1/*` 경로로 프록시
- **용도**: LMSupplyDepot의 v1 표준 API 엔드포인트

## 프록시 동작

### 자동 서비스 관리
- LMSystemService가 LMSupplyDepot 프로세스를 자동으로 시작/중지
- 사용 가능한 포트를 자동으로 찾아서 할당
- HTTP/HTTPS 포트 지원 (HTTP 우선 사용)

### 이벤트 기반 프록시 업데이트
- 서비스 상태 변경 시 즉시 프록시 설정 업데이트
- 60초마다 상태 검증 및 동기화
- 스마트 변경 감지로 필요시에만 설정 갱신

### 헬스체크
- 프록시 대상 서비스의 `/health` 엔드포인트 모니터링
- 30초 간격으로 자동 헬스체크 수행
- 실패 시 자동 복구 시도

## 사용 예시

```bash
# 모델 목록 조회 (기본 API)
GET http://localhost:PORT/lm/api/models

# 채팅 완성 (v1 표준)
POST http://localhost:PORT/lm/v1/chat/completions
Content-Type: application/json

{
    "model": "model-name",
    "messages": [
        {"role": "user", "content": "Hello"}
    ]
}
```

## 구성 요소

### LMSystemService
- LMSupplyDepot 프로세스 생명주기 관리
- 포트 자동 할당 및 프로세스 시작/중지
- 서비스 상태 모니터링 및 이벤트 발생

### ProxyManagerService
- YARP 기반 리버스 프록시 구현
- 이벤트 기반 실시간 프록시 설정 업데이트
- 헬스체크 및 장애 복구

## 상태 관리

프록시는 다음 상태에 따라 동작합니다:
- **실행 중**: LMSupplyDepot 프로세스가 정상 동작하며 프록시 활성화
- **중지됨**: 프로세스가 중지된 상태로 빈 클러스터 구성
- **오류**: 프로세스 오류 시 자동 재시작 시도

서비스 상태는 실시간으로 모니터링되며, 변경 시 즉시 프록시 설정이 업데이트됩니다.