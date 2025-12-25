using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using PromptAgent.Models;

namespace PromptAgent.Services;

/// <summary>
/// 範例生成服務 - 使用 LLM 根據分類生成測試範例
/// </summary>
public class ExampleGeneratorService
{
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<ExampleGeneratorService> _logger;
    private readonly Lazy<ChatClient> _chatClient;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ExampleGeneratorService(IOptions<AzureOpenAISettings> settings, ILogger<ExampleGeneratorService> logger)
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
                var client = new OpenAIClient(
                    new System.ClientModel.ApiKeyCredential(_settings.ApiKey), 
                    new OpenAIClientOptions { Endpoint = new Uri(_settings.Endpoint) });
                return client.GetChatClient(_settings.DeploymentName);
            }
        });
    }

    /// <summary>
    /// 可用的範例分類
    /// </summary>
    public static readonly List<ExampleCategory> Categories =
    [
        new("math", "📐 數學運算", "數學計算、方程式求解、邏輯運算"),
        new("logic", "🧠 邏輯推理", "推理問題、條件判斷、關係分析"),
        new("translation", "🌐 翻譯任務", "多語言翻譯、文本轉換"),
        new("summary", "📝 文字摘要", "文章摘要、重點提取"),
        new("code", "💻 程式碼生成", "程式撰寫、演算法實作"),
        new("creative", "✨ 創意寫作", "故事創作、文案撰寫"),
        new("qa", "❓ 問答系統", "知識問答、客服對話")
    ];

    /// <summary>
    /// 根據分類生成隨機測試範例
    /// </summary>
    public async Task<TestCase> GenerateExampleAsync(string categoryId, CancellationToken cancellationToken = default)
    {
        var category = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category == null)
        {
            throw new ArgumentException($"Unknown category: {categoryId}");
        }

        _logger.LogInformation("Generating example for category: {Category}", category.Name);

        var chatClient = _chatClient.Value;

        var systemPrompt = """
            你是一個 Prompt 測試範例生成專家。你的任務是根據指定的分類，生成一個創意且實用的測試範例。
            
            請以 JSON 格式回覆，包含以下欄位：
            {
                "systemPrompt": "一個針對此任務設計的 System Prompt，應該詳細說明 AI 的角色和回答格式要求",
                "question": "一個具體的測試問題",
                "expectedAnswer": "這個問題的預期答案（簡潔版本）"
            }
            
            注意：
            1. System Prompt 要具體且有結構，包含格式要求
            2. 問題要有明確答案，方便評估
            3. 預期答案要簡潔，作為評估參考
            4. 每次生成的內容要有創意，不要重複
            """;

        var userPrompt = $"""
            請生成一個「{category.Name}」類別的測試範例。
            類別說明：{category.Description}
            
            要求：生成一個有趣且實用的範例，確保問題有明確的正確答案。
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        try
        {
            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var content = response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

            // 解析 JSON 回應
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..(jsonEnd + 1)];
                var parsed = JsonSerializer.Deserialize<GeneratedExample>(json, JsonOptions);

                if (parsed != null)
                {
                    _logger.LogInformation("Successfully generated example for {Category}", category.Name);
                    
                    return new TestCase
                    {
                        SystemPrompt = parsed.SystemPrompt ?? string.Empty,
                        Question = parsed.Question ?? string.Empty,
                        ExpectedAnswer = parsed.ExpectedAnswer ?? string.Empty
                    };
                }
            }

            throw new InvalidOperationException("Failed to parse generated example");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate example for {Category}", category.Name);
            throw;
        }
    }

    private class GeneratedExample
    {
        public string? SystemPrompt { get; set; }
        public string? Question { get; set; }
        public string? ExpectedAnswer { get; set; }
    }
}

/// <summary>
/// 範例分類
/// </summary>
public record ExampleCategory(string Id, string Name, string Description);
