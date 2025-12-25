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

    #region 引導模式 (Guided Mode)

    /// <summary>
    /// 分析回答差異並分群 (引導模式專用)
    /// </summary>
    public async Task<DifferenceAnalysis> AnalyzeResponseDifferencesAsync(
        TestCase testCase, 
        List<AgentResponse> responses, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing response differences for guided mode");

        var prompt = BuildDifferenceAnalysisPrompt(testCase, responses);

        try
        {
            var deploymentName = string.IsNullOrEmpty(_settings.EvaluatorDeploymentName)
                ? _settings.DeploymentName
                : _settings.EvaluatorDeploymentName;

            var chatClient = _client.GetChatClient(deploymentName);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(GetDifferenceAnalysisSystemPrompt()),
                new UserChatMessage(prompt)
            };

            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var content = response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

            return ParseDifferenceAnalysis(content, responses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze response differences");
            return new DifferenceAnalysis
            {
                Summary = $"分析失敗: {ex.Message}",
                Clusters = [],
                SuggestedDirections = ["請重試"]
            };
        }
    }

    /// <summary>
    /// 根據用戶反饋優化 Prompt (引導模式專用)
    /// </summary>
    public async Task<string> OptimizeWithFeedbackAsync(
        TestCase testCase,
        List<AgentResponse> responses,
        UserFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Optimizing prompt with user feedback: {Feedback}", 
            feedback.SelectedCluster ?? feedback.CustomFeedback);

        var prompt = BuildFeedbackOptimizationPrompt(testCase, responses, feedback);

        try
        {
            var deploymentName = string.IsNullOrEmpty(_settings.EvaluatorDeploymentName)
                ? _settings.DeploymentName
                : _settings.EvaluatorDeploymentName;

            var chatClient = _client.GetChatClient(deploymentName);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(GetFeedbackOptimizationSystemPrompt()),
                new UserChatMessage(prompt)
            };

            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var content = response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

            // 嘗試解析 JSON 取得優化後的 prompt
            return ParseOptimizedPrompt(content) ?? testCase.SystemPrompt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to optimize with feedback");
            return testCase.SystemPrompt;
        }
    }

    private static string GetDifferenceAnalysisSystemPrompt()
    {
        return """
            你是一個專業的回答分析專家。你的任務是分析多次 LLM 執行的結果，找出不同的回答風格並分群。
            
            請分析這些回答的差異，將它們分成 2-3 個群組，每個群組代表一種回答風格。
            
            請以 JSON 格式回覆：
            {
                "clusters": [
                    {
                        "clusterName": "簡潔版",
                        "description": "直接回答問題，字數較少",
                        "responseIndices": [1, 3, 5]
                    },
                    {
                        "clusterName": "詳細版",
                        "description": "包含步驟說明，解釋詳細",
                        "responseIndices": [2, 4]
                    }
                ],
                "summary": "這些回答主要分為簡潔直接型和詳細解釋型兩種風格...",
                "suggestedDirections": [
                    "如果需要簡潔回答，可以在 Prompt 加入「簡短回答」",
                    "如果需要詳細解釋，可以要求「逐步說明」"
                ]
            }
            """;
    }

    private static string GetFeedbackOptimizationSystemPrompt()
    {
        return """
            你是一個專業的 Prompt 優化專家。根據用戶的反饋，優化 System Prompt。
            
            用戶可能會：
            1. 選擇某種回答風格（如「簡潔版」）
            2. 提供自訂反饋（如「要更正式」「加入表情符號」）
            
            請根據反饋優化 System Prompt，讓 LLM 產生符合用戶期望的回答。
            
            請以 JSON 格式回覆：
            {
                "optimizedPrompt": "優化後的完整 System Prompt...",
                "changes": "說明做了什麼調整..."
            }
            """;
    }

    private static string BuildDifferenceAnalysisPrompt(TestCase testCase, List<AgentResponse> responses)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 原始 System Prompt");
        sb.AppendLine("```");
        sb.AppendLine(testCase.SystemPrompt);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## 問題");
        sb.AppendLine(testCase.Question);
        sb.AppendLine();
        sb.AppendLine("## 各次執行結果");
        sb.AppendLine();

        foreach (var response in responses.Where(r => r.IsSuccess))
        {
            sb.AppendLine($"### 回答 {response.ExecutionIndex}");
            sb.AppendLine("```");
            sb.AppendLine(response.Content);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("請分析這些回答的差異，將它們分群。");

        return sb.ToString();
    }

    private static string BuildFeedbackOptimizationPrompt(
        TestCase testCase, 
        List<AgentResponse> responses, 
        UserFeedback feedback)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 原始 System Prompt");
        sb.AppendLine("```");
        sb.AppendLine(testCase.SystemPrompt);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## 問題");
        sb.AppendLine(testCase.Question);
        sb.AppendLine();
        sb.AppendLine("## 預期答案");
        sb.AppendLine(testCase.ExpectedAnswer);
        sb.AppendLine();
        
        // 顯示部分回答作為參考
        sb.AppendLine("## 目前的回答範例");
        foreach (var response in responses.Where(r => r.IsSuccess).Take(2))
        {
            sb.AppendLine($"### 回答 {response.ExecutionIndex}");
            sb.AppendLine("```");
            sb.AppendLine(response.Content);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## 用戶反饋");
        if (!string.IsNullOrWhiteSpace(feedback.SelectedCluster))
        {
            sb.AppendLine($"用戶選擇了「{feedback.SelectedCluster}」風格");
        }
        if (!string.IsNullOrWhiteSpace(feedback.CustomFeedback))
        {
            sb.AppendLine($"用戶的自訂要求：{feedback.CustomFeedback}");
        }
        sb.AppendLine();
        sb.AppendLine("請根據用戶反饋優化 System Prompt。");

        return sb.ToString();
    }

    private static DifferenceAnalysis ParseDifferenceAnalysis(string content, List<AgentResponse> responses)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..(jsonEnd + 1)];
                var parsed = JsonSerializer.Deserialize<DifferenceAnalysisResponse>(json, JsonOptions);

                if (parsed != null)
                {
                    var analysis = new DifferenceAnalysis
                    {
                        Summary = parsed.Summary ?? "分析完成",
                        SuggestedDirections = parsed.SuggestedDirections ?? []
                    };

                    // 轉換 clusters
                    if (parsed.Clusters != null)
                    {
                        foreach (var c in parsed.Clusters)
                        {
                            var cluster = new ResponseCluster
                            {
                                ClusterName = c.ClusterName ?? "未命名",
                                Description = c.Description ?? "",
                                ResponseIndices = c.ResponseIndices ?? []
                            };

                            // 取得預覽內容
                            if (cluster.ResponseIndices.Count > 0)
                            {
                                var firstIndex = cluster.ResponseIndices.First();
                                var matchingResponse = responses.FirstOrDefault(r => r.ExecutionIndex == firstIndex);
                                if (matchingResponse != null)
                                {
                                    cluster.PreviewContent = matchingResponse.Content.Length > 100
                                        ? matchingResponse.Content[..100] + "..."
                                        : matchingResponse.Content;
                                }
                            }

                            analysis.Clusters.Add(cluster);
                        }
                    }

                    return analysis;
                }
            }
        }
        catch
        {
            // 解析失敗
        }

        // Fallback: 將所有回答放在一個群組
        return new DifferenceAnalysis
        {
            Summary = "無法分析差異，所有回答歸為一類",
            Clusters = [new ResponseCluster 
            { 
                ClusterName = "全部", 
                Description = "所有回答",
                ResponseIndices = responses.Select(r => r.ExecutionIndex).ToList()
            }],
            SuggestedDirections = []
        };
    }

    private static string? ParseOptimizedPrompt(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..(jsonEnd + 1)];
                var parsed = JsonSerializer.Deserialize<FeedbackOptimizationResponse>(json, JsonOptions);
                return parsed?.OptimizedPrompt;
            }
        }
        catch
        {
            // 解析失敗
        }

        return null;
    }

    private class DifferenceAnalysisResponse
    {
        public List<ClusterResponse>? Clusters { get; set; }
        public string? Summary { get; set; }
        public List<string>? SuggestedDirections { get; set; }
    }

    private class ClusterResponse
    {
        public string? ClusterName { get; set; }
        public string? Description { get; set; }
        public List<int>? ResponseIndices { get; set; }
    }

    private class FeedbackOptimizationResponse
    {
        public string? OptimizedPrompt { get; set; }
        public string? Changes { get; set; }
    }

    #endregion
}
