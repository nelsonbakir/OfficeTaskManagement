using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;


using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Settings;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.Services.Ai;
using OfficeTaskManagement.Services.WorkflowEngine;
using OfficeTaskManagement.Services.Authorization;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseVector()));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentity<User, AppRole>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();

builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? ""))
        };
    });

builder.Services.AddHttpClient<OfficeTaskManagement.Services.IGeminiAnalyticsService, OfficeTaskManagement.Services.GeminiAnalyticsService>();

// Media Service Configuration
builder.Services.Configure<MediaSettings>(builder.Configuration.GetSection(MediaSettings.SectionName));
var mediaProvider = builder.Configuration.GetValue<string>("MediaSettings:Provider") ?? "Local";

if (mediaProvider.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IMediaService, S3MediaService>();
}
else
{
    builder.Services.AddScoped<IMediaService, LocalMediaService>();
}

// Resource Management Services
builder.Services.AddScoped<IResourceService, ResourceService>();
builder.Services.AddScoped<ICapacityPlanningService, CapacityPlanningService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();

// Workflow Engine (RACI task lifecycle)
builder.Services.AddScoped<StageGateService>();
builder.Services.AddScoped<IWorkflowEngineService, WorkflowEngineService>();
builder.Services.AddScoped<KanbanGovernanceService>();

// P3-2: Lag Scheduling — promotes stage sub-tasks to ToDo when PlannedStartDate elapses
builder.Services.AddHostedService<LagSchedulingService>();

// ── Onboarding Wizard Services ────────────────────────────────────────────
builder.Services.AddScoped<OfficeTaskManagement.Services.Onboarding.IOnboardingOrchestrationService,
                            OfficeTaskManagement.Services.Onboarding.OnboardingOrchestrationService>();
builder.Services.AddScoped<OfficeTaskManagement.Services.Onboarding.OnboardingSessionService>();
// ─────────────────────────────────────────────────────────────────────────

// GeminiAiService: core estimation with typed HttpClient
builder.Services.AddHttpClient<GeminiAiService>(client => {
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddScoped<IGeminiAiService>(sp => sp.GetRequiredService<GeminiAiService>());
builder.Services.AddScoped<OfficeTaskManagement.Services.Ai.AiQueuedJobService>();

// Dynamic Embedding Service Registration based on config provider (Gemini, Ollama, LocalMock)
var provider = builder.Configuration["Gemini:Provider"] 
    ?? builder.Configuration["Gemini:EmbeddingProvider"] 
    ?? "Gemini";
if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(provider, "Gemma", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<OllamaEmbeddingService>(client => {
        client.Timeout = TimeSpan.FromMinutes(10);
    });
    builder.Services.AddScoped<IGeminiEmbeddingService>(sp => sp.GetRequiredService<OllamaEmbeddingService>());
}
else if (string.Equals(provider, "OpenVINO", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(provider, "DirectML", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<OpenVINOEmbeddingService>(client => {
        client.Timeout = TimeSpan.FromMinutes(10);
    });
    builder.Services.AddScoped<IGeminiEmbeddingService>(sp => sp.GetRequiredService<OpenVINOEmbeddingService>());
}
else if (string.Equals(provider, "LocalMock", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IGeminiEmbeddingService, LocalMockEmbeddingService>();
}
else
{
    builder.Services.AddHttpClient<GeminiEmbeddingService>();
    builder.Services.AddScoped<IGeminiEmbeddingService>(sp => sp.GetRequiredService<GeminiEmbeddingService>());
}

// Supporting AI services
builder.Services.AddScoped<ContextBuilderService>();
builder.Services.AddScoped<PmKnowledgeService>();
builder.Services.AddScoped<AiEstimationLogService>();

// ── Phase 3: Codebase RAG Services ───────────────────────────────────────────
// GitCloneService: clones remote repositories
builder.Services.AddScoped<OfficeTaskManagement.Services.Codebase.GitCloneService>();

// CodebaseRetrievalService: semantic search over indexed code chunks
builder.Services.AddScoped<OfficeTaskManagement.Services.Codebase.CodebaseRetrievalService>();

// CodebaseIndexingService: runs on startup, re-indexes changed files
builder.Services.AddSingleton<OfficeTaskManagement.Services.Codebase.CodebaseIndexingService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<OfficeTaskManagement.Services.Codebase.CodebaseIndexingService>());

// ── Phase 5: AI Accuracy + Background Jobs ────────────────────────────────
// Nightly job: back-fills ActualHours on AiEstimationLogs for completed tasks
builder.Services.AddHostedService<OfficeTaskManagement.Services.Ai.AiAccuracyUpdateService>();

// KF-4: Risk Radar — proactive risk signal scanner (runs every 30 min)
builder.Services.AddHostedService<OfficeTaskManagement.Services.Ai.RiskRadarService>();

// KF-5: PM Status Report generator
builder.Services.AddScoped<OfficeTaskManagement.Services.Ai.PmReportService>();

// ── Phase 4: Multi-turn AI Copilot Services ───────────────────────────────────
builder.Services.AddHttpClient<OfficeTaskManagement.Services.Agent.AgentService>(client => {
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddScoped<OfficeTaskManagement.Services.Agent.IAgentService>(sp =>
    sp.GetRequiredService<OfficeTaskManagement.Services.Agent.AgentService>());
builder.Services.AddScoped<OfficeTaskManagement.Services.Agent.AgentConversationService>();
builder.Services.AddScoped<OfficeTaskManagement.Services.Agent.AgentToolDispatcher>();
builder.Services.AddScoped<OfficeTaskManagement.Services.Agent.MentionSearchService>();
builder.Services.AddScoped<OfficeTaskManagement.Services.Agent.MentionContextResolver>();
// ─────────────────────────────────────────────────────────────────────────────

// In-process caching for heatmap and utilization data (15-min sliding window)
builder.Services.AddMemoryCache();

// Permission-based authorization
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddAuthorization(options =>
{
    foreach (var key in Permissions.All)
    {
        options.AddPolicy($"permission:{key}",
            policy => policy.Requirements.Add(new PermissionRequirement(key)));
    }
});
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    PermissionAuthorizationHandler>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Seed Initial Data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();
        
        await SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
