using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using PromptAgent.Models;

namespace PromptAgent.Services;

/// <summary>
/// 評估服務 - 使用更強的模型分析結果
/// </summary>
public class EvaluationService
{
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<EvaluationService> _logger;
    private readonly AzureOpenAIClient _client;
    
    // 快取 JsonSerializerOptions 避免重複建立
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public EvaluationService(IOptions<AzureOpenAISettings> settings, ILogger<EvaluationService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        // 使用評估者專用的 endpoint 和 API key (如果有配置)
        var endpoint = string.IsNullOrEmpty(_settings.EvaluatorEndpoint)
            ? _settings.Endpoint
            : _settings.EvaluatorEndpoint;
        var apiKey = string.IsNullOrEmpty(_settings.EvaluatorApiKey)
            ? _settings.ApiKey
            : _settings.EvaluatorApiKey;

        _client = new AzureOpenAIClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));
    }

    /// <summary>
    /// 評估多次執行結果
    /// </summary>
    public async Task<TestResult> EvaluateResultsAsync(TestCase testCase, List<AgentResponse> responses, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting evaluation for test case {TestCaseId}", testCase.Id);

        var result = new TestResult
        {
            TestCaseId = testCase.Id,
            Responses = responses
        };

        // 建立評估提示
        var evaluationPrompt = BuildEvaluationPrompt(testCase, responses);

        try
        {
            var deploymentName = string.IsNullOrEmpty(_settings.EvaluatorDeploymentName)
                ? _settings.DeploymentName
                : _settings.EvaluatorDeploymentName;

            var chatClient = _client.GetChatClient(deploymentName);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(GetEvaluatorSystemPrompt()),
                new UserChatMessage(evaluationPrompt)
            };

            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var evaluationContent = response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

            // 解析評估結果
            ParseEvaluationResult(evaluationContent, result);

            _logger.LogInformation("Evaluation completed. Stability: {Stability}, Correctness: {Correctness}",
                result.StabilityScore, result.CorrectnessScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Evaluation failed for test case {TestCaseId}", testCase.Id);
            result.EvaluationReport = $"評估失敗: {ex.Message}";
            result.Suggestions = ["請檢查 Azure OpenAI 連線設定"];
        }

        return result;
    }

    private static string GetEvaluatorSystemPrompt()
    {
        return """
            你是一個專業的 Prompt 評估專家。你的任務是分析多次 LLM 執行的結果，評估：
            1. 穩定性：多次執行結果的一致性 (0-100分)
            2. 正確性：與預期答案的吻合程度 (0-100分)
            3. 優化建議：如何改進 System Prompt 以獲得更好的結果
            4. 優化後的 Prompt：根據建議優化後的完整 System Prompt

            請以 JSON 格式回覆，格式如下：
            {
                "stabilityScore": 85,
                "correctnessScore": 90,
                "evaluationReport": "詳細評估報告...",
                "suggestions": ["建議1", "建議2", "建議3"],
                "optimizedPrompt": "優化後的完整 System Prompt..."
            """;
    }

    private static string BuildEvaluationPrompt(TestCase testCase, List<AgentResponse> responses)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 測試案例資訊");
        sb.AppendLine();
        sb.AppendLine("### System Prompt");
        sb.AppendLine("```");
        sb.AppendLine(testCase.SystemPrompt);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### 問題");
        sb.AppendLine(testCase.Question);
        sb.AppendLine();
        sb.AppendLine("### 預期答案");
        sb.AppendLine(testCase.ExpectedAnswer);
        sb.AppendLine();
        sb.AppendLine("## 執行結果");
        sb.AppendLine();

        foreach (var response in responses)
        {
            sb.AppendLine($"### 第 {response.ExecutionIndex} 次執行 ({response.ExecutionTimeMs}ms)");
            if (response.IsSuccess)
            {
                sb.AppendLine("```");
                sb.AppendLine(response.Content);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine($"**執行失敗**: {response.ErrorMessage}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("請分析以上執行結果，給出穩定性評分、正確性評分、詳細評估報告和優化建議。");

        return sb.ToString();
    }

    private static void ParseEvaluationResult(string content, TestResult result)
    {
        try
        {
            // 嘗試從 content 中提取 JSON
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..(jsonEnd + 1)];
                var parsed = JsonSerializer.Deserialize<EvaluationResponse>(json, JsonOptions);

                if (parsed != null)
                {
                    result.StabilityScore = parsed.StabilityScore;
                    result.CorrectnessScore = parsed.CorrectnessScore;
                    result.EvaluationReport = parsed.EvaluationReport ?? string.Empty;
                    result.Suggestions = parsed.Suggestions ?? [];
                    result.OptimizedPrompt = parsed.OptimizedPrompt;
                    return;
                }
            }
        }
        catch
        {
            // 解析失敗，使用原始內容
        }

        // Fallback: 使用原始內容作為報告
        result.EvaluationReport = content;
        result.StabilityScore = 50;
        result.CorrectnessScore = 50;
        result.Suggestions = ["無法解析評估結果，請查看詳細報告"];
    }

    private class EvaluationResponse
    {
        public int StabilityScore { get; set; }
        public int CorrectnessScore { get; set; }
        public string? EvaluationReport { get; set; }
        public List<string>? Suggestions { get; set; }
        public string? OptimizedPrompt { get; set; }
    }
}
