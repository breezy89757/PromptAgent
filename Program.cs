using Azure;
using Azure.AI.OpenAI;
using Blazored.LocalStorage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using PromptAgent.Components;
using PromptAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure Azure OpenAI settings
builder.Services.Configure<AzureOpenAISettings>(
    builder.Configuration.GetSection("AzureOpenAI"));

// Register IChatClient for Microsoft Agent Framework
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AzureOpenAISettings>>().Value;
    return new AzureOpenAIClient(
        new Uri(settings.Endpoint),
        new AzureKeyCredential(settings.ApiKey))
        .GetChatClient(settings.DeploymentName)
        .AsIChatClient();
});

// Register application services
// 使用 Singleton 讓 AzureOpenAIClient 可以重用 HTTP 連線
builder.Services.AddSingleton<AgentService>();
builder.Services.AddSingleton<EvaluationService>();
builder.Services.AddSingleton<ExampleGeneratorService>();

// Register HttpClient for CodeAdvisor (MS best practice: IHttpClientFactory)
builder.Services.AddHttpClient("CodeAdvisor", client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddSingleton<GAIEvaluatorService>();

// Meta-Evaluator 使用 Scoped，每個用戶 session 獨立追蹤
builder.Services.AddScoped<MetaEvaluatorService>();

// OpenTelemetry 監控 - 追蹤 AI 呼叫延遲
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.Agents.AI")   // MAF 內建追蹤
        .AddSource("PromptAgent.AI")        // 自訂 AI 追蹤
        .AddSource("System.Net.Http")       // HTTP 請求追蹤
        .AddAspNetCoreInstrumentation()     // ASP.NET Core 追蹤
        .AddConsoleExporter());             // 輸出到 console

// 評估歷史紀錄 - 使用瀏覽器 LocalStorage
builder.Services.AddBlazoredLocalStorage();

// Prompt 版本控制服務
builder.Services.AddScoped<PromptVersionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
