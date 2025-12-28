using System.Text.Json;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Azure;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using PromptAgent.Models;

namespace PromptAgent.Services;

/// <summary>
/// GAI 可行性評估服務 - 使用 Microsoft Agent Framework
/// 成本以新台幣(TWD)計算，人工成本以台灣薪資中位數估算
/// 智慧路由：簡單需求快速回應，複雜需求多 Agent 協作
/// </summary>
public class GAIEvaluatorService
{
    // 台灣薪資中位數約 43,000 TWD/月，約 269 TWD/時
    private const decimal TW_HOURLY_RATE = 269m;
    
    // Token 成本估算 (GPT-4o 價格: $0.01/1K input, $0.03/1K output)
    private const decimal USD_PER_1K_INPUT_TOKENS = 0.01m;
    private const decimal USD_PER_1K_OUTPUT_TOKENS = 0.03m;
    private const decimal USD_TO_TWD = 32m; // 匯率
    private const int CHARS_PER_TOKEN = 4; // 中文約 1-2 字/token，英文約 4 字/token
    
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<GAIEvaluatorService> _logger;
    private readonly IChatClient _chatClient;
    private readonly AIAgent _routerAgent;
    private readonly AIAgent _evaluatorAgent;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly bool _hasCodeAdvisor;

    public GAIEvaluatorService(
        IOptions<AzureOpenAISettings> settings, 
        ILogger<GAIEvaluatorService> logger,
        IChatClient chatClient,
        IHttpClientFactory httpClientFactory)
    {
        _settings = settings.Value;
        _logger = logger;
        _chatClient = chatClient;
        _httpClientFactory = httpClientFactory;
        
        // 建立 Router Agent - 快速判斷複雜度
        _routerAgent = new ChatClientAgent(
            chatClient,
            instructions: """
                你是一個需求複雜度分類器。判斷需求是 SIMPLE 還是 COMPLEX。
                
                SIMPLE（傳統程式可解決）：
                - 格式驗證（Email、電話、身分證）
                - 簡單字串轉換
                - 固定規則的資料處理
                - 明確的算法問題
                
                COMPLEX（需要多角度分析）：
                - 影像/語音識別
                - 自然語言處理
                - 需要比較多種方案
                - 涉及 AI vs 傳統的取捨
                
                只回答 SIMPLE 或 COMPLEX，不要有其他文字。
                """,
            name: "RouterAgent");
        
        // 建立 Evaluator Agent - 完整評估
        _evaluatorAgent = new ChatClientAgent(
            chatClient,
            instructions: BuildSystemPrompt(),
            name: "EvaluatorAgent");
        
        // 檢查是否有 Code Advisor 設定 (使用 Responses API)
        _hasCodeAdvisor = !string.IsNullOrEmpty(_settings.CodeAdvisorEndpoint) && 
                          !string.IsNullOrEmpty(_settings.CodeAdvisorApiKey) &&
                          !string.IsNullOrEmpty(_settings.CodeAdvisorDeploymentName);
    }

    /// <summary>
    /// 評估需求是否適合使用 GAI - 使用智慧路由
    /// </summary>
    public async Task<EvaluationResult> EvaluateRequirementAsync(EvaluationRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting evaluation for requirement: {Requirement}", request.RequirementDescription);

        try
        {
            // Step 1: 使用 Router Agent 快速判斷複雜度
            var routerResponse = await _routerAgent.RunAsync(
                $"判斷這個需求的複雜度：{request.RequirementDescription}");
            
            var complexity = routerResponse.ToString().Trim().ToUpperInvariant();
            _logger.LogInformation("Router classified requirement as: {Complexity}", complexity);
            
            // Step 2: 根據複雜度選擇處理方式
            if (complexity.Contains("SIMPLE"))
            {
                // 簡單需求：快速生成傳統程式建議
                return await QuickEvaluateAsync(request, cancellationToken);
            }
            
            // Step 3: 複雜需求：完整評估
            return await FullEvaluateAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate requirement");
            return CreateFallbackResult(request);
        }
    }
    
    /// <summary>
    /// 簡單需求的快速評估 - 直接推薦傳統程式
    /// </summary>
    private async Task<EvaluationResult> QuickEvaluateAsync(EvaluationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using quick evaluation for simple requirement");
        
        var quickPrompt = $$"""
            這是一個簡單的需求，請給出傳統程式解決方案。
            需求：{{request.RequirementDescription}}
            
            請以 JSON 格式回應，只包含：
            {
                "traditionalAlternative": "建議使用的技術（如 Regex、DateTime.Parse）",
                "description": "一句話說明實作方式"
            }
            """;
        
        var response = await _evaluatorAgent.RunAsync(quickPrompt);
        var content = response.ToString();
        
        // 解析簡單回應並建立結果
        var result = CreateSimpleResult(request, content);
        
        // 如果有 Codex，生成程式碼建議
        if (_hasCodeAdvisor)
        {
            try
            {
                result.CodeSuggestion = await GetCodeSuggestionAsync(
                    request.RequirementDescription, 
                    result.TraditionalAlternative,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get code suggestion from Codex");
            }
        }
        
        // 計算 Token 成本
        CalculateTokenCost(result, quickPrompt, content);
        result.RequirementDescription = request.RequirementDescription;
        
        return result;
    }
    
    /// <summary>
    /// 計算 Token 使用量和成本
    /// </summary>
    private static void CalculateTokenCost(EvaluationResult result, string prompt, string response)
    {
        // 估算 Token 數量（中文約 1-2 字/token）
        result.EstimatedPromptTokens = Math.Max(1, prompt.Length / CHARS_PER_TOKEN);
        result.EstimatedResponseTokens = Math.Max(1, response.Length / CHARS_PER_TOKEN);
        
        // 計算成本
        var inputCost = (result.EstimatedPromptTokens / 1000m) * USD_PER_1K_INPUT_TOKENS;
        var outputCost = (result.EstimatedResponseTokens / 1000m) * USD_PER_1K_OUTPUT_TOKENS;
        result.EstimatedCostUsd = inputCost + outputCost;
        result.EstimatedCostTwd = result.EstimatedCostUsd * USD_TO_TWD;
        
        result.EvaluatedAt = DateTime.Now;
    }
    
    /// <summary>
    /// 複雜需求的完整評估 - 三方案比較
    /// </summary>
    private async Task<EvaluationResult> FullEvaluateAsync(EvaluationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using full evaluation for complex requirement");
        
        var userPrompt = BuildUserPrompt(request);
        var response = await _evaluatorAgent.RunAsync(userPrompt);
        var content = response.ToString();

        _logger.LogInformation("Received full evaluation response");

        var result = ParseEvaluationResponse(content);
        
        // 如果推薦傳統程式方案，使用 Codex 生成專業程式碼建議
        if (result.RecommendedSolution == "Traditional" && _hasCodeAdvisor)
        {
            try
            {
                result.CodeSuggestion = await GetCodeSuggestionAsync(
                    request.RequirementDescription, 
                    result.TraditionalAlternative,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get code suggestion from Codex");
            }
        }
        
        // 計算 Token 成本
        CalculateTokenCost(result, userPrompt, content);
        result.RequirementDescription = request.RequirementDescription;
        
        return result;
    }

    /// <summary>
    /// 使用 Codex 模型生成專業程式碼建議 (使用 Responses API)
    /// </summary>
    private async Task<CodeSuggestion?> GetCodeSuggestionAsync(
        string requirement, 
        string suggestedTools,
        CancellationToken cancellationToken)
    {
        if (!_hasCodeAdvisor) return null;
        
        var prompt = $$"""
            你是一位資深軟體工程師，專門提供實用的程式碼建議。
            
            需求描述：{{requirement}}
            建議使用的技術方向：{{suggestedTools}}
            
            請根據需求提供專業的實作建議。技術棧可以是 Python、C#、JavaScript 或其他適合的語言。
            
            請以 JSON 格式回應（只回傳 JSON，不要有其他文字）：
            {
                "techStack": "語言版本 + 主要框架或工具",
                "libraries": ["套件1", "套件2"],
                "difficultyLevel": 1-5,
                "estimatedHours": 預估開發時數,
                "sampleCode": "完整可執行的範例程式碼",
                "implementationSteps": [
                    "步驟1：安裝相關套件",
                    "步驟2：實作核心邏輯",
                    "..."
                ],
                "caveats": [
                    "注意事項1",
                    "注意事項2"
                ]
            }
            
            重要：
            - sampleCode 必須是完整且可執行的程式碼片段
            - 根據需求選擇最合適的程式語言和工具
            - difficultyLevel 為 1-5 (1=簡單, 5=困難)
            - estimatedHours 為預估開發時數（整數）
            """;
        
        // 建立 Responses API 請求
        var requestBody = new
        {
            model = _settings.CodeAdvisorDeploymentName,
            input = prompt,
            max_output_tokens = 4096
        };
        
        var endpoint = $"{_settings.CodeAdvisorEndpoint.TrimEnd('/')}/openai/responses?api-version=2025-04-01-preview";
        
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("Authorization", $"Bearer {_settings.CodeAdvisorApiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");
        
        var httpClient = _httpClientFactory.CreateClient("CodeAdvisor");
        var response = await httpClient.SendAsync(request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Codex API returned {StatusCode}: {Error}", response.StatusCode, errorContent);
            return null;
        }
        
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        // 解析 Responses API 的回應格式
        using var doc = JsonDocument.Parse(responseJson);
        var outputText = "";
        
        // Responses API 格式: 
        // { "output": [
        //     { "type": "reasoning", ... },
        //     { "type": "message", "content": [{ "type": "output_text", "text": "..." }] }
        // ]}
        if (doc.RootElement.TryGetProperty("output", out var outputArray))
        {
            foreach (var outputItem in outputArray.EnumerateArray())
            {
                // 找到 type: "message" 的輸出
                if (outputItem.TryGetProperty("type", out var typeEl) && 
                    typeEl.GetString() == "message" &&
                    outputItem.TryGetProperty("content", out var contentArray))
                {
                    foreach (var contentItem in contentArray.EnumerateArray())
                    {
                        // 找到 type: "output_text" 的內容
                        if (contentItem.TryGetProperty("type", out var contentType) &&
                            contentType.GetString() == "output_text" &&
                            contentItem.TryGetProperty("text", out var textEl))
                        {
                            outputText = textEl.GetString() ?? "";
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(outputText)) break;
                }
            }
        }
        
        if (string.IsNullOrEmpty(outputText))
        {
            _logger.LogWarning("Could not parse Codex response: {Response}", responseJson);
            return null;
        }
        
        return ParseCodeSuggestion(outputText);
    }

    private static string BuildSystemPrompt()
    {
        return """
            你是一位資深的技術架構師，專門評估軟體需求的最佳實現方式。
            
            使用者會提供一個需求描述，你需要分析這個需求適合用哪種方式實現：
            1. GAI 方案 - 使用生成式 AI (如 GPT-4 Vision, Claude 等)
            2. 傳統程式 - 使用傳統程式庫 (如 OpenCV, Tesseract, regex, rule-based 等)
            3. 人工處理 - 雇用人員或外包處理
            
            【重要】成本計算基準（新台幣 TWD）：
            - 台灣軟體工程師時薪：約 269 TWD（月薪中位數 43,000 ÷ 160 工時）
            - GAI API 成本：GPT-4o 約 0.4 TWD/1K tokens，GPT-4o-mini 約 0.05 TWD/1K tokens
            - 人工處理：假設外包人員時薪約 180 TWD
            
            你必須以 JSON 格式回應，格式如下：
            {
                "solutions": [
                    {
                        "solutionType": "GAI",
                        "icon": "🤖",
                        "displayName": "GAI 方案",
                        "recommendationScore": 3,
                        "isRecommended": false,
                        "developmentSpeed": 85,
                        "accuracy": 90,
                        "maintenanceCost": 60,
                        "scalability": 95,
                        "flexibility": 95,
                        "setupCost": 3000,
                        "costPerUnit": 0.5,
                        "description": "使用 GPT-4 Vision API 進行圖像識別",
                        "pros": ["開發快速", "高度靈活"],
                        "cons": ["持續 API 成本", "需要穩定網路"]
                    },
                    {
                        "solutionType": "Traditional",
                        "icon": "💻",
                        "displayName": "傳統程式",
                        "recommendationScore": 5,
                        "isRecommended": true,
                        "developmentSpeed": 50,
                        "accuracy": 85,
                        "maintenanceCost": 90,
                        "scalability": 80,
                        "flexibility": 40,
                        "setupCost": 43000,
                        "costPerUnit": 0.01,
                        "description": "使用 OpenCV + Tesseract 進行 OCR",
                        "pros": ["一次開發長期使用", "無 API 成本"],
                        "cons": ["開發時間較長", "需要專業知識"]
                    },
                    {
                        "solutionType": "Manual",
                        "icon": "🧑‍💼",
                        "displayName": "人工處理",
                        "recommendationScore": 2,
                        "isRecommended": false,
                        "developmentSpeed": 100,
                        "accuracy": 99,
                        "maintenanceCost": 10,
                        "scalability": 20,
                        "flexibility": 100,
                        "setupCost": 0,
                        "costPerUnit": 3,
                        "description": "雇用人員進行處理",
                        "pros": ["最高準確率", "無需技術開發"],
                        "cons": ["成本高昂", "無法擴展"]
                    }
                ],
                "recommendedSolution": "Traditional",
                "aiConclusion": "針對圖形驗證碼識別，由於格式相對固定，建議使用 OpenCV + Tesseract 的傳統方案...",
                "traditionalAlternative": "OpenCV + Tesseract"
            }
            
            注意事項：
            - recommendationScore 為 1-5 的整數
            - 所有數值維度 (developmentSpeed 等) 為 0-100 的整數
            - setupCost 和 costPerUnit 單位為【新台幣 TWD】
            - setupCost 應根據預估開發時數 × 269 TWD/時計算
            - maintenanceCost 分數越高代表維護成本越低 (對使用者越有利)
            - 請根據實際情況給出合理的評估，不要總是推薦同一種方案
            - aiConclusion 應該用繁體中文，解釋為什麼推薦該方案
            - 只回傳 JSON，不要有其他文字
            """;
    }

    private static string BuildUserPrompt(EvaluationRequest request)
    {
        return $"""
            請評估以下需求：
            
            【需求描述】
            {request.RequirementDescription}
            
            【使用參數】
            - 每月預計使用次數：{request.MonthlyUsage:N0} 次
            
            請根據以上資訊，分析三種解決方案的優劣，並給出推薦。
            所有成本請以【新台幣 TWD】計算。
            """;
    }

    private EvaluationResult ParseEvaluationResponse(string content)
    {
        try
        {
            var jsonContent = ExtractJson(content);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<EvaluationResult>(jsonContent, options);
            return parsed ?? CreateFallbackResult(new EvaluationRequest());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse evaluation response, using fallback");
            return CreateFallbackResult(new EvaluationRequest());
        }
    }
    
    private CodeSuggestion? ParseCodeSuggestion(string content)
    {
        try
        {
            var jsonContent = ExtractJson(content);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<CodeSuggestion>(jsonContent, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse code suggestion");
            return null;
        }
    }
    
    private static string ExtractJson(string content)
    {
        var jsonContent = content;
        if (content.Contains("```json"))
        {
            var start = content.IndexOf("```json") + 7;
            var end = content.LastIndexOf("```");
            if (end > start) jsonContent = content[start..end].Trim();
        }
        else if (content.Contains("```"))
        {
            var start = content.IndexOf("```") + 3;
            var end = content.LastIndexOf("```");
            if (end > start) jsonContent = content[start..end].Trim();
        }
        return jsonContent;
    }

    /// <summary>
    /// 為簡單需求建立快速結果
    /// </summary>
    private static EvaluationResult CreateSimpleResult(EvaluationRequest request, string content)
    {
        var traditional = "傳統程式";
        var description = "使用傳統程式方式處理";
        
        try
        {
            var jsonContent = ExtractJson(content);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            
            traditional = root.TryGetProperty("traditionalAlternative", out var alt) 
                ? alt.GetString() ?? traditional 
                : traditional;
            description = root.TryGetProperty("description", out var desc) 
                ? desc.GetString() ?? description 
                : description;
        }
        catch
        {
            // 使用預設值
        }
        
        return new EvaluationResult
        {
            Solutions = new List<SolutionAnalysis>
            {
                new()
                {
                    SolutionType = "Traditional",
                    Icon = "💻",
                    DisplayName = "傳統程式",
                    RecommendationScore = 5,
                    IsRecommended = true,
                    DevelopmentSpeed = 80,
                    Accuracy = 98,
                    MaintenanceCost = 90,
                    Scalability = 85,
                    Flexibility = 30,
                    SetupCost = 2000, // TWD (簡單需求開發時間短)
                    CostPerUnit = 0.001m,
                    Description = description,
                    Pros = new List<string> { "簡單可靠", "無持續成本", "高準確率" },
                    Cons = new List<string> { "靈活性較低" }
                },
                new()
                {
                    SolutionType = "GAI",
                    Icon = "🤖",
                    DisplayName = "GAI 方案",
                    RecommendationScore = 1,
                    IsRecommended = false,
                    DevelopmentSpeed = 90,
                    Accuracy = 85,
                    MaintenanceCost = 60,
                    Scalability = 90,
                    Flexibility = 95,
                    SetupCost = 5000,
                    CostPerUnit = 0.5m,
                    Description = "對於這個簡單需求，使用 GAI 是過度設計",
                    Pros = new List<string> { "開發最快" },
                    Cons = new List<string> { "成本過高", "殺雞用牛刀" }
                },
                new()
                {
                    SolutionType = "Manual",
                    Icon = "🧑‍💼",
                    DisplayName = "人工處理",
                    RecommendationScore = 1,
                    IsRecommended = false,
                    DevelopmentSpeed = 100,
                    Accuracy = 99,
                    MaintenanceCost = 10,
                    Scalability = 10,
                    Flexibility = 100,
                    SetupCost = 0,
                    CostPerUnit = 3m,
                    Description = "不建議人工處理這類可自動化的任務",
                    Pros = new List<string> { "無需開發" },
                    Cons = new List<string> { "效率極低", "成本高昂" }
                }
            },
            RecommendedSolution = "Traditional",
            AiConclusion = $"✅ 這是一個簡單需求！建議使用 {traditional}，開發快速且穩定可靠。",
            TraditionalAlternative = traditional
        };
    }

    private static EvaluationResult CreateFallbackResult(EvaluationRequest request)
    {
        return new EvaluationResult
        {
            Solutions = new List<SolutionAnalysis>
            {
                new()
                {
                    SolutionType = "GAI",
                    Icon = "🤖",
                    DisplayName = "GAI 方案",
                    RecommendationScore = 3,
                    IsRecommended = false,
                    DevelopmentSpeed = 85,
                    Accuracy = 85,
                    MaintenanceCost = 60,
                    Scalability = 90,
                    Flexibility = 90,
                    SetupCost = 5000, // TWD
                    CostPerUnit = 0.5m, // TWD per call
                    Description = "使用生成式 AI API 處理",
                    Pros = new List<string> { "開發快速", "高度靈活", "易於迭代" },
                    Cons = new List<string> { "持續 API 成本", "網路依賴" }
                },
                new()
                {
                    SolutionType = "Traditional",
                    Icon = "💻",
                    DisplayName = "傳統程式",
                    RecommendationScore = 4,
                    IsRecommended = true,
                    DevelopmentSpeed = 50,
                    Accuracy = 80,
                    MaintenanceCost = 85,
                    Scalability = 75,
                    Flexibility = 40,
                    SetupCost = 43000, // TWD (約 160 小時 × 269)
                    CostPerUnit = 0.01m, // TWD
                    Description = "使用傳統程式庫開發",
                    Pros = new List<string> { "無持續成本", "可離線運作", "完全掌控" },
                    Cons = new List<string> { "開發時間較長", "需專業知識" }
                },
                new()
                {
                    SolutionType = "Manual",
                    Icon = "🧑‍💼",
                    DisplayName = "人工處理",
                    RecommendationScore = 2,
                    IsRecommended = false,
                    DevelopmentSpeed = 100,
                    Accuracy = 99,
                    MaintenanceCost = 20,
                    Scalability = 15,
                    Flexibility = 100,
                    SetupCost = 0,
                    CostPerUnit = 3m, // TWD (約 180/60 = 3 per minute task)
                    Description = "雇用人員處理",
                    Pros = new List<string> { "最高準確率", "無需開發" },
                    Cons = new List<string> { "高人力成本", "難以擴展" }
                }
            },
            RecommendedSolution = "Traditional",
            AiConclusion = "根據您的需求描述，建議評估傳統程式方案，可能有成熟的開源工具可以使用。",
            TraditionalAlternative = "視需求而定"
        };
    }
}
