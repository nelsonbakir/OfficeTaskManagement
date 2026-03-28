using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services;
using Xunit;

namespace OfficeTaskManagement.Tests.Services
{
    public class ResourceServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly ResourceService _resourceService;

        public ResourceServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);
            _resourceService = new ResourceService(_context);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        [Fact]
        public async Task GetUserAvailableHoursAsync_ReturnsCorrectBaseCapacity_WithNoBlocks()
        {
            // Arrange
            var userId = "user1";
            var startDate = new DateTime(2023, 10, 1); // Sunday
            var endDate = new DateTime(2023, 10, 31);   // Tuesday
            
            _context.ResourceProfiles.Add(new ResourceProfile
            {
                UserId = userId,
                DailyCapacityHours = 8
            });
            await _context.SaveChangesAsync();

            // Act — working days exclude Fri/Sat (Bangladesh weekend); Oct 2023 has 23 such days × 8h
            var capacity = await _resourceService.GetUserAvailableHoursAsync(userId, startDate, endDate);

            // Assert
            Assert.Equal(184m, capacity);
        }

        [Fact]
        public async Task GetUserAvailableHoursAsync_SubtractsLeaveBlocks()
        {
            // Arrange
            var userId = "user2";
            var startDate = new DateTime(2023, 10, 1);
            var endDate = new DateTime(2023, 10, 31);
            
            _context.ResourceProfiles.Add(new ResourceProfile
            {
                UserId = userId,
                DailyCapacityHours = 8
            });

            // Add 2 days of leave (16 hours)
            _context.ResourceAvailabilityBlocks.Add(new ResourceAvailabilityBlock
            {
                UserId = userId,
                StartDate = new DateTime(2023, 10, 2), // Monday
                EndDate = new DateTime(2023, 10, 3),   // Tuesday
                Reason = AvailabilityBlockReason.Leave
            });
            
            await _context.SaveChangesAsync();

            // Act
            var capacity = await _resourceService.GetUserAvailableHoursAsync(userId, startDate, endDate);

            // Assert — 184 - 16 (two working days leave)
            Assert.Equal(168m, capacity);
        }

        [Fact]
        public async Task IsUserOverAllocatedAsync_ReturnsTrue_WhenAllocationExceeds100()
        {
            // Arrange
            var userId = "user3";
            var startDate = new DateTime(2023, 10, 1);
            var endDate = new DateTime(2023, 10, 31);
            
            _context.ResourceProfiles.Add(new ResourceProfile { UserId = userId, DailyCapacityHours = 8 });
            
            _context.ProjectResourceAllocations.Add(new ProjectResourceAllocation
            {
                UserId = userId,
                ProjectId = 1,
                AllocationPercentage = 60,
                StartDate = startDate,
                EndDate = endDate
            });
            
            _context.ProjectResourceAllocations.Add(new ProjectResourceAllocation
            {
                UserId = userId,
                ProjectId = 2,
                AllocationPercentage = 50,
                StartDate = startDate,
                EndDate = endDate
            });
            
            await _context.SaveChangesAsync();

            // Act
            var result = await _resourceService.IsUserOverAllocatedAsync(userId, new DateTime(2023, 10, 15), new DateTime(2023, 10, 20));

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task IsUserOverAllocatedAsync_ReturnsFalse_WhenAllocationIsUnder100()
        {
            // Arrange
            var userId = "user4";
            var startDate = new DateTime(2023, 10, 1);
            var endDate = new DateTime(2023, 10, 31);
            
            _context.ResourceProfiles.Add(new ResourceProfile { UserId = userId, DailyCapacityHours = 8 });
            
            _context.ProjectResourceAllocations.Add(new ProjectResourceAllocation
            {
                UserId = userId,
                ProjectId = 1,
                AllocationPercentage = 80,
                StartDate = startDate,
                EndDate = endDate
            });
            
            await _context.SaveChangesAsync();

            // Act
            var result = await _resourceService.IsUserOverAllocatedAsync(userId, new DateTime(2023, 10, 15), new DateTime(2023, 10, 20));

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task GetPeakAllocationPercentInRangeAsync_ReturnsMaxDailyCombinedAllocation()
        {
            var userId = "peak-user";
            var start = new DateTime(2023, 10, 2);
            var end = new DateTime(2023, 10, 3);

            _context.ResourceProfiles.Add(new ResourceProfile { UserId = userId, DailyCapacityHours = 8 });
            _context.Projects.Add(new Project { Id = 1, Name = "P1" });
            _context.Projects.Add(new Project { Id = 2, Name = "P2" });
            _context.ProjectResourceAllocations.Add(new ProjectResourceAllocation
            {
                UserId = userId,
                ProjectId = 1,
                AllocationPercentage = 60,
                StartDate = start,
                EndDate = end
            });
            _context.ProjectResourceAllocations.Add(new ProjectResourceAllocation
            {
                UserId = userId,
                ProjectId = 2,
                AllocationPercentage = 50,
                StartDate = start,
                EndDate = end
            });
            await _context.SaveChangesAsync();

            var peak = await _resourceService.GetPeakAllocationPercentInRangeAsync(userId, start, end);

            Assert.Equal(110, peak);
        }
    }
}
