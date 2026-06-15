using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    /// <summary>
    /// Unit tests for BudgetService covering:
    ///  - Budget baseline set/update
    ///  - Advisory level thresholds (None, Info, Warning, Critical)
    ///  - DerivedBudgetForecast computation
    ///  - Other-cost CRUD
    /// </summary>
    public class BudgetServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly Mock<IResourceService> _resourceSvcMock;
        private readonly BudgetService _budgetService;

        private const int ProjectId = 1;

        public BudgetServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            _context          = new ApplicationDbContext(options);
            _resourceSvcMock  = new Mock<IResourceService>();

            // Default: effective hourly rate = BDT 100
            _resourceSvcMock
                .Setup(r => r.GetEffectiveHourlyRateAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
                .ReturnsAsync(100m);

            _budgetService = new BudgetService(_context, _resourceSvcMock.Object);

            // Seed a test project
            _context.Projects.Add(new Project { Id = ProjectId, Name = "Test Project" });
            _context.SaveChanges();
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        // ── Set Budget ────────────────────────────────────────────────────────

        [Fact]
        public async Task SetBudget_PreApproved_SetsModeAndAmount()
        {
            // Act
            await _budgetService.SetProjectBudgetAsync(
                ProjectId, BudgetMode.PreApproved, 500_000m, 50_000m, setById: null);

            // Assert
            var project = await _context.Projects.FindAsync(ProjectId);
            Assert.Equal(BudgetMode.PreApproved, project!.BudgetMode);
            Assert.Equal(500_000m, project.ApprovedBudget);
            Assert.Equal(50_000m, project.ContingencyReserve);
            Assert.NotNull(project.BudgetSetAt);
        }

        [Fact]
        public async Task SetBudget_DerivedFromWork_ClearsApprovedBudget()
        {
            // Arrange — first set a pre-approved budget
            await _budgetService.SetProjectBudgetAsync(
                ProjectId, BudgetMode.PreApproved, 500_000m, null, null);

            // Act — switch to DerivedFromWork
            await _budgetService.SetProjectBudgetAsync(
                ProjectId, BudgetMode.DerivedFromWork, 999_999m, null, null);

            // Assert — ApprovedBudget must be nulled out
            var project = await _context.Projects.FindAsync(ProjectId);
            Assert.Equal(BudgetMode.DerivedFromWork, project!.BudgetMode);
            Assert.Null(project.ApprovedBudget);
        }

        // ── Advisory Level Thresholds ─────────────────────────────────────────

        [Fact]
        public async Task GetBudgetAdvisory_NoBudgetSet_ReturnsNoneLevel()
        {
            // No budget set (BudgetMode.NotSet by default)
            var advisory = await _budgetService.GetBudgetAdvisoryAsync(ProjectId, 10_000m);

            Assert.Equal(BudgetAdvisoryLevel.None, advisory.Level);
            Assert.False(advisory.HasBudget);
        }

        [Fact]
        public async Task GetBudgetAdvisory_Below80Percent_ReturnsInfoLevel()
        {
            // Arrange — approved BDT 500,000; current estimate = 0; proposed = 200,000 (40%)
            await _budgetService.SetProjectBudgetAsync(
                ProjectId, BudgetMode.PreApproved, 500_000m, null, null);

            var advisory = await _budgetService.GetBudgetAdvisoryAsync(ProjectId, 200_000m);

            Assert.Equal(BudgetAdvisoryLevel.Info, advisory.Level);
            Assert.True(advisory.HasBudget);
        }

        [Fact]
        public async Task GetBudgetAdvisory_At85Percent_ReturnsWarningLevel()
        {
            // Arrange — approved BDT 500,000; proposed pushes to 85%
            await _budgetService.SetProjectBudgetAsync(
                ProjectId, BudgetMode.PreApproved, 500_000m, null, null);

            // 85% of 500,000 = 425,000
            var advisory = await _budgetService.GetBudgetAdvisoryAsync(ProjectId, 425_000m);

            Assert.Equal(BudgetAdvisoryLevel.Warning, advisory.Level);
        }

        [Fact]
        public async Task GetBudgetAdvisory_Over100Percent_ReturnsCriticalLevel()
        {
            // Arrange — approved BDT 500,000; proposed pushes over 100%
            await _budgetService.SetProjectBudgetAsync(
                ProjectId, BudgetMode.PreApproved, 500_000m, null, null);

            // 550,000 / 500,000 = 110%
            var advisory = await _budgetService.GetBudgetAdvisoryAsync(ProjectId, 550_000m);

            Assert.Equal(BudgetAdvisoryLevel.Critical, advisory.Level);
            Assert.True(advisory.ProjectedTotalCost > advisory.ApprovedBudget);
        }

        // ── Derived Budget Forecast ───────────────────────────────────────────

        [Fact]
        public async Task DeriveBottomUpBudget_SumsOtherCostsOnly_WhenNoTasks()
        {
            // Arrange — two other-cost line items: 30,000 + 20,000 = 50,000
            _context.ProjectOtherCosts.Add(new ProjectOtherCost
            {
                ProjectId       = ProjectId,
                Description     = "Server license",
                EstimatedAmount = 30_000m,
                Category        = OtherCostCategory.License
            });
            _context.ProjectOtherCosts.Add(new ProjectOtherCost
            {
                ProjectId       = ProjectId,
                Description     = "Team travel",
                EstimatedAmount = 20_000m,
                Category        = OtherCostCategory.Travel
            });
            await _context.SaveChangesAsync();

            // Act
            var forecast = await _budgetService.GetDerivedBudgetForecastAsync(ProjectId);

            // Assert
            Assert.Equal(0m, forecast.LaborEstimate);
            Assert.Equal(50_000m, forecast.OtherCostEstimate);
            Assert.Equal(50_000m, forecast.TotalForecast);
        }

        // ── Other Cost CRUD ───────────────────────────────────────────────────

        [Fact]
        public async Task AddOtherCost_CreatesLineItem()
        {
            var dto = new OtherCostUpsertDto
            {
                ProjectId       = ProjectId,
                Description     = "AWS License",
                Category        = OtherCostCategory.Software,
                Frequency       = CostFrequency.Monthly,
                EstimatedAmount = 5_000m
            };

            var cost = await _budgetService.AddOtherCostAsync(dto, createdById: null);

            Assert.NotEqual(0, cost.Id);
            Assert.Equal("AWS License", cost.Description);
            Assert.Equal(OtherCostCategory.Software, cost.Category);

            var saved = await _context.ProjectOtherCosts.FindAsync(cost.Id);
            Assert.NotNull(saved);
        }

        [Fact]
        public async Task DeleteOtherCost_RemovesLineItem()
        {
            // Arrange
            var cost = new ProjectOtherCost
            {
                ProjectId       = ProjectId,
                Description     = "Temp item",
                EstimatedAmount = 1_000m
            };
            _context.ProjectOtherCosts.Add(cost);
            await _context.SaveChangesAsync();
            var id = cost.Id;

            // Act
            await _budgetService.DeleteOtherCostAsync(id);

            // Assert
            var deleted = await _context.ProjectOtherCosts.FindAsync(id);
            Assert.Null(deleted);
        }
    }
}
