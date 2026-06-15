using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services.WorkflowEngine;
using TaskStatus = OfficeTaskManagement.Models.Enums.TaskStatus;

namespace OfficeTaskManagement.Tests.Services;

/// <summary>
/// Unit tests for <see cref="StageGateService"/>.
/// Uses EF Core InMemory provider to avoid PostgreSQL dependency.
/// </summary>
public class StageGateServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly StageGateService _sut;

    // ── Shared test fixtures ─────────────────────────────────────────────────
    private const string ResponsibleUserId  = "user-r";
    private const string AccountableUserId  = "user-a";
    private const string UnrelatedUserId    = "user-x";

    public StageGateServiceTests()
    {
        _db  = PostgresTestDb.CreateContextAsync().GetAwaiter().GetResult();
        _sut = new StageGateService(_db);
    }

    public void Dispose()
    {
        var dbName = _db.Database.GetDbConnection().Database;
        _db.Dispose();
        if (!string.IsNullOrEmpty(dbName))
        {
            PostgresTestDb.DropDatabaseAsync(dbName).GetAwaiter().GetResult();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private WorkflowStage MakeStage(StageGateType gate, bool requiresAccountable = false) => new()
    {
        Id              = 1,
        Name            = "Test Stage",
        GateType        = gate,
        RequiresAccountableSignoff = requiresAccountable
    };

    private TaskItem MakeSubTask(WorkflowStage stage, TaskStatus status,
        string assigneeId = ResponsibleUserId,
        string accountableId = AccountableUserId,
        bool isPaused = false,
        string? pauseReason = null) => new()
    {
        Id                = 10,
        Title             = "Test Sub-Task",
        Status            = status,
        AssigneeId        = assigneeId,
        AccountableUserId = accountableId,
        WorkflowStageId   = stage.Id,
        WorkflowStage     = stage,
        IsPaused          = isPaused,
        PauseReason       = pauseReason,
        ActualHours       = 0
    };

    // ── IsPaused tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task EnforceGate_PausedTask_ThrowsGovernanceBlock()
    {
        var stage   = MakeStage(StageGateType.None);
        var subTask = MakeSubTask(stage, TaskStatus.ToDo, isPaused: true, pauseReason: "Blocked by budget freeze");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnforceGateAsync(subTask, ResponsibleUserId));

        Assert.Contains("paused", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Blocked by budget freeze", ex.Message);
    }

    [Fact]
    public async Task EnforceGate_ParentWpPaused_ThrowsGovernanceBlock()
    {
        var stage  = MakeStage(StageGateType.None);
        var parent = new TaskItem { Id = 5, Title = "WP", IsPaused = true, PauseReason = "On hold" };
        _db.Tasks.Add(parent);
        await _db.SaveChangesAsync();

        var subTask = MakeSubTask(stage, TaskStatus.ToDo);
        subTask.ParentTaskId = parent.Id;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnforceGateAsync(subTask, ResponsibleUserId));

        Assert.Contains("parent Work Package is paused", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── RACI tests ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EnforceGate_NonAssignee_ThrowsRaciViolation()
    {
        var stage   = MakeStage(StageGateType.None);
        var subTask = MakeSubTask(stage, TaskStatus.ToDo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnforceGateAsync(subTask, UnrelatedUserId));

        Assert.Contains("RACI Violation", ex.Message);
    }

    [Fact]
    public async Task EnforceGate_AccountableSignoffRequired_NonAccountableActor_Throws()
    {
        var stage   = MakeStage(StageGateType.None, requiresAccountable: true);
        var subTask = MakeSubTask(stage, TaskStatus.ToDo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnforceGateAsync(subTask, ResponsibleUserId)); // R tries, A required

        Assert.Contains("Accountable", ex.Message);
    }

    [Fact]
    public async Task EnforceGate_AccountableSignoffRequired_AccountableActor_Passes()
    {
        var stage   = MakeStage(StageGateType.None, requiresAccountable: true);
        var subTask = MakeSubTask(stage, TaskStatus.Committed);

        // Should NOT throw — accountable user is passing the gate
        await _sut.EnforceGateAsync(subTask, AccountableUserId);
    }

    // ── CommittedOnly gate ────────────────────────────────────────────────────

    [Fact]
    public async Task EnforceGate_CommittedOnly_WrongStatus_Throws()
    {
        var stage   = MakeStage(StageGateType.CommittedOnly);
        var subTask = MakeSubTask(stage, TaskStatus.InProgress);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnforceGateAsync(subTask, ResponsibleUserId));

        Assert.Contains("Committed", ex.Message);
    }

    [Fact]
    public async Task EnforceGate_CommittedOnly_CorrectStatus_Passes()
    {
        var stage   = MakeStage(StageGateType.CommittedOnly);
        var subTask = MakeSubTask(stage, TaskStatus.Committed);

        await _sut.EnforceGateAsync(subTask, ResponsibleUserId); // no throw
    }

    // ── CommittedWithHours gate ───────────────────────────────────────────────

    [Fact]
    public async Task EnforceGate_CommittedWithHours_NoHours_Throws()
    {
        var stage   = MakeStage(StageGateType.CommittedWithHours);
        var subTask = MakeSubTask(stage, TaskStatus.Committed);
        subTask.ActualHours = 0;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnforceGateAsync(subTask, ResponsibleUserId));

        Assert.Contains("hours", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnforceGate_CommittedWithHours_HoursLogged_Passes()
    {
        var stage   = MakeStage(StageGateType.CommittedWithHours);
        var subTask = MakeSubTask(stage, TaskStatus.Committed);
        subTask.ActualHours = 4.5m;

        await _sut.EnforceGateAsync(subTask, ResponsibleUserId); // no throw
    }

    // ── CommittedWithPeerReview gate ──────────────────────────────────────────

    [Fact]
    public async Task EnforceGate_CommittedWithPeerReview_NoComment_Throws()
    {
        var stage   = MakeStage(StageGateType.CommittedWithPeerReview);
        var subTask = MakeSubTask(stage, TaskStatus.Reviewed);
        _db.Tasks.Add(subTask);
        await _db.SaveChangesAsync();
        // no comments added

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnforceGateAsync(subTask, ResponsibleUserId));

        Assert.Contains("comment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnforceGate_CommittedWithPeerReview_HasComment_Passes()
    {
        var stage   = MakeStage(StageGateType.CommittedWithPeerReview);
        var subTask = MakeSubTask(stage, TaskStatus.Reviewed);
        _db.Tasks.Add(subTask);
        _db.TaskComments.Add(new TaskComment { TaskId = subTask.Id, CommentText = "LGTM", UserId = AccountableUserId });
        await _db.SaveChangesAsync();

        await _sut.EnforceGateAsync(subTask, ResponsibleUserId); // no throw
    }

    [Fact]
    public async Task EnforceGate_CommittedWithPeerReview_WrongStatus_Throws()
    {
        var stage   = MakeStage(StageGateType.CommittedWithPeerReview);
        var subTask = MakeSubTask(stage, TaskStatus.Committed); // should be Reviewed

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnforceGateAsync(subTask, ResponsibleUserId));

        Assert.Contains("Reviewed", ex.Message);
    }

    // ── No stage context ──────────────────────────────────────────────────────

    [Fact]
    public async Task EnforceGate_NoStageContext_Passes()
    {
        var standaloneTask = new TaskItem
        {
            Id         = 99,
            Title      = "Standalone",
            Status     = TaskStatus.InProgress,
            AssigneeId = ResponsibleUserId
        };
        // No WorkflowStageId set — should pass without any checks
        await _sut.EnforceGateAsync(standaloneTask, UnrelatedUserId);
    }

    [Fact]
    public async Task EnforceGate_StageRoleRestricted_ActorHasRole_Passes()
    {
        var role = new AppRole { Id = "dev-role", Name = "Developer", HierarchyLevel = 5 };
        _db.Roles.Add(role);
        _db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = ResponsibleUserId, RoleId = role.Id });
        await _db.SaveChangesAsync();

        var stage = MakeStage(StageGateType.None);
        stage.RoleId = role.Id;
        var subTask = MakeSubTask(stage, TaskStatus.ToDo);

        // Act & Assert (should NOT throw)
        await _sut.EnforceGateAsync(subTask, ResponsibleUserId);
    }

    [Fact]
    public async Task EnforceGate_StageRoleRestricted_ActorDoesNotHaveRole_Throws()
    {
        var role = new AppRole { Id = "dev-role", Name = "Developer", HierarchyLevel = 5 };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        var stage = MakeStage(StageGateType.None);
        stage.RoleId = role.Id;
        var subTask = MakeSubTask(stage, TaskStatus.ToDo);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnforceGateAsync(subTask, ResponsibleUserId));
        Assert.Contains("Role Restriction", ex.Message);
    }

    [Fact]
    public async Task EnforceGate_StageRoleRestricted_ActorHasHigherPrivilegeRole_Passes()
    {
        var stageRole = new AppRole { Id = "dev-role", Name = "Developer", HierarchyLevel = 5 };
        var higherRole = new AppRole { Id = "pm-role", Name = "Project Manager", HierarchyLevel = 2 };
        _db.Roles.AddRange(stageRole, higherRole);
        _db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = ResponsibleUserId, RoleId = higherRole.Id });
        await _db.SaveChangesAsync();

        var stage = MakeStage(StageGateType.None);
        stage.RoleId = stageRole.Id;
        var subTask = MakeSubTask(stage, TaskStatus.ToDo);

        // Act & Assert (should NOT throw because PM hierarchy level 2 <= Dev hierarchy level 5)
        await _sut.EnforceGateAsync(subTask, ResponsibleUserId);
    }
}
