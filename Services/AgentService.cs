using System.Diagnostics;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using PromptAgent.Models;

namespace PromptAgent.Services;

/// <summary>
/// AI 服務設定
/// </summary>
public class AzureOpenAISettings
{
    public string Provider { get; set; } = "Azure"; // "Azure" or "OpenAI"
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty; // For OpenAI, this is the Model ID
    public string EvaluatorEndpoint { get; set; } = string.Empty;
    public string EvaluatorApiKey { get; set; } = string.Empty;
    public string EvaluatorDeploymentName { get; set; } = string.Empty;
    // Code Advisor (Codex) 設定
    public string CodeAdvisorEndpoint { get; set; } = string.Empty;
    public string CodeAdvisorApiKey { get; set; } = string.Empty;
    public string CodeAdvisorDeploymentName { get; set; } = string.Empty;
}

/// <summary>
/// Agent 管理服務 - 使用 Microsoft Agent Framework
/// </summary>
public class AgentService
{
    // OpenTelemetry 追蹤源
    private static readonly ActivitySource _activitySource = new("PromptAgent.AI");
    
    private readonly ILogger<AgentService> _logger;
    private readonly IChatClient _chatClient;

    public AgentService(IChatClient chatClient, ILogger<AgentService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <summary>
    /// 執行單次 Agent 呼叫 - 使用 ChatClientAgent
    /// </summary>
    public async Task<AgentResponse> ExecuteAgentAsync(TestCase testCase, int executionIndex, CancellationToken cancellationToken = default)
    {
        // 開始 OTel span
        using var activity = _activitySource.StartActivity("AI.PromptTest", ActivityKind.Client);
        activity?.SetTag("ai.execution_index", executionIndex);
        activity?.SetTag("ai.prompt_length", testCase.SystemPrompt.Length);
        activity?.SetTag("ai.question_length", testCase.Question.Length);
        
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // 為每次執行建立獨立的 Agent
            var agent = new ChatClientAgent(
                _chatClient,
                instructions: testCase.SystemPrompt,
                name: $"PromptTester-{executionIndex}");
            
            // 使用 MAF 執行
            var response = await agent.RunAsync(testCase.Question);
            stopwatch.Stop();

            var content = response.ToString();

            // 記錄成功的追蹤資訊
            activity?.SetTag("ai.response_length", content.Length);
            activity?.SetTag("ai.latency_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("ai.success", true);

            _logger.LogInformation("Agent execution {Index} completed in {Time}ms", executionIndex, stopwatch.ElapsedMilliseconds);

            return new AgentResponse
            {
                ExecutionIndex = executionIndex,
                Content = content,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                IsSuccess = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // 記錄錯誤追蹤資訊
            activity?.SetTag("ai.success", false);
            activity?.SetTag("ai.error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            
            _logger.LogError(ex, "Agent execution {Index} failed", executionIndex);

            return new AgentResponse
            {
                ExecutionIndex = executionIndex,
                Content = string.Empty,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 使用 Task.WhenAll 平行執行多個 Agent
    /// 注意：ConcurrentOrchestration 底層也是並行，速度相同
    /// </summary>
    public async Task<List<AgentResponse>> ExecuteParallelAsync(TestCase testCase, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting parallel execution of {Count} agents", testCase.ExecutionCount);

        var tasks = Enumerable.Range(1, testCase.ExecutionCount)
            .Select(i => ExecuteAgentAsync(testCase, i, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);

        _logger.LogInformation("All {Count} agent executions completed", testCase.ExecutionCount);

        return [.. results.OrderBy(r => r.ExecutionIndex)];
    }
}
