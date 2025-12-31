# PromptAgent

> Prompt 優化測試系統 — 使用 Microsoft Agent Framework 進行智慧評估與多輪優化

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?logo=blazor)
![Azure OpenAI](https://img.shields.io/badge/Azure-OpenAI-0078D4?logo=microsoft-azure)
![MAF](https://img.shields.io/badge/Microsoft-Agent_Framework-purple)

PromptAgent 是一個 Prompt 品質測試與優化工具。透過多輪平行執行與 AI 評估，快速找到最佳 Prompt 配置。

![Prompt Test Page](docs/prompt_test_page.png)

## Features

- **Multi-Agent Parallel Execution** — 使用 `Task.WhenAll` 同時執行多個 Agent，測試 Prompt 穩定性
- **GAI Feasibility Analysis** — 智慧分析需求適合 AI、傳統程式、還是人工處理
- **Prompt Version Control** — 像 Git 一樣管理 Prompt 版本，支援回滾與 Diff 比較
- **Smart Correction Mode** — 全自動多輪優化，達標自動停止
- **Guided Mode** — 每輪暫停讓用戶選擇偏好的回答風格
- **AI Example Generation** — LLM 動態生成測試範例

## Test Modes

| Mode | Description | Use Case |
|------|-------------|----------|
| Normal | Single execution + AI evaluation | Quick validation |
| Smart Correction | Fully automatic multi-round | Trust AI judgment |
| Guided | Pause each round for user input | Precise control |

## Quick Start

### 1. Clone

```bash
git clone https://github.com/breezy89757/PromptAgent.git
cd PromptAgent
```

### 2. Configure

```bash
cp appsettings.template.json appsettings.json
```

Edit `appsettings.json`:

```json
{
  "AzureOpenAI": {
    "Provider": "Azure",
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "your-api-key",
    "DeploymentName": "gpt-4o-mini",
    "EvaluatorDeploymentName": "gpt-4o"
  }
}
```

### 3. Run

```bash
dotnet run
```

Open http://localhost:5036

## Project Structure

```
PromptAgent/
├── Models/           # Data models
├── Services/         # Business logic (Agent, Evaluation, Versioning)
├── Components/       # Blazor pages
└── appsettings.json  # Configuration
```

## Data Storage

All data is stored in **browser LocalStorage**:

| Feature | Key | Limit |
|---------|-----|-------|
| Test History | `prompt_test_history` | 30 records |
| GAI Evaluation | `gai_evaluation_history` | 20 records |
| Version Projects | `prompt_projects` | Unlimited |

> [!NOTE]
> Clearing browser data will delete these records.

## License

MIT
