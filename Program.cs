using PromptAgent.Components;
using PromptAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure Azure OpenAI settings
builder.Services.Configure<AzureOpenAISettings>(
    builder.Configuration.GetSection("AzureOpenAI"));

// Register application services
// 使用 Singleton 讓 AzureOpenAIClient 可以重用 HTTP 連線
builder.Services.AddSingleton<AgentService>();
builder.Services.AddSingleton<EvaluationService>();
builder.Services.AddSingleton<ExampleGeneratorService>();

// Meta-Evaluator 使用 Scoped，每個用戶 session 獨立追蹤
builder.Services.AddScoped<MetaEvaluatorService>();

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
