# 📝 Future Roadmap / TODO List

## 🚀 New Features

### 1. 📊 Prompt A/B Test Mode
- Compare two different System Prompts side-by-side.
- Run the same test cases against both prompts.
- Visualize performance differences (Stability, Correctness).

### 2. 💾 History & Version Control
- Auto-save optimization history to local storage or JSON.
- "Before / After" diff view for prompts.
- Rollback capability to previous versions.

### 3. 💰 Token Analysis & Cost Estimation
- Track token usage per test run.
- Estimate costs based on model pricing (e.g., GPT-4o vs mini).
- Calculate "Cost per Success" metrics.

### 4. ✨ Few-Shot Auto-Generation
- User provides 1 example -> AI generates 3-5 similar examples.
- Automatically insert Few-Shot examples into System Prompt.
- Dynamic selection of examples based on category.

### 5. 📥 Test Case Import/Export
- Import test cases from CSV/JSON.
- Export test reports to PDF/Markdown.
- Shareable test configurations.

## 🛠️ Tech Debt & Improvements
- [ ] Refactor `PromptTest.razor` into smaller components.
- [ ] Add Unit Tests for `MetaEvaluatorService`.
- [ ] Support more LLM Providers (Claude, Gemini) via standardized interface.
