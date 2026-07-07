using System.Text.Json;

namespace OfficeTaskManagement.Services.Agent;

/// <summary>
/// Defines all Gemini Function Calling tool schemas for the AI Copilot.
/// Each tool maps to an EF Core operation handled by AgentToolDispatcher.
/// Spec: ai-agent-plan/05_SERVICE_LAYER.md → AgentToolDefinitions
/// </summary>
public static class AgentToolDefinitions
{
    /// <summary>
    /// Returns the JSON-serializable tool list to pass to the Gemini `tools` parameter.
    /// </summary>
    public static object[] GetTools() => new object[]
    {
        new
        {
            function_declarations = new object[]
            {
                CreateEpicTool(),
                CreateFeatureTool(),
                CreateUserStoryTool(),
                CreateTaskTool(),
                QueryResourceAvailabilityTool(),
                GetSprintCapacityTool(),
                UpdateEstimateTool(),
                // KF-2: Read tools
                ReadProjectTasksTool(),
                ReadSprintListTool(),
                ReadProjectStatusTool(),
                // KF-2: Write tools
                CreateProjectTool(),
                AssignTaskTool(),
                DraftEpicsTool(),
                DraftFeaturesTool(),
                DraftStoriesAndTasksTool(),
                GetWorkPackageSummaryTool(),
                // KF-5: PM Status Report
                GenerateStatusReportTool()
            }
        }
    };

    private static object CreateEpicTool() => new
    {
        name = "create_epic",
        description = "Creates a new Epic under a Project. Use when the user asks to add a major feature area or initiative to a project.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                projectId   = new { type = "integer", description = "The ID of the parent project" },
                name        = new { type = "string",  description = "The Epic title" },
                description = new { type = "string",  description = "Brief description of the Epic" },
                priority    = new { type = "string",  description = "Priority level: Low | Medium | High | Critical", @enum = new[] { "Low", "Medium", "High", "Critical" } }
            },
            required = new[] { "projectId", "name" }
        }
    };

    private static object CreateFeatureTool() => new
    {
        name = "create_feature",
        description = "Creates a new Feature under an Epic. Use when the user asks to break down an Epic into deliverable features.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                epicId      = new { type = "integer", description = "The ID of the parent Epic" },
                name        = new { type = "string",  description = "The Feature title" },
                description = new { type = "string",  description = "Brief description of the Feature" },
                priority    = new { type = "string",  description = "Priority level: Low | Medium | High | Critical", @enum = new[] { "Low", "Medium", "High", "Critical" } }
            },
            required = new[] { "epicId", "name" }
        }
    };

    private static object CreateUserStoryTool() => new
    {
        name = "create_user_story",
        description = "Creates a new User Story under a Feature. Include acceptance criteria when provided.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                featureId          = new { type = "integer", description = "The ID of the parent Feature" },
                title              = new { type = "string",  description = "The User Story title, e.g. 'As a user I can...'" },
                description        = new { type = "string",  description = "Detailed description" },
                acceptanceCriteria = new { type = "string",  description = "Markdown-formatted acceptance criteria" },
                priority           = new { type = "string",  @enum = new[] { "Low", "Medium", "High", "Critical" } }
            },
            required = new[] { "featureId", "title" }
        }
    };

    private static object CreateTaskTool() => new
    {
        name = "create_task",
        description = "Creates a development Task under a User Story with PERT three-point estimates.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                userStoryId      = new { type = "integer", description = "The ID of the parent User Story" },
                title            = new { type = "string",  description = "Task title" },
                description      = new { type = "string",  description = "Task description" },
                optimisticHours  = new { type = "number",  description = "Best-case hours (O)" },
                mostLikelyHours  = new { type = "number",  description = "Most likely hours (M)" },
                pessimisticHours = new { type = "number",  description = "Worst-case hours (P)" },
                priority         = new { type = "string",  @enum = new[] { "Low", "Medium", "High", "Critical" } },
                assigneeId       = new { type = "string",  description = "Optional: user ID to assign the task to" },
                sprintId         = new { type = "integer", description = "Optional: sprint ID to place the task in" }
            },
            required = new[] { "userStoryId", "title" }
        }
    };

    private static object QueryResourceAvailabilityTool() => new
    {
        name = "query_resource_availability",
        description = "Returns available capacity hours for team members in a project for a given date range.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "integer", description = "Project ID to check resources for" },
                startDate = new { type = "string",  description = "ISO 8601 start date (YYYY-MM-DD)" },
                endDate   = new { type = "string",  description = "ISO 8601 end date (YYYY-MM-DD)" }
            },
            required = new[] { "projectId", "startDate", "endDate" }
        }
    };

    private static object GetSprintCapacityTool() => new
    {
        name = "get_sprint_capacity",
        description = "Returns the available and used capacity for a sprint, and lists tasks that can fit.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                sprintId = new { type = "integer", description = "The sprint ID to check capacity for" }
            },
            required = new[] { "sprintId" }
        }
    };

    private static object UpdateEstimateTool() => new
    {
        name = "update_estimate",
        description = "Updates the PERT estimate (optimistic, most likely, pessimistic hours) for an existing task.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                taskId           = new { type = "integer", description = "The task ID to update" },
                optimisticHours  = new { type = "number" },
                mostLikelyHours  = new { type = "number" },
                pessimisticHours = new { type = "number" }
            },
            required = new[] { "taskId", "optimisticHours", "mostLikelyHours", "pessimisticHours" }
        }
    };

    // ── KF-2 Read Tools ───────────────────────────────────────────────────────

    private static object ReadProjectTasksTool() => new
    {
        name = "read_project_tasks",
        description = "Returns a list of tasks for a project, optionally filtered by sprint, status, or assignee. Use this to answer questions about current work.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                projectId  = new { type = "integer", description = "The project ID to read tasks from" },
                sprintId   = new { type = "integer", description = "Optional: filter by sprint ID" },
                status     = new { type = "string",  description = "Optional: filter by status", @enum = new[] { "New", "ToDo", "InProgress", "Done", "Blocked" } },
                assigneeId = new { type = "string",  description = "Optional: filter by assignee user ID" },
                limit      = new { type = "integer", description = "Maximum number of tasks to return (default 20)" }
            },
            required = new[] { "projectId" }
        }
    };

    private static object ReadSprintListTool() => new
    {
        name = "read_sprint_list",
        description = "Returns all sprints for a project with their dates and task counts.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "integer", description = "The project ID to list sprints for" }
            },
            required = new[] { "projectId" }
        }
    };

    private static object ReadProjectStatusTool() => new
    {
        name = "read_project_status",
        description = "Returns an overall health snapshot of a project: epic count, feature count, story count, task counts by status, sprint velocity, and budget summary.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "integer", description = "The project ID to get the status snapshot for" }
            },
            required = new[] { "projectId" }
        }
    };

    // ── KF-2 Write Tools ──────────────────────────────────────────────────────

    private static object CreateProjectTool() => new
    {
        name = "create_project",
        description = "Creates a new Project. Use when user asks to start a new project.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                name        = new { type = "string", description = "Project name (required)" },
                description = new { type = "string", description = "Optional project description" },
                startDate   = new { type = "string", description = "Optional ISO 8601 start date (YYYY-MM-DD)" },
                endDate     = new { type = "string", description = "Optional ISO 8601 end date (YYYY-MM-DD)" }
            },
            required = new[] { "name" }
        }
    };

    private static object AssignTaskTool() => new
    {
        name = "assign_task",
        description = "Assigns an existing task to a user and/or sprint.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                taskId         = new { type = "integer", description = "The task ID to assign" },
                assigneeUserId = new { type = "string",  description = "Optional: user ID to assign the task to" },
                sprintId       = new { type = "integer", description = "Optional: sprint ID to place the task in" }
            },
            required = new[] { "taskId" }
        }
    };

    private static object DraftEpicsTool() => new
    {
        name = "draft_epics",
        description = "Drafts a list of Epics based on project meeting notes or requirements. Returns a structured JSON payload to the UI for user review. Use this as Step 1 of the interactive WBS planning flow.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "integer", description = "The project ID" },
                epics = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string" },
                            description = new { type = "string" }
                        }
                    }
                }
            },
            required = new[] { "projectId", "epics" }
        }
    };

    private static object DraftFeaturesTool() => new
    {
        name = "draft_features",
        description = "Drafts a list of Features for given approved Epics. Returns a structured JSON payload to the UI for user review. Use this as Step 2 of the interactive WBS planning flow.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "integer" },
                epics = new
                {
                    type = "array",
                    description = "Array of epics with drafted features",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string" },
                            description = new { type = "string" },
                            features = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        name = new { type = "string" },
                                        description = new { type = "string" }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            required = new[] { "projectId", "epics" }
        }
    };

    private static object DraftStoriesAndTasksTool() => new
    {
        name = "draft_stories_and_tasks",
        description = "Drafts User Stories, Test Cases, and PERT-estimated Tasks for given approved Features. Returns a structured JSON payload to the UI. Use this as Step 3 of the interactive WBS planning flow.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "integer" },
                epics = new
                {
                    type = "array",
                    description = "Array of epics with features and drafted stories/tasks",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string" },
                            description = new { type = "string" },
                            features = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        name = new { type = "string" },
                                        description = new { type = "string" },
                                        stories = new
                                        {
                                            type = "array",
                                            items = new
                                            {
                                                type = "object",
                                                properties = new
                                                {
                                                    title = new { type = "string" },
                                                    description = new { type = "string" },
                                                    acceptanceCriteria = new { type = "string" },
                                                    testCases = new
                                                    {
                                                        type = "array",
                                                        items = new
                                                        {
                                                            type = "object",
                                                            properties = new
                                                            {
                                                                title = new { type = "string" },
                                                                steps = new { type = "string" },
                                                                expectedResult = new { type = "string" },
                                                                isAutomated = new { type = "boolean" }
                                                            }
                                                        }
                                                    },
                                                    tasks = new
                                                    {
                                                        type = "array",
                                                        items = new
                                                        {
                                                            type = "object",
                                                            properties = new
                                                            {
                                                                title = new { type = "string" },
                                                                description = new { type = "string" },
                                                                optimisticHours = new { type = "number" },
                                                                mostLikelyHours = new { type = "number" },
                                                                pessimisticHours = new { type = "number" }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            required = new[] { "projectId", "epics" }
        }
    };

    private static object GetWorkPackageSummaryTool() => new
    {
        name = "get_work_package_summary",
        description = "Returns the aggregated work package summary for a task: total PERT estimate, actual hours, stage breakdown, and time-in-status.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                taskId = new { type = "integer", description = "The parent task ID to summarise" }
            },
            required = new[] { "taskId" }
        }
    };

    // KF-5: PM Status Report
    private static object GenerateStatusReportTool() => new
    {
        name = "generate_status_report",
        description = "Generates a comprehensive PMP-grade PM status report for a project. " +
                      "Includes RAG status, sprint progress, risk register, resource utilization, and recommendations. " +
                      "Use when the user requests a status report or types /report.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                projectId = new { type = "integer", description = "The project ID to generate the report for" }
            },
            required = new[] { "projectId" }
        }
    };
}
