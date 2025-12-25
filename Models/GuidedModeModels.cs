namespace PromptAgent.Models;

/// <summary>
/// 回答分群 - 將相似的回答歸類
/// </summary>
public class ResponseCluster
{
    /// <summary>
    /// 群組名稱 (如「簡潔版」「詳細版」)
    /// </summary>
    public string ClusterName { get; set; } = string.Empty;

    /// <summary>
    /// 群組描述 (AI 生成的特徵描述)
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 屬於此群的回答索引
    /// </summary>
    public List<int> ResponseIndices { get; set; } = [];

    /// <summary>
    /// 代表性回答內容預覽
    /// </summary>
    public string PreviewContent { get; set; } = string.Empty;
}

/// <summary>
/// 差異分析結果
/// </summary>
public class DifferenceAnalysis
{
    /// <summary>
    /// 回答分群列表
    /// </summary>
    public List<ResponseCluster> Clusters { get; set; } = [];

    /// <summary>
    /// 差異摘要
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// AI 建議的優化方向
    /// </summary>
    public List<string> SuggestedDirections { get; set; } = [];
}

/// <summary>
/// 用戶反饋
/// </summary>
public class UserFeedback
{
    /// <summary>
    /// 選擇的風格群組名稱
    /// </summary>
    public string? SelectedCluster { get; set; }

    /// <summary>
    /// 自訂反饋文字
    /// </summary>
    public string? CustomFeedback { get; set; }

    /// <summary>
    /// 是否有有效的反饋
    /// </summary>
    public bool HasFeedback => !string.IsNullOrWhiteSpace(SelectedCluster) || 
                                !string.IsNullOrWhiteSpace(CustomFeedback);
}
