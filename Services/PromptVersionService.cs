using Blazored.LocalStorage;
using PromptAgent.Models;

namespace PromptAgent.Services;

/// <summary>
/// Prompt 版本控制服務 - 使用 LocalStorage 儲存
/// </summary>
public class PromptVersionService
{
    private const string PROJECTS_KEY = "prompt_projects";
    private const string VERSIONS_KEY_PREFIX = "prompt_versions_";
    
    private readonly ILocalStorageService _localStorage;
    
    public PromptVersionService(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }
    
    // ===== 專案管理 =====
    
    public async Task<List<PromptProject>> GetProjectsAsync()
    {
        return await _localStorage.GetItemAsync<List<PromptProject>>(PROJECTS_KEY) ?? new();
    }
    
    public async Task<PromptProject?> GetProjectAsync(string projectId)
    {
        var projects = await GetProjectsAsync();
        return projects.FirstOrDefault(p => p.Id == projectId);
    }
    
    public async Task<PromptProject> CreateProjectAsync(string name)
    {
        var projects = await GetProjectsAsync();
        var project = new PromptProject
        {
            Name = name,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        projects.Insert(0, project);
        await _localStorage.SetItemAsync(PROJECTS_KEY, projects);
        return project;
    }
    
    public async Task DeleteProjectAsync(string projectId)
    {
        var projects = await GetProjectsAsync();
        projects.RemoveAll(p => p.Id == projectId);
        await _localStorage.SetItemAsync(PROJECTS_KEY, projects);
        
        // 刪除該專案的所有版本
        await _localStorage.RemoveItemAsync($"{VERSIONS_KEY_PREFIX}{projectId}");
    }
    
    public async Task UpdateProjectAsync(PromptProject project)
    {
        var projects = await GetProjectsAsync();
        var index = projects.FindIndex(p => p.Id == project.Id);
        if (index >= 0)
        {
            project.UpdatedAt = DateTime.Now;
            projects[index] = project;
            await _localStorage.SetItemAsync(PROJECTS_KEY, projects);
        }
    }
    
    // ===== 版本管理 =====
    
    public async Task<List<PromptVersion>> GetVersionsAsync(string projectId)
    {
        return await _localStorage.GetItemAsync<List<PromptVersion>>($"{VERSIONS_KEY_PREFIX}{projectId}") ?? new();
    }
    
    public async Task<PromptVersion?> GetVersionAsync(string projectId, string versionId)
    {
        var versions = await GetVersionsAsync(projectId);
        return versions.FirstOrDefault(v => v.Id == versionId);
    }
    
    public async Task<PromptVersion> SaveVersionAsync(string projectId, string systemPrompt, string question, string expectedAnswer, 
        int? stabilityScore = null, int? correctnessScore = null, string? note = null)
    {
        var versions = await GetVersionsAsync(projectId);
        var nextVersionNumber = versions.Count > 0 ? versions.Max(v => v.VersionNumber) + 1 : 1;
        
        var version = new PromptVersion
        {
            ProjectId = projectId,
            VersionNumber = nextVersionNumber,
            SystemPrompt = systemPrompt,
            Question = question,
            ExpectedAnswer = expectedAnswer,
            StabilityScore = stabilityScore,
            CorrectnessScore = correctnessScore,
            Note = note ?? "",
            CreatedAt = DateTime.Now
        };
        
        versions.Insert(0, version);
        await _localStorage.SetItemAsync($"{VERSIONS_KEY_PREFIX}{projectId}", versions);
        
        // 更新專案資訊
        var project = await GetProjectAsync(projectId);
        if (project != null)
        {
            project.CurrentVersionId = version.Id;
            project.VersionCount = versions.Count;
            await UpdateProjectAsync(project);
        }
        
        return version;
    }
    
    public async Task UpdateVersionTagsAsync(string projectId, string versionId, List<string> tags)
    {
        var versions = await GetVersionsAsync(projectId);
        var version = versions.FirstOrDefault(v => v.Id == versionId);
        if (version != null)
        {
            // 如果標記為 best，移除其他版本的 best 標籤
            if (tags.Contains("best"))
            {
                foreach (var v in versions.Where(v => v.Id != versionId))
                {
                    v.Tags.Remove("best");
                }
            }
            
            version.Tags = tags;
            await _localStorage.SetItemAsync($"{VERSIONS_KEY_PREFIX}{projectId}", versions);
        }
    }
    
    public async Task<PromptVersion?> GetBestVersionAsync(string projectId)
    {
        var versions = await GetVersionsAsync(projectId);
        return versions.FirstOrDefault(v => v.IsBest) ?? versions.FirstOrDefault();
    }
}
