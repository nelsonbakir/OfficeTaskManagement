using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Models.Enums;
using OfficeTaskManagement.Services;
using OfficeTaskManagement.ViewModels.ResourceManagement;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    public class ResourceController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IResourceService _resourceService;
        private readonly UserManager<User> _userManager;

        public ResourceController(
            ApplicationDbContext context,
            IResourceService resourceService,
            UserManager<User> userManager)
        {
            _context = context;
            _resourceService = resourceService;
            _userManager = userManager;
        }

        // GET: Resource (Resource Pool)
        public async Task<IActionResult> Index()
        {
            var users = await _context.Users
                .Include(u => u.ResourceProfile)
                .ThenInclude(rp => rp!.Skills)
                .OrderBy(u => u.FullName ?? u.Email)
                .ToListAsync();

            var currentMonth = DateTime.UtcNow;
            var utilizations = await _resourceService.GetTeamUtilizationAsync(currentMonth.Year, currentMonth.Month);
            var utilDict = utilizations.ToDictionary(u => u.UserId);

            var viewModels = users.Select(u => new ResourceProfileViewModel
            {
                UserId = u.Id,
                FullName = u.FullName ?? string.Empty,
                Email = u.Email ?? string.Empty,
                Department = u.ResourceProfile?.Department,
                SeniorityLevel = u.ResourceProfile?.SeniorityLevel ?? Models.Enums.SeniorityLevel.Mid,
                DailyCapacityHours = u.ResourceProfile?.DailyCapacityHours ?? 8,
                HourlyRate = User.IsInRole("Manager") ? (u.ResourceProfile?.HourlyRate ?? 0) : 0,
                Skills = u.ResourceProfile?.Skills.Select(s => new ResourceSkillViewModel
                {
                    Id = s.Id,
                    SkillName = s.SkillName,
                    ProficiencyLevel = s.ProficiencyLevel
                }).ToList() ?? new List<ResourceSkillViewModel>(),
                UtilizationPercent = utilDict.TryGetValue(u.Id, out var util) ? util.UtilizationPercent : 0,
                IsOverAllocated = utilDict.TryGetValue(u.Id, out var oUtil) && oUtil.IsOverAllocated
            }).ToList();

            return View(viewModels);
        }

        // GET: Resource/Profile/5
        public async Task<IActionResult> Profile(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            var user = await _context.Users
                .Include(u => u.ResourceProfile)
                .ThenInclude(rp => rp!.Skills)
                .Include(u => u.AvailabilityBlocks)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return NotFound();

            var allocations = await _resourceService.GetUserAllocationSummaryAsync(id);
            var currentMonth = DateTime.UtcNow;
            var utilPercent = await _resourceService.GetUserUtilizationPercentAsync(id, currentMonth.Year, currentMonth.Month);

            var vm = new ResourceProfileViewModel
            {
                UserId = user.Id,
                FullName = user.FullName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                Department = user.ResourceProfile?.Department,
                SeniorityLevel = user.ResourceProfile?.SeniorityLevel ?? Models.Enums.SeniorityLevel.Mid,
                DailyCapacityHours = user.ResourceProfile?.DailyCapacityHours ?? 8,
                HourlyRate = User.IsInRole("Manager") ? (user.ResourceProfile?.HourlyRate ?? 0) : 0,
                Notes = user.ResourceProfile?.Notes,
                UtilizationPercent = utilPercent,
                IsOverAllocated = utilPercent > 100,
                Skills = user.ResourceProfile?.Skills.Select(s => new ResourceSkillViewModel
                {
                    Id = s.Id,
                    SkillName = s.SkillName,
                    ProficiencyLevel = s.ProficiencyLevel
                }).ToList() ?? new List<ResourceSkillViewModel>(),
                ActiveAllocations = allocations.Select(a => new ProjectAllocationSummaryViewModel
                {
                    Id = a.AllocationId,
                    ProjectId = a.ProjectId,
                    ProjectName = a.ProjectName,
                    AllocationPercentage = a.AllocationPercentage,
                    ProjectRole = a.ProjectRole,
                    StartDate = a.StartDate,
                    EndDate = a.EndDate
                }).ToList(),
                AvailabilityBlocks = user.AvailabilityBlocks.Select(b => new AvailabilityBlockViewModel
                {
                    Id = b.Id,
                    StartDate = b.StartDate,
                    EndDate = b.EndDate,
                    Reason = b.Reason,
                    Notes = b.Notes
                }).ToList()
            };

            return View(vm);
        }

        // POST: Resource/Profile/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> Profile(string id, ResourceProfileViewModel model)
        {
            if (id != model.UserId) return NotFound();

            if (ModelState.IsValid)
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null) return NotFound();

                var profile = await _resourceService.GetOrCreateProfileAsync(id);
                
                profile.Department = model.Department;
                profile.SeniorityLevel = model.SeniorityLevel;
                profile.DailyCapacityHours = model.DailyCapacityHours;
                profile.Notes = model.Notes;
                profile.IsResource = model.IsResource;

                if (User.IsInRole("Manager"))
                {
                    profile.HourlyRate = model.HourlyRate;
                }

                _context.Update(profile);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Resource profile updated successfully.";
                return RedirectToAction(nameof(Profile), new { id });
            }

            return View(model);
        }

        // GET: Resource/Allocate/5 (projectId)
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> Allocate(int? projectId)
        {
            if (projectId == null) return NotFound();

            var project = await _context.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            ViewBag.ProjectName = project.Name;
            ViewBag.Users = await _context.Users.OrderBy(u => u.FullName ?? u.Email).ToListAsync();

            return View(new EditProjectAllocationViewModel { ProjectId = project.Id, ProjectName = project.Name });
        }

        // POST: Resource/Allocate
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> Allocate([Bind("ProjectId,UserId,AllocationPercentage,ProjectRole,StartDate,EndDate")] EditProjectAllocationViewModel model)
        {
            if (ModelState.IsValid)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                
                var profile = await _resourceService.GetOrCreateProfileAsync(model.UserId);

                var allocation = new ProjectResourceAllocation
                {
                    ProjectId = model.ProjectId,
                    UserId = model.UserId,
                    AllocationPercentage = model.AllocationPercentage,
                    ProjectRole = model.ProjectRole,
                    StartDate = model.StartDate,
                    EndDate = model.EndDate,
                    AllocatedById = currentUser?.Id,
                    ResourceProfileId = profile.Id
                };

                _context.ProjectResourceAllocations.Add(allocation);
                await _context.SaveChangesAsync();

                // Check for over-allocation after adding
                bool overAllocated = await _resourceService.IsUserOverAllocatedAsync(
                    model.UserId, 
                    model.StartDate, 
                    model.EndDate ?? model.StartDate.AddMonths(3) // Check next 3 months if no end date
                );

                if (overAllocated)
                {
                    TempData["ResourceWarning"] = "Warning: This allocation pushes the user over 100% capacity for some or all of this period.";
                }

                return RedirectToAction("Details", "Projects", new { id = model.ProjectId });
            }

            ViewBag.ProjectName = model.ProjectName ?? "Project";
            ViewBag.Users = await _context.Users.OrderBy(u => u.FullName ?? u.Email).ToListAsync();
            return View(model);
        }
        // POST: Resource/Block — Record a leave / availability block
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> Block(string userId, DateTime startDate, DateTime endDate,
            AvailabilityBlockReason reason, string? notes)
        {
            if (string.IsNullOrEmpty(userId)) return BadRequest();
            if (endDate < startDate)
            {
                TempData["ErrorMessage"] = "End date must be on or after the start date.";
                return RedirectToAction(nameof(Profile), new { id = userId });
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var block = new ResourceAvailabilityBlock
            {
                UserId      = userId,
                StartDate   = startDate,
                EndDate     = endDate,
                Reason      = reason,
                Notes       = notes,
                CreatedById = currentUser?.Id
            };

            _context.ResourceAvailabilityBlocks.Add(block);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Availability block recorded: {reason} from {startDate:MMM d} to {endDate:MMM d}.";
            return RedirectToAction(nameof(Profile), new { id = userId });
        }

        // DELETE: Resource/Block/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Manager,Admin")]
        [ActionName("DeleteBlock")]
        public async Task<IActionResult> DeleteBlock(int id, string userId)
        {
            var block = await _context.ResourceAvailabilityBlocks.FindAsync(id);
            if (block != null)
            {
                _context.ResourceAvailabilityBlocks.Remove(block);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Availability block removed.";
            }
            return RedirectToAction(nameof(Profile), new { id = userId });
        }
    }
}
