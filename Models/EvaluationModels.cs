namespace PromptAgent.Models;

/// <summary>
/// 使用者的評估請求
/// </summary>
public class EvaluationRequest
{
    /// <summary>需求描述</summary>
    public string RequirementDescription { get; set; } = string.Empty;
    
    /// <summary>每月預計使用次數</summary>
    public int MonthlyUsage { get; set; } = 1000;
    
    /// <summary>準確率要求 (百分比)</summary>
    public int AccuracyRequirement { get; set; } = 95;
    
    /// <summary>開發時間限制</summary>
    public string DevelopmentTimeLimit { get; set; } = "2週";
}

/// <summary>
/// 單一解決方案的分析結果
/// </summary>
public class SolutionAnalysis
{
    /// <summary>解決方案類型: "GAI", "Traditional", "Manual"</summary>
    public string SolutionType { get; set; } = string.Empty;
    
    /// <summary>顯示圖示</summary>
    public string Icon { get; set; } = string.Empty;
    
    /// <summary>顯示名稱</summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>推薦指數 (1-5 星)</summary>
    public int RecommendationScore { get; set; }
    
    /// <summary>是否為推薦方案</summary>
    public bool IsRecommended { get; set; }
    
    // ===== 雷達圖維度 (0-100) =====
    
    /// <summary>開發速度</summary>
    public int DevelopmentSpeed { get; set; }
    
    /// <summary>準確度</summary>
    public int Accuracy { get; set; }
    
    /// <summary>維護成本 (分數越高=成本越低=越好)</summary>
    public int MaintenanceCost { get; set; }
    
    /// <summary>擴展性</summary>
    public int Scalability { get; set; }
    
    /// <summary>靈活性</summary>
    public int Flexibility { get; set; }
    
    // ===== 成本估算 =====
    
    /// <summary>初期建置成本 (USD)</summary>
    public decimal SetupCost { get; set; }
    
    /// <summary>每次使用成本 (USD)</summary>
    public decimal CostPerUnit { get; set; }
    
    /// <summary>方案描述</summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>優點列表</summary>
    public List<string> Pros { get; set; } = new();
    
    /// <summary>缺點列表</summary>
    public List<string> Cons { get; set; } = new();
}

/// <summary>
/// 完整的評估結果
/// </summary>
public class EvaluationResult
{
    /// <summary>三種解決方案的分析</summary>
    public List<SolutionAnalysis> Solutions { get; set; } = new();
    
    /// <summary>推薦的解決方案類型</summary>
    public string RecommendedSolution { get; set; } = string.Empty;
    
    /// <summary>AI 生成的結論</summary>
    public string AiConclusion { get; set; } = string.Empty;
    
    /// <summary>傳統程式的替代工具建議 (如 "OpenCV + Tesseract")</summary>
    public string TraditionalAlternative { get; set; } = string.Empty;
    
    /// <summary>專業程式碼建議 (由 Codex 模型生成)</summary>
    public CodeSuggestion? CodeSuggestion { get; set; }
}

/// <summary>
/// 專業程式碼建議
/// </summary>
public class CodeSuggestion
{
    /// <summary>建議的技術棧</summary>
    public string TechStack { get; set; } = string.Empty;
    
    /// <summary>建議的程式庫/套件</summary>
    public List<string> Libraries { get; set; } = new();
    
    /// <summary>實作難度 (1-5)</summary>
    public int DifficultyLevel { get; set; }
    
    /// <summary>預估開發時數</summary>
    public int EstimatedHours { get; set; }
    
    /// <summary>範例程式碼</summary>
    public string SampleCode { get; set; } = string.Empty;
    
    /// <summary>實作步驟</summary>
    public List<string> ImplementationSteps { get; set; } = new();
    
    /// <summary>注意事項</summary>
    public List<string> Caveats { get; set; } = new();
}
