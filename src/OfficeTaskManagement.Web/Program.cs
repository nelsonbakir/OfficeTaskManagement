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

// ── AI Agent Services (Phase 1) ───────────────────────────────────────────────
// GeminiAiService: core estimation with typed HttpClient
builder.Services.AddHttpClient<GeminiAiService>();
builder.Services.AddScoped<IGeminiAiService, GeminiAiService>();

// GeminiEmbeddingService: text-embedding-004 for Phase 3 RAG
builder.Services.AddHttpClient<GeminiEmbeddingService>();
builder.Services.AddScoped<IGeminiEmbeddingService, GeminiEmbeddingService>();

// Supporting AI services
builder.Services.AddScoped<ContextBuilderService>();
builder.Services.AddScoped<PmKnowledgeService>();
builder.Services.AddScoped<AiEstimationLogService>();

// ── Phase 3: Codebase RAG Services ───────────────────────────────────────────
// CodebaseRetrievalService: semantic search over indexed code chunks
builder.Services.AddScoped<OfficeTaskManagement.Services.Codebase.CodebaseRetrievalService>();

// CodebaseIndexingService: runs on startup, re-indexes changed files
builder.Services.AddSingleton<OfficeTaskManagement.Services.Codebase.CodebaseIndexingService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<OfficeTaskManagement.Services.Codebase.CodebaseIndexingService>());

// ── Phase 5: AI Accuracy + Background Jobs ────────────────────────────────
// Nightly job: back-fills ActualHours on AiEstimationLogs for completed tasks
builder.Services.AddHostedService<OfficeTaskManagement.Services.Ai.AiAccuracyUpdateService>();
// ─────────────────────────────────────────────────────────────────────────────
// ─────────────────────────────────────────────────────────────────────────────

// ── Phase 4: Multi-turn AI Copilot Services ───────────────────────────────────
builder.Services.AddHttpClient<OfficeTaskManagement.Services.Agent.AgentService>();
builder.Services.AddScoped<OfficeTaskManagement.Services.Agent.IAgentService,
                            OfficeTaskManagement.Services.Agent.AgentService>();
builder.Services.AddScoped<OfficeTaskManagement.Services.Agent.AgentConversationService>();
builder.Services.AddScoped<OfficeTaskManagement.Services.Agent.AgentToolDispatcher>();
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
