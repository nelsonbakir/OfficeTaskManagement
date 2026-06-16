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
                UpdateEstimateTool()
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
                priority         = new { type = "string",  @enum = new[] { "Low", "Medium", "High", "Critical" } }
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
}
