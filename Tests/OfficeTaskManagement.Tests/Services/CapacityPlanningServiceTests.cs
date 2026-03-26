using System;
using System.Collections.Generic;
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
    public class CapacityPlanningServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly Mock<IResourceService> _mockResourceService;
        private readonly CapacityPlanningService _capacityPlanningService;

        public CapacityPlanningServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);
            _mockResourceService = new Mock<IResourceService>();
            _capacityPlanningService = new CapacityPlanningService(_context, _mockResourceService.Object);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        [Fact]
        public async Task GetSprintDemandHoursAsync_ReturnsSumOfTaskEstimates()
        {
            // Arrange
            var sprintId = 1;
            _context.Tasks.Add(new TaskItem { Title = "Task 1", SprintId = sprintId, EstimatedHours = 5, Status = OfficeTaskManagement.Models.Enums.TaskStatus.ToDo });
            _context.Tasks.Add(new TaskItem { Title = "Task 2", SprintId = sprintId, EstimatedHours = 3, Status = OfficeTaskManagement.Models.Enums.TaskStatus.InProgress });
            _context.Tasks.Add(new TaskItem { Title = "Task 3", SprintId = 2, EstimatedHours = 8, Status = OfficeTaskManagement.Models.Enums.TaskStatus.ToDo }); // Different sprint
            await _context.SaveChangesAsync();

            // Act
            var demand = await _capacityPlanningService.GetSprintDemandHoursAsync(sprintId);

            // Assert
            Assert.Equal(8m, demand);
        }

        [Fact]
        public async Task GetSprintCapacityHoursAsync_ReturnsPlannedCapacity_WhenSet()
        {
            // Arrange
            var sprintId = 1;
            _context.Sprints.Add(new Sprint 
            { 
                Id = sprintId, 
                Name = "Sprint 1", 
                StartDate = DateTime.UtcNow, 
                EndDate = DateTime.UtcNow.AddDays(14),
                PlannedCapacityHours = 120 
            });
            await _context.SaveChangesAsync();

            // Act
            var capacity = await _capacityPlanningService.GetSprintCapacityHoursAsync(sprintId);

            // Assert
            Assert.Equal(120m, capacity);
            _mockResourceService.Verify(s => s.GetUserAvailableHoursAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task GetSprintCapacityHoursAsync_CalculatesDynamicCapacity_WhenPlannedIsEmpty()
        {
            // Arrange
            var sprintId = 1;
            var startDate = DateTime.UtcNow;
            var endDate = startDate.AddDays(14);
            
            _context.Sprints.Add(new Sprint 
            { 
                Id = sprintId, 
                Name = "Sprint 1", 
                StartDate = startDate, 
                EndDate = endDate
            });

            _context.Tasks.Add(new TaskItem { Title = "Task 1", SprintId = sprintId, AssigneeId = "user1" });
            _context.Tasks.Add(new TaskItem { Title = "Task 2", SprintId = sprintId, AssigneeId = "user2" });
            _context.Tasks.Add(new TaskItem { Title = "Task 3", SprintId = sprintId, AssigneeId = "user1" }); // Duplicate assignee
            await _context.SaveChangesAsync();

            _mockResourceService.Setup(s => s.GetUserAvailableHoursAsync("user1", startDate, endDate))
                .ReturnsAsync(80m);
            _mockResourceService.Setup(s => s.GetUserAvailableHoursAsync("user2", startDate, endDate))
                .ReturnsAsync(70m);

            // Act
            var capacity = await _capacityPlanningService.GetSprintCapacityHoursAsync(sprintId);

            // Assert
            Assert.Equal(150m, capacity); // 80 + 70
            _mockResourceService.Verify(s => s.GetUserAvailableHoursAsync("user1", startDate, endDate), Times.Once);
            _mockResourceService.Verify(s => s.GetUserAvailableHoursAsync("user2", startDate, endDate), Times.Once);
        }
    }
}
