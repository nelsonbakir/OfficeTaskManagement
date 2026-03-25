using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using OfficeTaskManagement.Controllers;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.ViewModels.ResourceManagement;
using Xunit;

namespace OfficeTaskManagement.Tests.Controllers
{
    public class ResourceControllerTests : IDisposable
    {
        private readonly ApplicationDbContext _context;
        private readonly Mock<IResourceService> _mockResourceService;
        private readonly Mock<UserManager<User>> _mockUserManager;
        private readonly ResourceController _controller;

        public ResourceControllerTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);
            _mockResourceService = new Mock<IResourceService>();

            var store = new Mock<IUserStore<User>>();
            _mockUserManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

            _controller = new ResourceController(_context, _mockResourceService.Object, _mockUserManager.Object);

            var httpContext = new DefaultHttpContext();
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "admin_user"),
                new Claim(ClaimTypes.Role, "Manager")
            }, "TestAuthentication");
            httpContext.User = new ClaimsPrincipal(identity);

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            var tempDataProvider = new Mock<ITempDataProvider>();
            _controller.TempData = new TempDataDictionary(httpContext, tempDataProvider.Object);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        [Fact]
        public async Task Allocate_SetsWarning_WhenUserIsOverAllocated()
        {
            // Arrange
            var projectId = 1;
            var userId = "user1";
            var startDate = DateTime.UtcNow;
            
            _context.Projects.Add(new Project { Id = projectId, Name = "Test Project" });
            
            var resourceProfile = new ResourceProfile { Id = 1, UserId = userId, DailyCapacityHours = 8 };
            _mockResourceService.Setup(s => s.GetOrCreateProfileAsync(userId)).ReturnsAsync(resourceProfile);
            
            _mockResourceService.Setup(s => s.IsUserOverAllocatedAsync(userId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(true); // Simulate over-allocation
                
            _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(new User { Id = "admin_user" });

            await _context.SaveChangesAsync();

            var model = new EditProjectAllocationViewModel
            {
                ProjectId = projectId,
                UserId = userId,
                AllocationPercentage = 80,
                StartDate = startDate
            };

            // Act
            var result = await _controller.Allocate(model);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Details", redirectResult.ActionName);
            Assert.Equal("Projects", redirectResult.ControllerName);
            
            // Verify allocation was saved
            var savedAllocation = await _context.ProjectResourceAllocations.FirstOrDefaultAsync();
            Assert.NotNull(savedAllocation);
            Assert.Equal(80, savedAllocation.AllocationPercentage);
            
            // Verify TempData contains the warning
            Assert.True(_controller.TempData.ContainsKey("ResourceWarning"));
            Assert.Contains("over 100% capacity", _controller.TempData["ResourceWarning"]!.ToString());
        }
        
        [Fact]
        public async Task Allocate_DoesNotSetWarning_WhenUserIsNotOverAllocated()
        {
            // Arrange
            var projectId = 1;
            var userId = "user1";
            
            _context.Projects.Add(new Project { Id = projectId, Name = "Test Project" });
            
            var resourceProfile = new ResourceProfile { Id = 1, UserId = userId, DailyCapacityHours = 8 };
            _mockResourceService.Setup(s => s.GetOrCreateProfileAsync(userId)).ReturnsAsync(resourceProfile);
            
            _mockResourceService.Setup(s => s.IsUserOverAllocatedAsync(userId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(false); // No over-allocation
                
            _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(new User { Id = "admin_user" });

            await _context.SaveChangesAsync();

            var model = new EditProjectAllocationViewModel
            {
                ProjectId = projectId,
                UserId = userId,
                AllocationPercentage = 50,
                StartDate = DateTime.UtcNow
            };

            // Act
            var result = await _controller.Allocate(model);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            
            // Verify TempData does NOT contain the warning
            Assert.False(_controller.TempData.ContainsKey("ResourceWarning"));
        }
    }
}
