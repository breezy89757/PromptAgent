using System.Text;
using PromptAgent.Models;

namespace PromptAgent.Services;

/// <summary>
/// 配置匯出服務 - 將專案架構匯出為 LLM 可理解的 Markdown 格式
/// </summary>
public class ConfigExportService
{
    private readonly ILogger<ConfigExportService> _logger;
    private readonly IWebHostEnvironment _environment;

    public ConfigExportService(ILogger<ConfigExportService> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// 匯出專案架構配置到 .promptagent/config.md
    /// </summary>
    public async Task ExportProjectConfigAsync(CancellationToken cancellationToken = default)
    {
        var configDir = Path.Combine(_environment.ContentRootPath, ".promptagent");
        var configPath = Path.Combine(configDir, "config.md");

        // 確保目錄存在
        Directory.CreateDirectory(configDir);

        var content = BuildProjectConfigContent();

        await File.WriteAllTextAsync(configPath, content, cancellationToken);
        _logger.LogInformation("Project configuration exported to {Path}", configPath);
    }

    /// <summary>
    /// 讀取現有配置
    /// </summary>
    public async Task<string?> ReadConfigAsync(CancellationToken cancellationToken = default)
    {
        var configPath = Path.Combine(_environment.ContentRootPath, ".promptagent", "config.md");

        if (!File.Exists(configPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(configPath, cancellationToken);
    }

    private string BuildProjectConfigContent()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# PromptAgent 專案配置");
        sb.AppendLine();
        sb.AppendLine("> 此檔案記錄專案架構和修改說明，供其他 LLM 理解並重現此專案。");
        sb.AppendLine("> 測試結果不會儲存在此檔案中。");
        sb.AppendLine();
        sb.AppendLine($"**最後更新時間**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // 專案架構
        sb.AppendLine("## 專案架構");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("PromptAgent/");
        sb.AppendLine("├── Models/");
        sb.AppendLine("│   ├── TestCase.cs          # 測試案例模型 (SystemPrompt, Question, ExpectedAnswer, ExecutionCount)");
        sb.AppendLine("│   └── TestResult.cs        # 測試結果模型 (Responses, StabilityScore, CorrectnessScore, Suggestions, OptimizedPrompt)");
        sb.AppendLine("├── Services/");
        sb.AppendLine("│   ├── AgentService.cs      # Agent 管理服務 - Task.WhenAll 平行執行多個 Agent");
        sb.AppendLine("│   ├── EvaluationService.cs # 評估服務 - 使用更強模型 (gpt-5) 分析穩定性、正確性並生成優化建議");
        sb.AppendLine("│   └── ConfigExportService.cs # 配置匯出服務 - 匯出專案架構為 Markdown");
        sb.AppendLine("├── Components/");
        sb.AppendLine("│   ├── Pages/");
        sb.AppendLine("│   │   └── PromptTest.razor  # Prompt 測試頁面 (含預設範例和一鍵接受建議)");
        sb.AppendLine("│   └── Layout/");
        sb.AppendLine("│       └── NavMenu.razor     # 導航選單 (新增 Prompt Test 項目)");
        sb.AppendLine("├── appsettings.json         # Azure OpenAI 連線設定");
        sb.AppendLine("└── .promptagent/");
        sb.AppendLine("    └── config.md            # 此配置檔案");
        sb.AppendLine("```");
        sb.AppendLine();

        // 功能說明
        sb.AppendLine("## 功能說明");
        sb.AppendLine();
        sb.AppendLine("### 核心功能");
        sb.AppendLine();
        sb.AppendLine("1. **Prompt 測試**: 輸入 System Prompt、問題和預期答案進行測試");
        sb.AppendLine("2. **多輪平行執行**: 使用 `Task.WhenAll` 平行執行 1-10 個 Agent 測試穩定性");
        sb.AppendLine("3. **智慧評估**: 使用更強的模型 (gpt-5-chat) 分析結果穩定性與正確性");
        sb.AppendLine("4. **優化建議**: 自動生成 Prompt 優化建議和優化後的 Prompt");
        sb.AppendLine("5. **一鍵接受建議**: 點擊按鈕直接套用優化後的 Prompt");
        sb.AppendLine("6. **預設測試範例**: 提供數學運算、邏輯推理、翻譯、摘要、程式碼生成等範例");
        sb.AppendLine();

        // Azure OpenAI 設定
        sb.AppendLine("## Azure OpenAI 設定");
        sb.AppendLine();
        sb.AppendLine("在 `appsettings.json` 中配置：");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"AzureOpenAI\": {");
        sb.AppendLine("    \"Endpoint\": \"測試用 Azure OpenAI 端點\",");
        sb.AppendLine("    \"ApiKey\": \"測試用 API Key\",");
        sb.AppendLine("    \"DeploymentName\": \"gpt-4o-mini\",");
        sb.AppendLine("    \"EvaluatorEndpoint\": \"評估用 Azure OpenAI 端點\",");
        sb.AppendLine("    \"EvaluatorApiKey\": \"評估用 API Key\",");
        sb.AppendLine("    \"EvaluatorDeploymentName\": \"gpt-5-chat\"");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();

        // 技術實現
        sb.AppendLine("## 技術實現");
        sb.AppendLine();
        sb.AppendLine("### AgentService");
        sb.AppendLine();
        sb.AppendLine("- 使用 `Azure.AI.OpenAI` SDK 連接 Azure OpenAI");
        sb.AppendLine("- `ExecuteParallelAsync()` 使用 `Task.WhenAll` 平行執行多個 Agent");
        sb.AppendLine("- 每次執行記錄回應內容、執行時間、成功狀態");
        sb.AppendLine();
        sb.AppendLine("### EvaluationService");
        sb.AppendLine();
        sb.AppendLine("- 使用獨立的評估者端點 (可配置更強的模型)");
        sb.AppendLine("- 評估 Prompt 要求返回 JSON 格式：");
        sb.AppendLine("  - `stabilityScore`: 穩定性評分 (0-100)");
        sb.AppendLine("  - `correctnessScore`: 正確性評分 (0-100)");
        sb.AppendLine("  - `evaluationReport`: 詳細評估報告");
        sb.AppendLine("  - `suggestions`: 優化建議列表");
        sb.AppendLine("  - `optimizedPrompt`: 優化後的完整 Prompt");
        sb.AppendLine();

        // 專案重現指南
        sb.AppendLine("## 專案重現指南");
        sb.AppendLine();
        sb.AppendLine("### 環境需求");
        sb.AppendLine();
        sb.AppendLine("- .NET 10.0 或更高版本");
        sb.AppendLine("- Azure OpenAI 服務 (需要兩個 deployment: 測試用和評估用)");
        sb.AppendLine();
        sb.AppendLine("### 執行步驟");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine("# 1. 進入專案目錄");
        sb.AppendLine("cd PromptAgent");
        sb.AppendLine();
        sb.AppendLine("# 2. 配置 appsettings.json 中的 Azure OpenAI 設定");
        sb.AppendLine();
        sb.AppendLine("# 3. 執行專案");
        sb.AppendLine("dotnet run");
        sb.AppendLine();
        sb.AppendLine("# 4. 開啟瀏覽器訪問 http://localhost:5036/prompt-test");
        sb.AppendLine("```");
        sb.AppendLine();

        return sb.ToString();
    }
}
