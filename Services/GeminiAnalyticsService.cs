using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;

namespace OfficeTaskManagement.Services
{
    public class GeminiAnalyticsService : IGeminiAnalyticsService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public GeminiAnalyticsService(ApplicationDbContext context, IConfiguration configuration, HttpClient httpClient)
        {
            _context = context;
            _configuration = configuration;
            _httpClient = httpClient;
        }

        private async Task<string> CallGeminiApiAsync(string prompt)
        {
            var apiKey = _configuration["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_GEMINI_API_KEY_HERE")
            {
                return "Error: Gemini API Key is missing or invalid. Please configure it in appsettings.json.";
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:generateContent?key={apiKey}";
            
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = prompt } }
                    }
                }
            };
            
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, jsonContent);
            
            if (!response.IsSuccessStatusCode)
            {
                return $"Error: Gemini API request failed with status {response.StatusCode}. {await response.Content.ReadAsStringAsync()}";
            }
            
            var jsonString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonString);
            
            try 
            {
                var text = doc.RootElement
                            .GetProperty("candidates")[0]
                            .GetProperty("content")
                            .GetProperty("parts")[0]
                            .GetProperty("text")
                            .GetString();
                return text ?? "No response generated.";
            } 
            catch 
            {
                return "Error: Failed to parse Gemini response payload.";
            }
        }

        public async Task<string> PredictProjectDelayAsync(int projectId)
        {
            var project = await _context.Projects
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null) return "Project not found.";

            var tasksData = await _context.Tasks
                .Where(t => t.ProjectId == projectId)
                .Select(t => new { t.Title, t.Status, t.EstimatedHours, t.DueDate })
                .ToListAsync();
            
            var json = JsonSerializer.Serialize(tasksData);

            string prompt = $"You are an expert technical project manager. Analyze this JSON representing the tasks of the project '{project.Name}'. Estimate if this project is on track, at risk, or delayed based on task statuses and due dates. Be concise and format your response in markdown. JSON data: {json}";

            return await CallGeminiApiAsync(prompt);
        }

        public async Task<string> DetectBurnoutAsync()
        {
            var usersData = await _context.Users
                .Select(u => new
                {
                    FullName = u.FullName,
                    AssignedTasksCount = _context.Tasks.Count(t => t.AssigneeId == u.Id && t.Status != OfficeTaskManagement.Models.Enums.TaskStatus.Done),
                    TotalEstimatedHours = _context.Tasks.Where(t => t.AssigneeId == u.Id && t.Status != OfficeTaskManagement.Models.Enums.TaskStatus.Done).Sum(t => t.EstimatedHours),
                    CommentsMade = _context.TaskComments.Count(c => c.UserId == u.Id && c.CreatedAt >= DateTime.UtcNow.AddDays(-14))
                })
                .ToListAsync();

            var json = JsonSerializer.Serialize(usersData);
            
            string prompt = $"You are a technical team lead and agile coach. Review the workload data. The workflow is: New, ToDo, InProgress, Committed (Delivered), Tested (QA), Done. Identify any team members who might be at risk of burnout. Point out specific names and why. Be concise and format your response in markdown. JSON data: {json}";

            return await CallGeminiApiAsync(prompt);
        }

        public async Task<string> GenerateSprintRetrospectiveAsync()
        {
            var recentDoneTasks = await _context.Tasks
                .Include(t => t.Assignee)
                .Include(t => t.Comments)
                .Where(t => t.Status == OfficeTaskManagement.Models.Enums.TaskStatus.Done && t.DueDate >= DateTime.UtcNow.AddDays(-14))
                .Select(t => new
                {
                    TaskTitle = t.Title,
                    Assignee = t.Assignee != null ? t.Assignee.FullName : "Unassigned",
                    ActualCompletionDate = t.DueDate,
                    Priority = t.Priority,
                    CommentsCount = t.Comments.Count
                })
                .ToListAsync();

            var json = JsonSerializer.Serialize(recentDoneTasks);

            string prompt = $"You are a Scrum Master. Write a Sprint Retrospective based on these tasks marked as 'Done' (PO Confirmed). Summarize accomplishments. Format your response in markdown. JSON data: {json}";

            return await CallGeminiApiAsync(prompt);
        }

        public async Task<string> AnalyzeTechnicalDebtAsync()
        {
            var tasks = await _context.Tasks
                .Include(t => t.Comments)
                .Where(t => t.Status != OfficeTaskManagement.Models.Enums.TaskStatus.Done && t.Status != OfficeTaskManagement.Models.Enums.TaskStatus.New)
                .Select(t => new
                {
                    TaskTitle = t.Title,
                    Status = t.Status.ToString(),
                    CommentsCount = t.Comments.Count,
                    IsBacklog = t.IsBacklog
                })
                .ToListAsync();

            var json = JsonSerializer.Serialize(tasks);

            string prompt = $"You are a Senior Architect. Review these active tasks. The workflow is: New, ToDo, InProgress, Committed (Delivered for testing), Tested (QA), Done. Flag any tasks that represent potential Technical Debt or poorly defined requirements. For example, tasks with high comment counts stuck in 'InProgress' or 'Committed' indicate friction. Format your response in markdown. JSON data: {json}";

            return await CallGeminiApiAsync(prompt);
        }
    }
}
