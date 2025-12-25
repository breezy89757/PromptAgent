namespace PromptAgent.Models;

/// <summary>
/// 單次執行回應
/// </summary>
public class AgentResponse
{
    /// <summary>
    /// 執行序號
    /// </summary>
    public int ExecutionIndex { get; set; }

    /// <summary>
    /// Agent 回應內容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 執行時間 (毫秒)
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 錯誤訊息 (如果有)
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 測試結果模型
/// </summary>
public class TestResult
{
    /// <summary>
    /// 對應測試案例 ID
    /// </summary>
    public string TestCaseId { get; set; } = string.Empty;

    /// <summary>
    /// 多次執行的回應列表
    /// </summary>
    public List<AgentResponse> Responses { get; set; } = [];

    /// <summary>
    /// 穩定性評分 (0-100)
    /// </summary>
    public int StabilityScore { get; set; }

    /// <summary>
    /// 正確性評分 (0-100)
    /// </summary>
    public int CorrectnessScore { get; set; }

    /// <summary>
    /// 評估報告
    /// </summary>
    public string EvaluationReport { get; set; } = string.Empty;

    /// <summary>
    /// 優化建議
    /// </summary>
    public List<string> Suggestions { get; set; } = [];

    /// <summary>
    /// 優化後的 Prompt (用於一鍵接受建議)
    /// </summary>
    public string? OptimizedPrompt { get; set; }

    /// <summary>
    /// 測試執行時間
    /// </summary>
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}
