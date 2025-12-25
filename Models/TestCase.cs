namespace PromptAgent.Models;

/// <summary>
/// 測試案例模型
/// </summary>
public class TestCase
{
    /// <summary>
    /// 唯一識別碼
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 系統提示詞 (對應 MAF 的 instructions)
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 測試問題
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// 預期答案
    /// </summary>
    public string ExpectedAnswer { get; set; } = string.Empty;

    /// <summary>
    /// 執行次數（預設 3 次以測試穩定性）
    /// </summary>
    public int ExecutionCount { get; set; } = 3;

    /// <summary>
    /// Temperature (0-2)，0 = 穩定確定性，1 = 標準，2 = 高創意
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// 建立時間
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
