using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services.WorkflowEngine;
using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WorkflowEngineService"/>.
/// Covers: SpawnWorkflowSubTasksAsync, TransitionStageAsync,
/// SyncParentStatusAsync, IsPaused guard, PERT calculation.
/// Each test gets its own isolated InMemory database.
/// </summary>
public class WorkflowEngineServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly WorkflowEngineService _sut;
    private readonly StageGateService _gate;

    private const string PmUserId = "pm-user";
    private const string DevUserId = "dev-user";

    public WorkflowEngineServiceTests()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db   = new ApplicationDbContext(opts);
        _gate = new StageGateService(_db);
        _sut  = new WorkflowEngineService(_db, _gate);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(WorkflowTemplate template, TaskItem parent)> SeedWorkPackageAsync(
        int stageCount = 2, StageGateType gateType = StageGateType.None)
    {
        var template = new WorkflowTemplate
        {
            Name     = "Test Template",
            IsActive = true,
            Stages   = Enumerable.Range(1, stageCount).Select(i => new WorkflowStage
            {
                Name             = $"Stage {i}",
                Order            = i,
                GateType         = gateType,
                DefaultRoleTitle = "Developer",
                DependencyType   = StageDependency.FinishToStart
            }).ToList()
        };
        _db.WorkflowTemplates.Add(template);

        var parent = new TaskItem
        {
            Title             = "Parent WP Task",
            Status            = TaskStatus.Approved,
            CreatedById       = PmUserId,
            AccountableUserId = PmUserId,
            CreatedAt         = DateTime.UtcNow
        };
        _db.Tasks.Add(parent);
        await _db.SaveChangesAsync();

        return (template, parent);
    }

    // ── PERT Calculation ─────────────────────────────────────────────────────

    [Fact]
    public void CalculatePert_StandardFormula_ReturnsCorrectValue()
    {
        // (4 + 4*6 + 10) / 6 = (4 + 24 + 10) / 6 = 38/6 ≈ 6.33
        var result = _sut.CalculatePert(4, 6, 10);
        Assert.Equal(38m / 6m, result);
    }

    [Fact]
    public void CalculatePert_EqualValues_ReturnsSameValue()
    {
        var result = _sut.CalculatePert(5, 5, 5);
        Assert.Equal(5m, result);
    }

    // ── SpawnWorkflowSubTasksAsync ───────────────────────────────────────────

    [Fact]
    public async Task Spawn_CreatesOneSubTaskPerStage()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 3);

        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var subTasks = await _db.Tasks
            .Where(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null)
            .ToListAsync();

        Assert.Equal(3, subTasks.Count);
    }

    [Fact]
    public async Task Spawn_TransformsParentIntoWorkPackage()
    {
        var (template, parent) = await SeedWorkPackageAsync();

        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var updated = await _db.Tasks.FindAsync(parent.Id);
        Assert.True(updated!.IsWorkPackage);
        Assert.Equal(TaskType.WorkPackage, updated.Type);
        Assert.Equal(RaciRole.Accountable, updated.RaciRole);
        Assert.Null(updated.AssigneeId); // Responsible role delegated to stage activities
    }

    [Fact]
    public async Task Spawn_FirstStageIsToDo_RestAreNew()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 3);

        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var subTasks = await _db.Tasks
            .Include(t => t.WorkflowStage)
            .Where(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null)
            .OrderBy(t => t.WorkflowStage!.Order)
            .ToListAsync();

        Assert.Equal(TaskStatus.ToDo, subTasks[0].Status);
        Assert.Equal(TaskStatus.New,  subTasks[1].Status);
        Assert.Equal(TaskStatus.New,  subTasks[2].Status);
    }

    [Fact]
    public async Task Spawn_SetsParentStatusToInProgress()
    {
        var (template, parent) = await SeedWorkPackageAsync();

        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var updated = await _db.Tasks.FindAsync(parent.Id);
        Assert.Equal(TaskStatus.InProgress, updated!.Status);
    }

    [Fact]
    public async Task Spawn_WritesAuditHistoryOnParent()
    {
        var (template, parent) = await SeedWorkPackageAsync();

        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var history = await _db.TaskHistories
            .Where(h => h.TaskItemId == parent.Id)
            .ToListAsync();

        Assert.NotEmpty(history);
        Assert.Contains(history, h => h.FieldChanged == "WorkflowTemplate");
    }

    [Fact]
    public async Task Spawn_InvalidTemplate_Throws()
    {
        var (_, parent) = await SeedWorkPackageAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SpawnWorkflowSubTasksAsync(parent.Id, templateId: 9999));
    }

    [Fact]
    public async Task Spawn_ReSpawn_RemovesOldSubTasksFirst()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 2);
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        // Re-spawn with same template (should replace, not accumulate)
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var count = await _db.Tasks.CountAsync(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null);
        Assert.Equal(2, count);
    }

    // ── SyncParentStatusAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SyncParent_AllDone_SetsParentDone()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 2);
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var children = await _db.Tasks
            .Where(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null)
            .ToListAsync();
        foreach (var c in children) c.Status = TaskStatus.Done;
        await _db.SaveChangesAsync();

        await _sut.SyncParentStatusAsync(parent.Id, PmUserId);

        var updated = await _db.Tasks.FindAsync(parent.Id);
        Assert.Equal(TaskStatus.Done, updated!.Status);
    }

    [Fact]
    public async Task SyncParent_AnyCommitted_SetsParentCommitted()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 2);
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var children = await _db.Tasks
            .Where(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null)
            .ToListAsync();
        children[0].Status = TaskStatus.Committed;
        children[1].Status = TaskStatus.ToDo;
        await _db.SaveChangesAsync();

        await _sut.SyncParentStatusAsync(parent.Id, PmUserId);

        var updated = await _db.Tasks.FindAsync(parent.Id);
        Assert.Equal(TaskStatus.Committed, updated!.Status);
    }

    [Fact]
    public async Task SyncParent_RollsUpEffortHours()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 2);
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var children = await _db.Tasks
            .Where(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null)
            .ToListAsync();
        children[0].ActualHours = 3m;
        children[1].ActualHours = 5m;
        await _db.SaveChangesAsync();

        await _sut.SyncParentStatusAsync(parent.Id, PmUserId);

        var updated = await _db.Tasks.FindAsync(parent.Id);
        Assert.Equal(8m, updated!.ActualHours);
    }

    [Fact]
    public async Task SyncParent_NonWorkPackage_DoesNothing()
    {
        var standalone = new TaskItem
        {
            Title       = "Standalone",
            Status      = TaskStatus.InProgress,
            IsWorkPackage = false,
            CreatedById = PmUserId,
            CreatedAt   = DateTime.UtcNow
        };
        _db.Tasks.Add(standalone);
        await _db.SaveChangesAsync();

        // Should return early without modifying anything
        await _sut.SyncParentStatusAsync(standalone.Id, PmUserId);

        var unchanged = await _db.Tasks.FindAsync(standalone.Id);
        Assert.Equal(TaskStatus.InProgress, unchanged!.Status);
    }

    // ── TransitionStageAsync — IsPaused guard ────────────────────────────────

    [Fact]
    public async Task TransitionStage_PausedSubTask_ThrowsAndWritesAudit()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 2, gateType: StageGateType.None);
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var firstStage = await _db.Tasks
            .Include(t => t.WorkflowStage)
            .Where(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null)
            .OrderBy(t => t.WorkflowStage!.Order)
            .FirstAsync();

        // Pause the first stage
        firstStage.IsPaused    = true;
        firstStage.PauseReason = "Awaiting client sign-off";
        firstStage.AssigneeId  = DevUserId;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.TransitionStageAsync(firstStage.Id, DevUserId));

        Assert.Contains("paused", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify an audit record was written for the blocked attempt
        var auditEntry = await _db.TaskHistories
            .Where(h => h.TaskItemId == firstStage.Id && h.FieldChanged == "TransitionAttempt")
            .FirstOrDefaultAsync();
        Assert.NotNull(auditEntry);
        Assert.Equal("BLOCKED", auditEntry.NewValue);
    }

    // ── TransitionStageAsync — happy path (GateType.None) ───────────────────

    [Fact]
    public async Task TransitionStage_GateNone_MarksDoneAndActivatesNext()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 2, gateType: StageGateType.None);
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var stages = await _db.Tasks
            .Include(t => t.WorkflowStage)
            .Where(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null)
            .OrderBy(t => t.WorkflowStage!.Order)
            .ToListAsync();

        var first  = stages[0];
        var second = stages[1];

        // Assign the first stage to DevUserId (Responsible)
        first.AssigneeId        = DevUserId;
        first.AccountableUserId = PmUserId;
        await _db.SaveChangesAsync();

        await _sut.TransitionStageAsync(first.Id, DevUserId);

        var updatedFirst  = await _db.Tasks.FindAsync(first.Id);
        var updatedSecond = await _db.Tasks.FindAsync(second.Id);

        Assert.Equal(TaskStatus.Done,  updatedFirst!.Status);
        Assert.Equal(TaskStatus.ToDo,  updatedSecond!.Status);
        Assert.NotNull(updatedFirst.CompletedAt);
    }

    [Fact]
    public async Task TransitionStage_LastStage_ClosesParentWorkPackage()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 1, gateType: StageGateType.None);
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var onlyStage = await _db.Tasks
            .Include(t => t.WorkflowStage)
            .Where(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null)
            .FirstAsync();

        onlyStage.AssigneeId        = DevUserId;
        onlyStage.AccountableUserId = PmUserId;
        await _db.SaveChangesAsync();

        await _sut.TransitionStageAsync(onlyStage.Id, DevUserId);

        var updatedParent = await _db.Tasks.FindAsync(parent.Id);
        Assert.Equal(TaskStatus.Done, updatedParent!.Status);
    }

    [Fact]
    public async Task TransitionStage_SetsCompletedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 2, gateType: StageGateType.None);
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var first = await _db.Tasks
            .Include(t => t.WorkflowStage)
            .Where(t => t.ParentTaskId == parent.Id && t.WorkflowStageId != null)
            .OrderBy(t => t.WorkflowStage!.Order)
            .FirstAsync();

        first.AssigneeId        = DevUserId;
        first.AccountableUserId = PmUserId;
        await _db.SaveChangesAsync();

        await _sut.TransitionStageAsync(first.Id, DevUserId);

        var updated = await _db.Tasks.FindAsync(first.Id);
        Assert.NotNull(updated!.CompletedAt);
        Assert.True(updated.CompletedAt >= before);
    }

    // ── GetWorkPackageSummaryAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetWorkPackageSummary_ReturnsStageSummaries()
    {
        var (template, parent) = await SeedWorkPackageAsync(stageCount: 2);
        await _sut.SpawnWorkflowSubTasksAsync(parent.Id, template.Id);

        var summary = await _sut.GetWorkPackageSummaryAsync(parent.Id);

        Assert.Equal(parent.Id, summary.ParentTaskId);
        Assert.Equal(2, summary.Stages.Count);
        Assert.All(summary.Stages, s => Assert.False(string.IsNullOrEmpty(s.StageName)));
    }

    [Fact]
    public async Task GetWorkPackageSummary_InvalidParent_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetWorkPackageSummaryAsync(parentTaskId: 9999));
    }
}
