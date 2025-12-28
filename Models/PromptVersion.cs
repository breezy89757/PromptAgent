namespace PromptAgent.Models;

/// <summary>
/// Prompt 專案 - 用於組織多個版本的 Prompt
/// </summary>
public class PromptProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? CurrentVersionId { get; set; }
    public int VersionCount { get; set; }
}

/// <summary>
/// Prompt 版本 - 單一版本的 Prompt 內容及測試結果
/// </summary>
public class PromptVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = "";
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    // Prompt 內容
    public string SystemPrompt { get; set; } = "";
    public string Question { get; set; } = "";
    public string ExpectedAnswer { get; set; } = "";
    
    // 測試結果
    public int? StabilityScore { get; set; }
    public int? CorrectnessScore { get; set; }
    
    // 標籤和備註
    public List<string> Tags { get; set; } = new();
    public string Note { get; set; } = "";
    
    // 計算屬性
    public bool IsBest => Tags.Contains("best");
    public bool IsProduction => Tags.Contains("production");
}
