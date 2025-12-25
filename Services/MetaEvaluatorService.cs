namespace PromptAgent.Services;

using PromptAgent.Models;

/// <summary>
/// Evaluator 性能策略
/// </summary>
public enum EvaluatorStrategy
{
    /// <summary>標準策略</summary>
    Standard,
    /// <summary>保守策略 - 小幅修改</summary>
    Conservative,
    /// <summary>激進策略 - 大膽嘗試</summary>
    Aggressive,
    /// <summary>專注穩定性</summary>
    StabilityFocus,
    /// <summary>專注正確性</summary>
    CorrectnessFocus
}

/// <summary>
/// Meta-Evaluator 服務 - 追蹤 Evaluator 表現並動態調整策略
/// </summary>
public class MetaEvaluatorService
{
    private readonly ILogger<MetaEvaluatorService> _logger;
    
    // 追蹤歷史
    private readonly List<EvaluationRecord> _history = [];
    private EvaluatorStrategy _currentStrategy = EvaluatorStrategy.Standard;
    
    // 策略調整閾值
    private const int MinRoundsForAnalysis = 2;
    private const int ConsecutiveDeclineThreshold = 2;
    private const int StagnationThreshold = 3;

    public MetaEvaluatorService(ILogger<MetaEvaluatorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 目前使用的策略
    /// </summary>
    public EvaluatorStrategy CurrentStrategy => _currentStrategy;
    
    /// <summary>
    /// 取得策略說明
    /// </summary>
    public string StrategyDescription => _currentStrategy switch
    {
        EvaluatorStrategy.Conservative => "保守模式：小幅漸進式修改",
        EvaluatorStrategy.Aggressive => "激進模式：大膽嘗試新方向",
        EvaluatorStrategy.StabilityFocus => "穩定性優先：專注減少輸出變異",
        EvaluatorStrategy.CorrectnessFocus => "正確性優先：專注提升答案品質",
        _ => "標準模式：平衡優化"
    };

    /// <summary>
    /// 記錄一輪評估結果
    /// </summary>
    public void RecordRound(TestResult result, string previousPrompt, string optimizedPrompt)
    {
        var record = new EvaluationRecord
        {
            Round = _history.Count + 1,
            StabilityScore = result.StabilityScore,
            CorrectnessScore = result.CorrectnessScore,
            AverageScore = (result.StabilityScore + result.CorrectnessScore) / 2,
            PreviousPrompt = previousPrompt,
            OptimizedPrompt = optimizedPrompt,
            Timestamp = DateTime.UtcNow
        };
        
        _history.Add(record);
        _logger.LogInformation(
            "Recorded round {Round}: Stability={Stability}, Correctness={Correctness}, Avg={Avg}",
            record.Round, record.StabilityScore, record.CorrectnessScore, record.AverageScore);
        
        // 分析並調整策略
        AnalyzeAndAdapt();
    }

    /// <summary>
    /// 分析歷史並調整策略
    /// </summary>
    private void AnalyzeAndAdapt()
    {
        if (_history.Count < MinRoundsForAnalysis)
        {
            return;
        }

        var recentRounds = _history.TakeLast(5).ToList();
        var previousStrategy = _currentStrategy;

        // 檢測連續下降
        int consecutiveDeclines = 0;
        for (int i = recentRounds.Count - 1; i > 0; i--)
        {
            if (recentRounds[i].AverageScore < recentRounds[i - 1].AverageScore)
            {
                consecutiveDeclines++;
            }
            else
            {
                break;
            }
        }

        // 檢測停滯（分數變化 < 3）
        bool isStagnant = recentRounds.Count >= StagnationThreshold &&
            Math.Abs(recentRounds.Last().AverageScore - recentRounds.First().AverageScore) < 3;

        // 檢測穩定性問題
        var lastRound = recentRounds.Last();
        bool hasStabilityIssue = lastRound.StabilityScore < lastRound.CorrectnessScore - 15;
        bool hasCorrectnessIssue = lastRound.CorrectnessScore < lastRound.StabilityScore - 15;

        // 決定策略
        if (consecutiveDeclines >= ConsecutiveDeclineThreshold)
        {
            // 連續下降 → 切換到保守模式
            _currentStrategy = EvaluatorStrategy.Conservative;
            _logger.LogWarning("Detected {Count} consecutive declines, switching to Conservative strategy",
                consecutiveDeclines);
        }
        else if (isStagnant)
        {
            // 停滯 → 切換到激進模式
            _currentStrategy = EvaluatorStrategy.Aggressive;
            _logger.LogWarning("Detected stagnation, switching to Aggressive strategy");
        }
        else if (hasStabilityIssue)
        {
            _currentStrategy = EvaluatorStrategy.StabilityFocus;
            _logger.LogInformation("Stability issue detected, focusing on stability");
        }
        else if (hasCorrectnessIssue)
        {
            _currentStrategy = EvaluatorStrategy.CorrectnessFocus;
            _logger.LogInformation("Correctness issue detected, focusing on correctness");
        }
        else if (_currentStrategy != EvaluatorStrategy.Standard && 
                 recentRounds.Count >= 2 &&
                 recentRounds.Last().AverageScore > recentRounds[^2].AverageScore + 5)
        {
            // 有明顯改善 → 回到標準模式
            _currentStrategy = EvaluatorStrategy.Standard;
            _logger.LogInformation("Good improvement detected, returning to Standard strategy");
        }

        if (_currentStrategy != previousStrategy)
        {
            _logger.LogInformation("Strategy changed: {Previous} → {Current}",
                previousStrategy, _currentStrategy);
        }
    }

    /// <summary>
    /// 根據目前策略取得調整後的 Evaluator 指令
    /// </summary>
    public string GetStrategyInstructions()
    {
        return _currentStrategy switch
        {
            EvaluatorStrategy.Conservative => """
                
                ## ⚠️ 當前策略：保守模式
                
                之前的優化導致分數下降，請採用更保守的方式：
                - 只做最小的必要修改
                - 保留原 Prompt 的核心結構
                - 每次只嘗試修正一個問題
                - 如果不確定，寧可不改
                """,
            
            EvaluatorStrategy.Aggressive => """
                
                ## 🚀 當前策略：激進模式
                
                分數停滯，需要突破性的改變：
                - 嘗試完全不同的表達方式
                - 重新思考 Prompt 的結構
                - 加入新的約束或範例
                - 不要害怕大幅修改
                """,
            
            EvaluatorStrategy.StabilityFocus => """
                
                ## 🎯 當前策略：穩定性優先
                
                穩定性分數較低，請專注於：
                - 減少輸出的變異性
                - 加入更明確的格式要求
                - 限制回答的長度和範圍
                - 使用更具體的指令詞
                """,
            
            EvaluatorStrategy.CorrectnessFocus => """
                
                ## 🎯 當前策略：正確性優先
                
                正確性分數較低，請專注於：
                - 改善答案的準確度
                - 加入更多上下文和約束
                - 明確說明預期的答案格式
                - 考慮加入範例
                """,
            
            _ => "" // Standard 不需要額外指令
        };
    }

    /// <summary>
    /// 取得歷史摘要
    /// </summary>
    public MetaEvaluatorSummary GetSummary()
    {
        if (_history.Count == 0)
        {
            return new MetaEvaluatorSummary();
        }

        var first = _history.First();
        var last = _history.Last();
        var best = _history.MaxBy(r => r.AverageScore)!;

        return new MetaEvaluatorSummary
        {
            TotalRounds = _history.Count,
            InitialScore = first.AverageScore,
            CurrentScore = last.AverageScore,
            BestScore = best.AverageScore,
            BestRound = best.Round,
            TotalImprovement = last.AverageScore - first.AverageScore,
            CurrentStrategy = _currentStrategy,
            StrategyDescription = StrategyDescription
        };
    }

    /// <summary>
    /// 重置歷史（新的測試案例時呼叫）
    /// </summary>
    public void Reset()
    {
        _history.Clear();
        _currentStrategy = EvaluatorStrategy.Standard;
        _logger.LogInformation("MetaEvaluator reset to Standard strategy");
    }
}

/// <summary>
/// 評估記錄
/// </summary>
public class EvaluationRecord
{
    public int Round { get; set; }
    public int StabilityScore { get; set; }
    public int CorrectnessScore { get; set; }
    public int AverageScore { get; set; }
    public string PreviousPrompt { get; set; } = string.Empty;
    public string OptimizedPrompt { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Meta-Evaluator 摘要
/// </summary>
public class MetaEvaluatorSummary
{
    public int TotalRounds { get; set; }
    public int InitialScore { get; set; }
    public int CurrentScore { get; set; }
    public int BestScore { get; set; }
    public int BestRound { get; set; }
    public int TotalImprovement { get; set; }
    public EvaluatorStrategy CurrentStrategy { get; set; }
    public string StrategyDescription { get; set; } = string.Empty;
}
