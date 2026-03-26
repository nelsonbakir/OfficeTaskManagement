using System.Collections.Generic;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Services
{
    public interface IGeminiAnalyticsService
    {
        Task<string> PredictProjectDelayAsync(int projectId);
        Task<string> DetectBurnoutAsync();
        Task<string> GenerateSprintRetrospectiveAsync();
        Task<string> AnalyzeTechnicalDebtAsync();
    }
}
