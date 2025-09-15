#!/bin/bash
# LMSupplyDepots Local Test Script (Linux/macOS)
# This script runs tests that require local resources (models, API keys, etc.)
# and cannot be executed in CI/CD environments

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Parse arguments
FILTER="*"
VERBOSE=false
SKIP_MODEL_DOWNLOAD=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --filter=*)
            FILTER="${1#*=}"
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        --skip-model-download)
            SKIP_MODEL_DOWNLOAD=true
            shift
            ;;
        *)
            echo "Unknown parameter: $1"
            exit 1
            ;;
    esac
done

echo -e "${GREEN}🧪 LMSupplyDepots Local Test Runner${NC}"
echo -e "${GREEN}==================================${NC}"

# Set test configuration
export ASPNETCORE_ENVIRONMENT="Test"
export DOTNET_ENVIRONMENT="Test"

# Test categories that require local resources
LOCAL_TEST_CATEGORIES=(
    "RequiresModel"
    "RequiresApiKey"
    "RequiresNetwork"
    "RequiresLargeMemory"
    "RequiresGpu"
    "Integration"
)

echo -e "${YELLOW}📋 Running tests in the following categories:${NC}"
for category in "${LOCAL_TEST_CATEGORIES[@]}"; do
    echo -e "  - ${category}"
done
echo ""

# Check prerequisites
echo -e "${CYAN}🔍 Checking prerequisites...${NC}"

# Check for required environment variables
REQUIRED_ENV_VARS=(
    "OPENAI_API_KEY"
    "HUGGINGFACE_API_TOKEN"
)

MISSING_ENV_VARS=()
for env_var in "${REQUIRED_ENV_VARS[@]}"; do
    if [[ -z "${!env_var}" ]]; then
        MISSING_ENV_VARS+=("$env_var")
    fi
done

if [[ ${#MISSING_ENV_VARS[@]} -ne 0 ]]; then
    echo -e "${YELLOW}⚠️  Missing environment variables:${NC}"
    for missing in "${MISSING_ENV_VARS[@]}"; do
        echo -e "  - ${RED}$missing${NC}"
    done
    echo -e "${YELLOW}  Tests requiring API keys will be skipped${NC}"
    echo ""
fi

# Check model directory
MODEL_DIR="$(pwd)/models"
if [[ ! -d "$MODEL_DIR" ]]; then
    echo -e "${CYAN}📁 Creating model directory: $MODEL_DIR${NC}"
    mkdir -p "$MODEL_DIR"
fi

# Download test models if not skipped
if [[ "$SKIP_MODEL_DOWNLOAD" == false ]]; then
    echo -e "${CYAN}📦 Checking for test models...${NC}"

    MODEL_NAME="TinyLlama-1.1B-Chat-v1.0.Q4_K_M.gguf"
    MODEL_URL="https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/resolve/main/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf"
    MODEL_PATH="$MODEL_DIR/$MODEL_NAME"

    if [[ ! -f "$MODEL_PATH" ]]; then
        echo -e "  ${YELLOW}📥 Downloading $MODEL_NAME (669MB)...${NC}"
        echo -e "     This may take several minutes..."
        if wget -O "$MODEL_PATH" "$MODEL_URL" 2>/dev/null; then
            echo -e "  ${GREEN}✅ Downloaded successfully${NC}"
        else
            echo -e "  ${RED}❌ Download failed${NC}"
            echo -e "     ${YELLOW}Model-dependent tests will be skipped${NC}"
            rm -f "$MODEL_PATH" 2>/dev/null || true
        fi
    else
        echo -e "  ${GREEN}✅ $MODEL_NAME already exists${NC}"
    fi
else
    echo -e "${YELLOW}⏭️  Model download skipped${NC}"
fi

echo ""

# Build solution first
echo -e "${CYAN}🔨 Building solution...${NC}"
if dotnet build --configuration Release --no-restore; then
    echo -e "${GREEN}✅ Build successful${NC}"
else
    echo -e "${RED}❌ Build failed${NC}"
    exit 1
fi
echo ""

# Run local tests by category
echo -e "${CYAN}🧪 Running local tests...${NC}"

TEST_RESULTS=()
PASSED_COUNT=0
FAILED_COUNT=0

for category in "${LOCAL_TEST_CATEGORIES[@]}"; do
    echo -e "  ${YELLOW}🔍 Testing category: $category${NC}"

    CATEGORY_FILTER="Category=$category"
    if [[ "$FILTER" != "*" ]]; then
        CATEGORY_FILTER="$CATEGORY_FILTER&$FILTER"
    fi

    TEST_COMMAND="dotnet test --configuration Release --no-build --logger \"console;verbosity=normal\" --filter \"$CATEGORY_FILTER\""

    if [[ "$VERBOSE" == true ]]; then
        echo -e "    Command: $TEST_COMMAND"
    fi

    if eval "$TEST_COMMAND"; then
        echo -e "    ${GREEN}✅ $category tests passed${NC}"
        ((PASSED_COUNT++))
        TEST_RESULTS+=("$category:PASS")
    else
        echo -e "    ${RED}❌ $category tests failed${NC}"
        ((FAILED_COUNT++))
        TEST_RESULTS+=("$category:FAIL")
    fi
done

echo ""

# Generate test report
echo -e "${GREEN}📊 Test Results Summary${NC}"
echo -e "${GREEN}======================${NC}"

TOTAL_COUNT=$((PASSED_COUNT + FAILED_COUNT))

echo -e "${CYAN}Total Categories: $TOTAL_COUNT${NC}"
echo -e "${GREEN}Passed: $PASSED_COUNT${NC}"
echo -e "${RED}Failed: $FAILED_COUNT${NC}"

if [[ $FAILED_COUNT -eq 0 ]]; then
    echo ""
    echo -e "${GREEN}🎉 All local tests passed!${NC}"
    exit 0
else
    echo ""
    echo -e "${RED}❌ Some tests failed. Check the output above for details.${NC}"
    echo -e "${YELLOW}Failed categories:${NC}"
    for result in "${TEST_RESULTS[@]}"; do
        if [[ "$result" == *":FAIL" ]]; then
            category="${result%:*}"
            echo -e "  - ${RED}$category${NC}"
        fi
    done
    exit 1
fi