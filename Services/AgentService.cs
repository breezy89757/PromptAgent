using System.Diagnostics;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI.OpenAI;
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
/// Agent 管理服務 - 支援 Azure OpenAI 和一般 OpenAI (LiteLLM)
/// </summary>
public class AgentService
{
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<AgentService> _logger;
    private readonly Lazy<ChatClient> _chatClient;

    public AgentService(IOptions<AzureOpenAISettings> settings, ILogger<AgentService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        
        // 快取 ChatClient 實例以重用連線
        _chatClient = new Lazy<ChatClient>(() => 
        {
            if (_settings.Provider.Equals("Azure", StringComparison.OrdinalIgnoreCase))
            {
                var client = new AzureOpenAIClient(
                    new Uri(_settings.Endpoint),
                    new AzureKeyCredential(_settings.ApiKey));
                return client.GetChatClient(_settings.DeploymentName);
            }
            else
            {
                // OpenAI / LiteLLM
                // 使用 OpenAIClient (需確保引用了 OpenAI namespace)
                var client = new OpenAIClient(
                    new System.ClientModel.ApiKeyCredential(_settings.ApiKey), 
                    new OpenAIClientOptions { Endpoint = new Uri(_settings.Endpoint) });
                return client.GetChatClient(_settings.DeploymentName);
            }
        });
    }

    /// <summary>
    /// 執行單次 Agent 呼叫
    /// </summary>
    public async Task<AgentResponse> ExecuteAgentAsync(TestCase testCase, int executionIndex, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var chatClient = _chatClient.Value;

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(testCase.SystemPrompt),
                new UserChatMessage(testCase.Question)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = testCase.Temperature
            };

            var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            stopwatch.Stop();

            var content = response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

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
