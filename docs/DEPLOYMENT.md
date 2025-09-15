# LMSupplyDepots 배포 가이드

## 개요

이 문서는 LMSupplyDepots 플랫폼의 내부 빌드 및 배포 프로세스에 대한 완전한 가이드입니다.

## 배포 아키텍처

### 환경 구성
- **개발 환경**: Docker Compose 기반 로컬 개발
- **스테이징 환경**: 프로덕션과 유사한 환경에서의 테스트
- **프로덕션 환경**: 고가용성 Docker Swarm 또는 Kubernetes 배포

### 주요 컴포넌트
- **HostApp**: ASP.NET Core 웹 애플리케이션 (메인 서비스)
- **CLI**: 명령줄 도구 (`lmd`)
- **PostgreSQL**: 데이터베이스
- **Redis**: 캐싱 및 세션 관리
- **Nginx**: 로드 밸런서 및 리버스 프록시

## 빌드 프로세스

### 1. 로컬 빌드

```powershell
# 전체 빌드 및 패키징
.\scripts\build-packages.ps1 -Configuration Release -Pack -Publish

# 테스트 포함 빌드
.\scripts\build-packages.ps1 -Configuration Release -Pack -Publish

# 버전 지정 빌드
.\scripts\build-packages.ps1 -Configuration Release -VersionSuffix "alpha.1" -Pack
```

### 2. Docker 이미지 빌드

```bash
# HostApp 이미지 빌드
docker build -f docker/Dockerfile.hostapp -t lmsupplydepots/hostapp:latest .

# CLI 이미지 빌드
docker build -f docker/Dockerfile.cli -t lmsupplydepots/cli:latest .

# 모든 이미지 빌드 (docker-compose 사용)
docker-compose build
```

## CI/CD 파이프라인

### GitHub Actions 워크플로

1. **보안 스캔**: 취약점 검사 및 코드 분석
2. **빌드 및 테스트**: 솔루션 빌드 및 CI 테스트 실행
3. **Docker 이미지 빌드**: 컨테이너 이미지 생성 및 레지스트리 푸시
4. **배포**: 스테이징/프로덕션 환경 배포

### 트리거 조건
- `main` 브랜치 푸시 → 스테이징 배포
- `develop` 브랜치 푸시 → 스테이징 배포
- `v*` 태그 푸시 → 프로덕션 배포
- Pull Request → 빌드 및 테스트만 실행

## 배포 방법

### 1. 개발 환경 배포

```bash
# 환경 변수 설정
cp .env.example .env.development
# .env.development 파일을 편집하여 필요한 값 설정

# 개발 환경 시작
docker-compose up -d

# 로그 확인
docker-compose logs -f hostapp
```

### 2. 스테이징 배포

```powershell
# 스테이징 배포 (자동화 스크립트 사용)
.\scripts\deploy.ps1 -Environment staging -Version latest

# 드라이런 (실제 배포 없이 확인만)
.\scripts\deploy.ps1 -Environment staging -Version latest -DryRun
```

### 3. 프로덕션 배포

```powershell
# 프로덕션 배포
.\scripts\deploy.ps1 -Environment production -Version v1.0.0

# 헬스체크 건너뛰기 (긴급 배포시)
.\scripts\deploy.ps1 -Environment production -Version v1.0.0 -SkipHealthCheck

# 롤백
.\scripts\deploy.ps1 -Environment production -Rollback
```

## 환경 설정

### 필수 환경 변수

```bash
# 데이터베이스
DB_PASSWORD=strong-password-here

# AI 서비스 API 키
OPENAI_API_KEY=sk-...
HUGGINGFACE_API_TOKEN=hf_...

# 컨테이너 레지스트리
REGISTRY=ghcr.io/your-org/lmsupplydepots
VERSION=v1.0.0
```

### 환경별 설정 파일

- `.env.development` - 개발 환경
- `.env.staging` - 스테이징 환경
- `.env.production` - 프로덕션 환경

## 보안 고려사항

### 1. 시크릿 관리

```bash
# Docker Secrets 사용 (프로덕션)
echo "your-db-password" | docker secret create db_password -
echo "sk-your-openai-key" | docker secret create openai_api_key -
echo "hf_your-huggingface-token" | docker secret create huggingface_token -
```

### 2. 네트워크 보안

- 내부 서비스는 `lmsupplydepots-internal` 네트워크 사용
- 외부 접근은 `lmsupplydepots-public` 네트워크를 통해 Nginx만 노출
- SSL/TLS 인증서 구성 (Let's Encrypt 권장)

### 3. 컨테이너 보안

- 비루트 사용자로 컨테이너 실행
- 읽기 전용 루트 파일시스템
- 최소 권한 원칙 적용

## 모니터링 및 로깅

### 1. 헬스체크

```bash
# 애플리케이션 상태 확인
curl http://localhost:8080/health

# 서비스별 상태 확인
docker-compose ps
```

### 2. 로그 수집

```bash
# 모든 서비스 로그
docker-compose logs

# 특정 서비스 로그
docker-compose logs hostapp
docker-compose logs postgres

# 실시간 로그
docker-compose logs -f hostapp
```

### 3. 메트릭 수집 (Prometheus)

- **애플리케이션 메트릭**: `/metrics` 엔드포인트
- **시스템 메트릭**: Node Exporter
- **데이터베이스 메트릭**: PostgreSQL Exporter
- **컨테이너 메트릭**: cAdvisor

## 백업 및 복구

### 1. 자동 백업

```bash
# 데이터베이스 백업 실행
docker-compose exec backup /usr/local/bin/backup.sh

# 백업 확인
ls -la backups/
```

### 2. 수동 백업

```bash
# 데이터베이스 백업
docker-compose exec postgres pg_dump -U lmsupplydepots lmsupplydepots > backup.sql

# 볼륨 백업
docker run --rm -v lmsupplydepots_app-data:/data -v $(pwd):/backup alpine tar czf /backup/app-data.tar.gz -C /data .
```

### 3. 복구

```bash
# 데이터베이스 복구
docker-compose exec -T postgres psql -U lmsupplydepots lmsupplydepots < backup.sql

# 볼륨 복구
docker run --rm -v lmsupplydepots_app-data:/data -v $(pwd):/backup alpine tar xzf /backup/app-data.tar.gz -C /data
```

## 트러블슈팅

### 일반적인 문제들

#### 1. 컨테이너 시작 실패

```bash
# 컨테이너 로그 확인
docker-compose logs [service-name]

# 컨테이너 내부 접근
docker-compose exec [service-name] /bin/bash
```

#### 2. 데이터베이스 연결 오류

```bash
# PostgreSQL 상태 확인
docker-compose exec postgres pg_isready

# 연결 테스트
docker-compose exec postgres psql -U lmsupplydepots -d lmsupplydepots -c "SELECT 1;"
```

#### 3. 성능 문제

```bash
# 리소스 사용량 확인
docker stats

# 애플리케이션 메트릭 확인
curl http://localhost:8080/metrics | grep -E "(request_duration|memory_usage)"
```

### 로그 파일 위치

- **애플리케이션 로그**: `/app/logs/`
- **Nginx 로그**: `/var/log/nginx/`
- **PostgreSQL 로그**: `/var/lib/postgresql/data/log/`

## 성능 최적화

### 1. 리소스 할당

```yaml
# docker-compose.prod.yml에서 리소스 제한 설정
deploy:
  resources:
    limits:
      cpus: '2.0'
      memory: 4G
    reservations:
      cpus: '1.0'
      memory: 2G
```

### 2. 캐싱 최적화

- Redis 캐시 구성 확인
- CDN 사용 (정적 자산)
- 애플리케이션 수준 캐싱

### 3. 데이터베이스 최적화

- PostgreSQL 설정 튜닝
- 인덱스 최적화
- 쿼리 성능 분석

## 버전 관리

### 태그 전략

- `v1.0.0` - 프로덕션 릴리스
- `v1.0.0-rc.1` - 릴리스 후보
- `v1.0.0-beta.1` - 베타 버전
- `v1.0.0-alpha.1` - 알파 버전

### 롤백 절차

1. 이전 버전으로 이미지 태그 변경
2. 배포 스크립트 실행
3. 데이터베이스 마이그레이션 롤백 (필요시)
4. 헬스체크 및 검증

## 지원 및 문의

- **기술 문의**: DevOps 팀
- **긴급 상황**: 온콜 엔지니어
- **문서 업데이트**: 개발팀

---

**마지막 업데이트**: 2025년 9월 15일
**문서 버전**: 1.0
**작성자**: AI Assistant