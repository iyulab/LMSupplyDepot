# Tool Serving 사용 예시

## 기본 설정

```csharp
using LMSupplyDepots.SDK;
using LMSupplyDepots.SDK.ToolServing;
using LMSupplyDepots.SDK.ToolServing.Extensions;

// LMSupplyDepot with Tool Serving
var supplyDepot = new LMSupplyDepot(options =>
{
    options.ModelsPath = @"D:\models";
    options.EnableModelHub = true;
});

// Add tool serving capabilities
services.AddToolServing(options =>
{
    options.EnableHttpServer = true;
    options.HttpPort = 8080;
    options.EnableDiscovery = true;
    options.EnableMetrics = true;
    options.MaxConcurrentExecutions = 50;
    options.ExecutionTimeout = TimeSpan.FromMinutes(2);
    
    // Configure bridges
    options.Bridges.YoMo.Enabled = true;
    options.Bridges.YoMo.ZipperEndpoint = "localhost:9000";
    
    options.Bridges.MCP.Enabled = true;
    options.Bridges.MCP.ServerName = "lmsupplydepots-tools";
});
```

## 1. 직접 Tool 사용 (기존 방식)

```csharp
// 기존 SDK 방식 그대로 사용 가능
supplyDepot.RegisterTool(new GetWeatherTool());
supplyDepot.RegisterTool(new CalculatorTool());

var tools = supplyDepot.GetAvailableTools();
var result = await supplyDepot.ExecuteToolAsync("get_weather", 
    """{"location": "Seoul, Korea"}""");
```

## 2. HTTP API를 통한 Tool Serving

### Tool Discovery
```http
GET http://localhost:8080/v1/tools/discover
```

Response:
```json
{
  "tools": [
    {
      "name": "get_weather",
      "description": "Get current weather for a location",
      "parameters": {...},
      "endpoint": "/v1/tools/execute",
      "method": "POST",
      "capabilities": {
        "supports_streaming": false,
        "supports_async": true,
        "requires_auth": false
      },
      "metrics": {
        "execution_count": 42,
        "success_rate": 0.95,
        "average_execution_time": "00:00:01.234"
      }
    }
  ]
}
```

### Tool Execution
```http
POST http://localhost:8080/v1/tools/execute
Content-Type: application/json

{
  "tool_name": "get_weather",
  "arguments": "{\"location\": \"Seoul, Korea\"}",
  "request_id": "uuid-here",
  "metadata": {
    "user_id": "user123",
    "session_id": "session456"
  }
}
```

### Batch Execution
```http
POST http://localhost:8080/v1/tools/batch
Content-Type: application/json

{
  "requests": [
    {
      "tool_name": "get_weather",
      "arguments": "{\"location\": \"Seoul\"}"
    },
    {
      "tool_name": "calculator",
      "arguments": "{\"expression\": \"25 * 4\"}"
    }
  ]
}
```

## 3. YoMo 오케스트레이션 통합

### YoMo Serverless Function 등록
```csharp
var yomoBridge = serviceProvider.GetRequiredService<YoMoToolBridge>();

// Tool을 YoMo SFN으로 등록
var weatherTool = new GetWeatherTool();
var endpoint = await yomoBridge.RegisterServerlessFunctionAsync("weather-sfn", weatherTool);
// Returns: "/sfn/weather-sfn"
```

### YoMo Bridge Configuration
```yaml
# yomo.yml
name: lmsupplydepots-bridge
host: 0.0.0.0
port: 9000

bridge:
  ai:
    server:
      addr: 0.0.0.0:9000
      provider: openai
    providers:
      openai:
        api_key: ${OPENAI_API_KEY}
        model: gpt-4o
```

### YoMo Function Call
```bash
# YoMo 방식으로 LLM이 함수 호출
curl http://127.0.0.1:9000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gpt-4o",
    "messages": [
      {
        "role": "user", 
        "content": "What is the weather in Seoul?"
      }
    ]
  }'
```

## 4. LangChain 스타일 Tool Binding

```csharp
var tools = new[] { new GetWeatherTool(), new CalculatorTool() };
var toolBinding = await yomoBridge.CreateToolBindingAsync(tools);

// LangChain-style invocation
var result = await toolBinding.InvokeAsync("Check weather in Tokyo and calculate 25*4");
```

## 5. LlamaIndex 스타일 Agent Context

```csharp
var tools = new[] { new GetWeatherTool(), new CalculatorTool() };
var agentContext = await yomoBridge.CreateAgentContextAsync(tools);

// LlamaIndex-style agent run
var response = await agentContext.RunAsync(
    "What's the weather in Paris and what's 100 divided by 4?");
```

## 6. OpenAI Functions 호환성

```csharp
var openAIAdapter = serviceProvider.GetRequiredService<OpenAIFunctionsAdapter>();

// OpenAI Functions 형식으로 변환
var functions = await openAIAdapter.GetOpenAIFunctionsAsync();

// OpenAI API와 동일한 방식으로 사용
var functionCall = new OpenAIFunctionCall
{
    Name = "get_weather",
    Arguments = "{\"location\": \"New York\"}"
};

var result = await openAIAdapter.ExecuteFunctionCallAsync(functionCall);
```

## 7. Model Context Protocol (MCP) 지원

```csharp
var mcpServer = serviceProvider.GetRequiredService<MCPToolServer>();

// MCP 형식으로 도구 목록 제공
var mcpTools = await mcpServer.ListToolsAsync();

// MCP 도구 호출
var mcpCall = new MCPToolCall
{
    Method = "get_weather",
    Params = new Dictionary<string, object>
    {
        ["location"] = "London"
    }
};

var mcpResult = await mcpServer.CallToolAsync(mcpCall);
```

## 8. 커스텀 Tool 구현

```csharp
public class DatabaseTool : FunctionToolBase
{
    private readonly IDbContext _dbContext;

    public DatabaseTool(IDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override FunctionDefinition Definition => new()
    {
        Name = "query_database",
        Description = "Execute SQL queries on the database",
        Parameters = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["query"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "SQL query to execute"
                }
            },
            ["required"] = new[] { "query" }
        }
    };

    public override async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = DeserializeArguments<QueryArgs>(argumentsJson);
        
        // Execute SQL query
        var results = await _dbContext.ExecuteQueryAsync(args.Query, cancellationToken);
        
        return JsonSerializer.Serialize(new
        {
            query = args.Query,
            results = results,
            count = results.Count
        });
    }

    private class QueryArgs
    {
        public string Query { get; set; } = string.Empty;
    }
}

// 등록
services.AddTool<DatabaseTool>();
```

## 9. Tool Metrics 및 Monitoring

```csharp
var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();

// 도구별 메트릭 조회
var weatherTool = await toolRegistry.GetToolDescriptorAsync("get_weather");
Console.WriteLine($"Weather tool executed {weatherTool.Metrics.ExecutionCount} times");
Console.WriteLine($"Success rate: {weatherTool.Metrics.SuccessCount / (double)weatherTool.Metrics.ExecutionCount:P}");
Console.WriteLine($"Average execution time: {weatherTool.Metrics.AverageExecutionTime}");

// 필터링된 도구 검색
var authRequiredTools = await toolRegistry.DiscoverToolsAsync(new ToolFilter
{
    RequiresAuth = true,
    SupportsStreaming = true
});
```

## 10. 실제 프로덕션 사용 사례

### 마이크로서비스 아키텍처에서 Tool Gateway로 사용
```
[LLM 오케스트레이터] → [LMSupplyDepots Tool Gateway] → [실제 서비스들]
     ↓                            ↓                           ↓
   LangChain                  Tool Registry              Database Service
   LlamaIndex               Tool Execution Engine        Email Service  
   Custom Agent                Tool Metrics              Weather API
                                                        Calculator Service
```

### Multi-Model Support
```csharp
// 여러 모델이 동일한 도구들을 공유하여 사용
var model1 = await supplyDepot.LoadModelAsync("llama-3-8b");
var model2 = await supplyDepot.LoadModelAsync("qwen-7b");

// 두 모델 모두 동일한 도구 세트에 접근 가능
var tools = supplyDepot.GetAvailableTools(); // 공통 도구 세트

// OpenAI 호환 API로 각 모델 사용
await supplyDepot.CreateChatCompletionAsync(new OpenAIChatCompletionRequest
{
    Model = "llama-3-8b",
    Messages = [...],
    Tools = tools
});
```

이러한 아키텍처를 통해 LMSupplyDepots는 단순한 모델 서버에서 벗어나 **포괄적인 AI 도구 플랫폼**으로 발전할 수 있습니다.
