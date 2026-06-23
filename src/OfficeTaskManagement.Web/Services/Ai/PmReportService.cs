using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OfficeTaskManagement.Services.Ai;

/// <summary>
/// KF-5: PM Status Report Generator.
/// Assembles a structured project health snapshot and returns a markdown
/// report string that the AI Copilot can display and offer as a PDF download.
///
/// The report covers:
///   - Executive Summary (RAG status: 🟢/🟡/🔴)
///   - Sprint Progress (velocity, burn trend)
///   - Risk Register (from Notifications of type AI_Risk)
///   - Resource Utilization
///   - Budget Status
///   - Next Steps
///
/// Spec: implementation_plan.md → KF-5 (PM Status Report)
/// </summary>
public class PmReportService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PmReportService> _logger;
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _config;
    private readonly Microsoft.AspNetCore.Identity.UI.Services.IEmailSender? _emailSender;

    public PmReportService(
        ApplicationDbContext db, 
        ILogger<PmReportService> logger,
        Microsoft.Extensions.Configuration.IConfiguration? config = null,
        Microsoft.AspNetCore.Identity.UI.Services.IEmailSender? emailSender = null)
    {
        _db          = db;
        _logger      = logger;
        _config      = config;
        _emailSender = emailSender;
    }

    /// <summary>
    /// Generates a PMP-grade markdown status report for a project.
    /// Returns the report as a markdown string ready for display in the AI Copilot chat bubble.
    /// </summary>
    public async Task<string> GenerateMarkdownReportAsync(int projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
            return $"⚠ Project #{projectId} not found.";

        var now = DateTime.UtcNow;

        // ── Task counts by status ────────────────────────────────────────────
        var taskCounts = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int totalTasks  = taskCounts.Sum(x => x.Count);
        int doneTasks   = taskCounts.Where(x => x.Status == Models.Enums.TaskStatus.Done).Sum(x => x.Count);
        int inProgress  = taskCounts.Where(x => x.Status == Models.Enums.TaskStatus.InProgress).Sum(x => x.Count);
        int blockedTasks = await _db.Tasks.AsNoTracking().CountAsync(t => t.ProjectId == projectId && t.IsPaused, ct);

        double completionPct = totalTasks > 0 ? Math.Round((double)doneTasks / totalTasks * 100, 1) : 0;

        // ── WBS counts ───────────────────────────────────────────────────────
        int epicCount    = await _db.Epics.AsNoTracking().CountAsync(e => e.ProjectId == projectId, ct);
        int featureCount = await _db.Features.AsNoTracking().CountAsync(f => f.Epic!.ProjectId == projectId, ct);
        int storyCount   = await _db.UserStories.AsNoTracking().CountAsync(s => s.Feature!.Epic!.ProjectId == projectId, ct);

        // ── Active sprint ────────────────────────────────────────────────────
        var activeSprint = await _db.Sprints
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.StartDate <= now && s.EndDate >= now)
            .FirstOrDefaultAsync(ct);

        string sprintSection = "No active sprint.";
        if (activeSprint is not null)
        {
            var sprintTaskCounts = await _db.Tasks
                .AsNoTracking()
                .Where(t => t.SprintId == activeSprint.Id)
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            int sprintTotal = sprintTaskCounts.Sum(x => x.Count);
            int sprintDone  = sprintTaskCounts.Where(x => x.Status == Models.Enums.TaskStatus.Done).Sum(x => x.Count);
            int sprintBurnPct = sprintTotal > 0 ? (int)Math.Round((double)sprintDone / sprintTotal * 100) : 0;

            decimal sprintEstimated = await _db.Tasks
                .AsNoTracking()
                .Where(t => t.SprintId == activeSprint.Id)
                .SumAsync(t => t.EstimatedHours, ct);

            decimal sprintActual = await _db.Tasks
                .AsNoTracking()
                .Where(t => t.SprintId == activeSprint.Id && t.ActualHours.HasValue)
                .SumAsync(t => t.ActualHours!.Value, ct);

            var daysLeft = (activeSprint.EndDate - now).Days;
            sprintSection = $"**Sprint:** {activeSprint.Name}  \n" +
                            $"**Period:** {activeSprint.StartDate:dd MMM} – {activeSprint.EndDate:dd MMM} ({daysLeft} day(s) left)  \n" +
                            $"**Progress:** {sprintDone}/{sprintTotal} tasks ({sprintBurnPct}% burned)  \n" +
                            $"**Hours:** {sprintActual:F1}h actual / {sprintEstimated:F1}h estimated";
        }

        // ── Risk signals (from Notifications) ───────────────────────────────
        var riskNotifications = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == "AI_Risk" && n.CreatedAt > now.AddDays(-7))
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .Select(n => new { n.Title, n.Message })
            .ToListAsync(ct);

        string riskSection = riskNotifications.Count == 0
            ? "✅ No recent AI risk alerts."
            : string.Join("\n", riskNotifications.Select(r => $"- **{r.Title}**: {r.Message}"));

        // ── Resource utilization (top 5 assigned) ───────────────────────────
        var resourceRows = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.AssigneeId != null)
            .GroupBy(t => new { t.AssigneeId, t.Assignee!.FullName })
            .Select(g => new
            {
                Name      = g.Key.FullName ?? "Unknown",
                Total     = g.Count(),
                Done      = g.Count(t => t.Status == Models.Enums.TaskStatus.Done),
                Active    = g.Count(t => t.Status == Models.Enums.TaskStatus.InProgress)
            })
            .OrderByDescending(x => x.Active)
            .Take(5)
            .ToListAsync(ct);

        string resourceSection = resourceRows.Count == 0
            ? "No tasks assigned yet."
            : string.Join("\n", resourceRows.Select(r =>
                $"| {r.Name} | {r.Total} | {r.Active} | {r.Done} |"));

        // ── Determine overall RAG status ─────────────────────────────────────
        string ragStatus;
        if (blockedTasks > 0 || completionPct < 20 && totalTasks > 10)
            ragStatus = "🔴 **RED** — Immediate attention required";
        else if (inProgress == 0 && doneTasks < totalTasks * 0.5 && totalTasks > 0)
            ragStatus = "🟡 **AMBER** — Monitor closely";
        else
            ragStatus = "🟢 **GREEN** — On track";

        // ── Assemble the report ──────────────────────────────────────────────
        var report = $"""
# 📋 PM Status Report — {project.Name}
*Generated: {now:dd MMM yyyy HH:mm} UTC*

---

## Executive Summary
**Overall Status:** {ragStatus}

| Metric | Value |
|--------|-------|
| Completion | {completionPct}% ({doneTasks}/{totalTasks} tasks) |
| Epics | {epicCount} |
| Features | {featureCount} |
| User Stories | {storyCount} |
| In Progress | {inProgress} |
| Blocked | {blockedTasks} |

---

## Sprint Status
{sprintSection}

---

## Risk Register *(last 7 days)*
{riskSection}

---

## Resource Utilization

| Team Member | Total Tasks | Active | Done |
|-------------|-------------|--------|------|
{resourceSection}

---

## Recommendations
{GenerateRecommendations(completionPct, blockedTasks, inProgress, riskNotifications.Count)}

---
*Report generated by AI Copilot · Use `/risk` for a full risk analysis · Type `/sprint` to optimize the current sprint*
""";

        // Configurable email delivery toggle
        bool emailEnabled = _config?.GetValue<bool>("PmReportSettings:EmailEnabled") ?? false;
        if (emailEnabled && _emailSender != null)
        {
            var recipient = _config?.GetValue<string>("PmReportSettings:RecipientEmail") ?? "pm-status@example.com";
            try
            {
                await _emailSender.SendEmailAsync(
                    recipient, 
                    $"[PM Status Report] {project.Name} - {ragStatus}", 
                    report);
                _logger.LogInformation("Status report email sent to {Recipient}", recipient);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send status report email");
            }
        }

        return report;
    }

    private static string GenerateRecommendations(
        double completionPct, int blocked, int inProgress, int riskCount)
    {
        var recs = new List<string>();

        if (blocked > 0)
            recs.Add($"- 🔴 **Resolve {blocked} blocked task(s) immediately** — blocked items cascade delays across dependents.");
        if (completionPct < 30)
            recs.Add("- 🟡 **Low completion rate** — review scope, re-estimate, or consider dropping non-critical backlog items.");
        if (inProgress == 0)
            recs.Add("- 🔵 **No tasks In Progress** — sprint may be stalled. Assign tasks or start a new sprint.");
        if (riskCount > 2)
            recs.Add($"- ⚠ **{riskCount} AI risk alerts** detected in the last 7 days — use `/risk` for details.");
        if (recs.Count == 0)
            recs.Add("- ✅ Project is healthy. Continue current pace and run `/risk` weekly to stay ahead of issues.");

        return string.Join("\n", recs);
    }

    /// <summary>
    /// Generates a PDF version of the status report using QuestPDF.
    /// </summary>
    public async Task<byte[]> GeneratePdfReportAsync(int projectId, CancellationToken ct = default)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
            return Array.Empty<byte>();

        var now = DateTime.UtcNow;

        // ── Task counts by status ────────────────────────────────────────────
        var taskCounts = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int totalTasks   = taskCounts.Sum(x => x.Count);
        int doneTasks    = taskCounts.Where(x => x.Status == Models.Enums.TaskStatus.Done).Sum(x => x.Count);
        int inProgress   = taskCounts.Where(x => x.Status == Models.Enums.TaskStatus.InProgress).Sum(x => x.Count);
        int blockedTasks = await _db.Tasks.AsNoTracking().CountAsync(t => t.ProjectId == projectId && t.IsPaused, ct);

        double completionPct = totalTasks > 0 ? Math.Round((double)doneTasks / totalTasks * 100, 1) : 0;

        // ── WBS counts ───────────────────────────────────────────────────────
        int epicCount    = await _db.Epics.AsNoTracking().CountAsync(e => e.ProjectId == projectId, ct);
        int featureCount = await _db.Features.AsNoTracking().CountAsync(f => f.Epic!.ProjectId == projectId, ct);
        int storyCount   = await _db.UserStories.AsNoTracking().CountAsync(s => s.Feature!.Epic!.ProjectId == projectId, ct);

        // ── Active sprint ────────────────────────────────────────────────────
        var activeSprint = await _db.Sprints
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.StartDate <= now && s.EndDate >= now)
            .FirstOrDefaultAsync(ct);

        string sprintName = "No Active Sprint";
        string sprintPeriod = "N/A";
        string sprintProgress = "N/A";
        string sprintHours = "N/A";

        if (activeSprint is not null)
        {
            var sprintTaskCounts = await _db.Tasks
                .AsNoTracking()
                .Where(t => t.SprintId == activeSprint.Id)
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            int sprintTotal = sprintTaskCounts.Sum(x => x.Count);
            int sprintDone  = sprintTaskCounts.Where(x => x.Status == Models.Enums.TaskStatus.Done).Sum(x => x.Count);
            int sprintBurnPct = sprintTotal > 0 ? (int)Math.Round((double)sprintDone / sprintTotal * 100) : 0;

            decimal sprintEstimated = await _db.Tasks
                .AsNoTracking()
                .Where(t => t.SprintId == activeSprint.Id)
                .SumAsync(t => t.EstimatedHours, ct);

            decimal sprintActual = await _db.Tasks
                .AsNoTracking()
                .Where(t => t.SprintId == activeSprint.Id && t.ActualHours.HasValue)
                .SumAsync(t => t.ActualHours!.Value, ct);

            var daysLeft = (activeSprint.EndDate - now).Days;
            sprintName = activeSprint.Name;
            sprintPeriod = $"{activeSprint.StartDate:dd MMM} – {activeSprint.EndDate:dd MMM} ({daysLeft} day(s) left)";
            sprintProgress = $"{sprintDone}/{sprintTotal} tasks ({sprintBurnPct}% burned)";
            sprintHours = $"{sprintActual:F1}h actual / {sprintEstimated:F1}h estimated";
        }

        // ── Risk signals (from Notifications) ───────────────────────────────
        var riskNotifications = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == "AI_Risk" && n.CreatedAt > now.AddDays(-7))
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .Select(n => new { n.Title, n.Message })
            .ToListAsync(ct);

        // ── Resource utilization (top 5 assigned) ───────────────────────────
        var resourceRows = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.AssigneeId != null)
            .GroupBy(t => new { t.AssigneeId, t.Assignee!.FullName })
            .Select(g => new
            {
                Name      = g.Key.FullName ?? "Unknown",
                Total     = g.Count(),
                Done      = g.Count(t => t.Status == Models.Enums.TaskStatus.Done),
                Active    = g.Count(t => t.Status == Models.Enums.TaskStatus.InProgress)
            })
            .OrderByDescending(x => x.Active)
            .Take(5)
            .ToListAsync(ct);

        // ── Determine overall RAG status ─────────────────────────────────────
        string ragStatus;
        string ragColor;
        if (blockedTasks > 0 || completionPct < 20 && totalTasks > 10)
        {
            ragStatus = "RED — Immediate attention required";
            ragColor = Colors.Red.Medium;
        }
        else if (inProgress == 0 && doneTasks < totalTasks * 0.5 && totalTasks > 0)
        {
            ragStatus = "AMBER — Monitor closely";
            ragColor = Colors.Orange.Medium;
        }
        else
        {
            ragStatus = "GREEN — On track";
            ragColor = Colors.Green.Medium;
        }

        var pdfData = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10));

                // Header
                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col => 
                    {
                        col.Item().Text($"PM STATUS REPORT — {project.Name.ToUpper()}").SemiBold().FontSize(18).FontColor(Colors.Blue.Darken3);
                        col.Item().Text($"Generated: {now.ToLocalTime():dd MMM yyyy HH:mm} • STRICTLY CONFIDENTIAL").FontSize(9).FontColor(Colors.Grey.Medium);
                    });
                });

                // Content
                page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                {
                    col.Spacing(20);

                    // 1. Executive Summary
                    col.Item().Text("1. Executive Summary").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken4);
                    col.Item().Text(text => 
                    {
                        text.Span("Overall Project Status: ").SemiBold();
                        text.Span(ragStatus).FontColor(ragColor).Bold();
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(3).Text("Metric").SemiBold();
                            header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(3).Text("Value").SemiBold();
                        });

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text("Completion Rate");
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text($"{completionPct}% ({doneTasks}/{totalTasks} tasks)");

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text("Epics / Features / User Stories");
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text($"{epicCount} / {featureCount} / {storyCount}");

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text("Active Tasks (In Progress)");
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(inProgress.ToString());

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text("Blocked Tasks");
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(blockedTasks.ToString()).FontColor(blockedTasks > 0 ? Colors.Red.Medium : Colors.Black);
                    });

                    // 2. Sprint Status
                    col.Item().Text("2. Sprint Status").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken4);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                        });

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text("Active Sprint").SemiBold();
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(sprintName);

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text("Period").SemiBold();
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(sprintPeriod);

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text("Progress").SemiBold();
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(sprintProgress);

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text("Effort Hours").SemiBold();
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(sprintHours);
                    });

                    // 3. Risks & Signals
                    col.Item().Text("3. Key Risk Registers (Last 7 Days)").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken4);
                    if (riskNotifications.Count == 0)
                    {
                        col.Item().Text("No active AI risks identified.").FontColor(Colors.Green.Darken2);
                    }
                    else
                    {
                        col.Item().Column(riskCol => 
                        {
                            riskCol.Spacing(5);
                            foreach (var risk in riskNotifications)
                            {
                                riskCol.Item().Text(rt => 
                                {
                                    rt.Span($"• {risk.Title}: ").SemiBold().FontColor(Colors.Red.Medium);
                                    rt.Span(risk.Message);
                                });
                            }
                        });
                    }

                    // 4. Resource Allocation
                    col.Item().Text("4. Top Resource Utilization").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken4);
                    if (resourceRows.Count == 0)
                    {
                        col.Item().Text("No active assignments in this project.");
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(3).Text("Team Member").SemiBold();
                                header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(3).Text("Total").SemiBold();
                                header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(3).Text("Active").SemiBold();
                                header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(3).Text("Done").SemiBold();
                            });

                            foreach (var r in resourceRows)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(r.Name);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(r.Total.ToString());
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(r.Active.ToString()).FontColor(r.Active > 3 ? Colors.Red.Medium : Colors.Black);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(r.Done.ToString());
                            }
                        });
                    }

                    // 5. AI Recommendations
                    col.Item().Text("5. AI Copilot PMP Recommendations").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken4);
                    col.Item().Background(Colors.Grey.Lighten5).Padding(10).Column(recCol =>
                    {
                        recCol.Spacing(5);
                        var recs = new List<string>();
                        if (blockedTasks > 0)
                            recs.Add($"Resolve {blockedTasks} blocked task(s) immediately to clear dependencies.");
                        if (completionPct < 30)
                            recs.Add("Low completion rate: recommend reviewing scope, re-estimating, or pruning non-critical user stories.");
                        if (inProgress == 0)
                            recs.Add("No tasks currently In Progress. Sprint may be stalled.");
                        if (riskNotifications.Count > 2)
                            recs.Add($"{riskNotifications.Count} AI risk alerts detected in last 7 days. Run /risk command in Copilot.");
                        if (recs.Count == 0)
                            recs.Add("Project is healthy. Keep up the current pace.");

                        foreach (var rec in recs)
                        {
                            recCol.Item().Text($"• {rec}").FontSize(9.5f);
                        }
                    });
                });

                // Footer
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Generated by AI Copilot | STRICTLY CONFIDENTIAL | Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();

        return pdfData;
    }
}
