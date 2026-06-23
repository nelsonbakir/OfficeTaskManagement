using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Ai;

namespace OfficeTaskManagement.Services.Agent
{
    public class MentionContextResolver
    {
        private readonly ApplicationDbContext _context;

        public MentionContextResolver(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<string>> ResolveAsync(MentionReference[] mentions, CancellationToken ct)
        {
            var blocks = new List<string>();
            if (mentions == null || mentions.Length == 0) return blocks;

            // Cap to maximum 5 mentions to prevent context bloating
            var limitedMentions = mentions.Take(5).ToList();

            foreach (var mention in limitedMentions)
            {
                var block = await ResolveSingleMentionAsync(mention, ct);
                if (!string.IsNullOrEmpty(block))
                {
                    blocks.Add(block);
                }
            }

            return blocks;
        }

        private async Task<string?> ResolveSingleMentionAsync(MentionReference mention, CancellationToken ct)
        {
            var type = mention.Type.ToLower().Trim();
            var idStr = mention.Id;

            try
            {
                switch (type)
                {
                    case "project":
                        if (int.TryParse(idStr, out int projId))
                        {
                            var proj = await _context.Projects
                                .Include(p => p.Epics)
                                .Include(p => p.Sprints)
                                .FirstOrDefaultAsync(p => p.Id == projId, ct);

                            if (proj != null)
                            {
                                var activeSprint = proj.Sprints.FirstOrDefault(s => s.IsActive);
                                var sb = new StringBuilder();
                                sb.AppendLine($"### @Project:{proj.Name} (ID: {proj.Id})");
                                sb.AppendLine($"- Description: {proj.Description ?? "No description"}");
                                sb.AppendLine($"- Epics Count: {proj.Epics.Count}");
                                sb.AppendLine($"- Active Sprint: {(activeSprint != null ? activeSprint.Name : "None")}");
                                return sb.ToString();
                            }
                        }
                        break;

                    case "epic":
                        if (int.TryParse(idStr, out int epicId))
                        {
                            var epic = await _context.Epics
                                .Include(e => e.Project)
                                .Include(e => e.Features)
                                .ThenInclude(f => f.Tasks)
                                .FirstOrDefaultAsync(e => e.Id == epicId, ct);

                            if (epic != null)
                            {
                                var featureNames = epic.Features.Select(f => f.Name).ToList();
                                var allTasks = epic.Features.SelectMany(f => f.Tasks).ToList();
                                var pertTotal = allTasks.Sum(t => t.PertEstimatedHours ?? 0);

                                var sb = new StringBuilder();
                                sb.AppendLine($"### @Epic:{epic.Name} (ID: {epic.Id})");
                                sb.AppendLine($"- Description: {epic.Description ?? "No description"}");
                                sb.AppendLine($"- Project: {(epic.Project != null ? epic.Project.Name : "None")}");
                                sb.AppendLine($"- Features: {(featureNames.Any() ? string.Join(", ", featureNames) : "None")}");
                                sb.AppendLine($"- Total Estimated Effort: {pertTotal:F1}h ({allTasks.Count} tasks)");
                                return sb.ToString();
                            }
                        }
                        break;

                    case "feature":
                        if (int.TryParse(idStr, out int featId))
                        {
                            var feat = await _context.Features
                                .Include(f => f.Epic)
                                .Include(f => f.UserStories)
                                .Include(f => f.Tasks)
                                .FirstOrDefaultAsync(f => f.Id == featId, ct);

                            if (feat != null)
                            {
                                var storyTitles = feat.UserStories.Select(us => us.Title).ToList();
                                var sb = new StringBuilder();
                                sb.AppendLine($"### @Feature:{feat.Name} (ID: {feat.Id})");
                                sb.AppendLine($"- Description: {feat.Description ?? "No description"}");
                                sb.AppendLine($"- Epic: {(feat.Epic != null ? feat.Epic.Name : "None")}");
                                sb.AppendLine($"- Stories: {(storyTitles.Any() ? string.Join(", ", storyTitles) : "None")}");
                                sb.AppendLine($"- Tasks Count: {feat.Tasks.Count}");
                                return sb.ToString();
                            }
                        }
                        break;

                    case "userstory":
                    case "story":
                        if (int.TryParse(idStr, out int storyId))
                        {
                            var story = await _context.UserStories
                                .Include(us => us.Feature)
                                .Include(us => us.Tasks)
                                .FirstOrDefaultAsync(us => us.Id == storyId, ct);

                            if (story != null)
                            {
                                var taskTitles = story.Tasks.Select(t => t.Title).ToList();
                                var sb = new StringBuilder();
                                sb.AppendLine($"### @UserStory:{story.Title} (ID: {story.Id})");
                                sb.AppendLine($"- Description: {story.Description ?? "No description"}");
                                sb.AppendLine($"- Acceptance Criteria: {story.AcceptanceCriteria ?? "None"}");
                                sb.AppendLine($"- Priority: {story.Priority}");
                                sb.AppendLine($"- Feature: {(story.Feature != null ? story.Feature.Name : "None")}");
                                sb.AppendLine($"- Tasks: {(taskTitles.Any() ? string.Join(", ", taskTitles) : "None")}");
                                return sb.ToString();
                            }
                        }
                        break;

                    case "task":
                        if (int.TryParse(idStr, out int taskId))
                        {
                            var task = await _context.Tasks
                                .Include(t => t.Project)
                                .Include(t => t.Assignee)
                                .FirstOrDefaultAsync(t => t.Id == taskId, ct);

                            if (task != null)
                            {
                                var sb = new StringBuilder();
                                sb.AppendLine($"### @Task:{task.Title} (ID: {task.Id})");
                                sb.AppendLine($"- Description: {task.Description ?? "No description"}");
                                sb.AppendLine($"- Status: {task.Status}");
                                sb.AppendLine($"- Assignee: {(task.Assignee != null ? task.Assignee.FullName ?? task.Assignee.UserName : "Unassigned")}");
                                sb.AppendLine($"- Project: {(task.Project != null ? task.Project.Name : "None")}");
                                sb.AppendLine($"- PERT Estimates: O={task.EstimatedOptimisticHours ?? 0}h, M={task.EstimatedMostLikelyHours ?? 0}h, P={task.EstimatedPessimisticHours ?? 0}h -> Calculated={task.PertEstimatedHours ?? 0}h");
                                return sb.ToString();
                            }
                        }
                        break;

                    case "sprint":
                        if (int.TryParse(idStr, out int sprintId))
                        {
                            var sprint = await _context.Sprints
                                .Include(s => s.Project)
                                .Include(s => s.Tasks)
                                .FirstOrDefaultAsync(s => s.Id == sprintId, ct);

                            if (sprint != null)
                            {
                                var actualLogged = sprint.Tasks.Sum(t => t.ActualHours ?? 0);
                                var estimatedTotal = sprint.Tasks.Sum(t => t.PertEstimatedHours ?? t.EstimatedHours);
                                var sb = new StringBuilder();
                                sb.AppendLine($"### @Sprint:{sprint.Name} (ID: {sprint.Id})");
                                sb.AppendLine($"- Dates: {sprint.StartDate:yyyy-MM-dd} to {sprint.EndDate:yyyy-MM-dd} (Active: {sprint.IsActive})");
                                sb.AppendLine($"- Planned Capacity: {(sprint.PlannedCapacityHours.HasValue ? sprint.PlannedCapacityHours.Value + "h" : "Unset")}");
                                sb.AppendLine($"- Tasks Count: {sprint.Tasks.Count}");
                                sb.AppendLine($"- Burn Status: {actualLogged:F1}h logged / {estimatedTotal:F1}h estimated");
                                return sb.ToString();
                            }
                        }
                        break;

                    case "user":
                        var u = await _context.Users
                            .Include(usr => usr.ResourceProfile)
                            .ThenInclude(rp => rp!.ProjectAllocations)
                            .ThenInclude(pa => pa.Project)
                            .FirstOrDefaultAsync(usr => usr.Id == idStr, ct);

                        if (u != null)
                        {
                            var openTasks = await _context.Tasks.CountAsync(t => t.AssigneeId == u.Id && t.Status != Models.Enums.TaskStatus.Done, ct);
                            
                            int totalAllocation = 0;
                            var allocProjects = new List<string>();
                            if (u.ResourceProfile?.ProjectAllocations != null)
                            {
                                totalAllocation = u.ResourceProfile.ProjectAllocations.Sum(pa => pa.AllocationPercentage);
                                allocProjects = u.ResourceProfile.ProjectAllocations
                                    .Select(pa => $"{pa.Project?.Name ?? "Project"} ({pa.AllocationPercentage}%)")
                                    .ToList();
                            }

                            var sb = new StringBuilder();
                            sb.AppendLine($"### @User:{u.FullName ?? u.UserName} (ID: {u.Id})");
                            sb.AppendLine($"- Email: {u.Email}");
                            sb.AppendLine($"- Department: {u.Department ?? u.ResourceProfile?.Department ?? "Unspecified"}");
                            sb.AppendLine($"- Job Title: {u.JobTitle ?? "Team Member"}");
                            sb.AppendLine($"- Allocation: {totalAllocation}% allocated{(allocProjects.Any() ? " [" + string.Join(", ", allocProjects) + "]" : "")}");
                            sb.AppendLine($"- Open Tasks: {openTasks} assigned open tasks");
                            return sb.ToString();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                // Return fallback error indicator block in context
                return $"### @{mention.Type}:{mention.Id}\nError resolving details: {ex.Message}";
            }

            return null;
        }
    }
}
