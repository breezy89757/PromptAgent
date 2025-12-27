using System.Text.Json;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;
using Azure;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;
using PromptAgent.Models;

namespace PromptAgent.Services;

/// <summary>
/// GAI 可行性評估服務 - 使用 LLM 分析需求是否適合使用 GAI
/// 成本以新台幣(TWD)計算，人工成本以台灣薪資中位數估算
/// </summary>
public class GAIEvaluatorService
{
    // 台灣薪資中位數約 43,000 TWD/月，約 269 TWD/時
    private const decimal TW_HOURLY_RATE = 269m;
    
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<GAIEvaluatorService> _logger;
    private readonly Lazy<ChatClient> _chatClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly bool _hasCodeAdvisor;

    public GAIEvaluatorService(
        IOptions<AzureOpenAISettings> settings, 
        ILogger<GAIEvaluatorService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _settings = settings.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        
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
                var client = new OpenAIClient(
                    new System.ClientModel.ApiKeyCredential(_settings.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(_settings.Endpoint) });
                return client.GetChatClient(_settings.DeploymentName);
            }
        });
        
        // 檢查是否有 Code Advisor 設定 (使用 Responses API)
        _hasCodeAdvisor = !string.IsNullOrEmpty(_settings.CodeAdvisorEndpoint) && 
                          !string.IsNullOrEmpty(_settings.CodeAdvisorApiKey) &&
                          !string.IsNullOrEmpty(_settings.CodeAdvisorDeploymentName);
    }

    /// <summary>
    /// 評估需求是否適合使用 GAI
    /// </summary>
    public async Task<EvaluationResult> EvaluateRequirementAsync(EvaluationRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting evaluation for requirement: {Requirement}", request.RequirementDescription);

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(request);

        try
        {
            var chatClient = _chatClient.Value;
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.3f
            };

            var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var content = response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

            _logger.LogInformation("Received evaluation response");

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
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate requirement");
            return CreateFallbackResult(request);
        }
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
