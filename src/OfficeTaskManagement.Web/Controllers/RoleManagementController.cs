using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services.Authorization;
using OfficeTaskManagement.ViewModels.RoleManagement;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    public class RoleManagementController : Controller
    {
        private readonly RoleManager<AppRole> _roleManager;
        private readonly UserManager<User> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IPermissionService _permissionService;

        public RoleManagementController(
            RoleManager<AppRole> roleManager,
            UserManager<User> userManager,
            ApplicationDbContext context,
            IPermissionService permissionService)
        {
            _roleManager      = roleManager;
            _userManager      = userManager;
            _context          = context;
            _permissionService = permissionService;
        }

        // ── Access Guard ──────────────────────────────────────────────────────

        private async Task<bool> CanManageRolesAsync() =>
            await _permissionService.HasPermissionAsync(User, Permissions.RolesManage);

        // ── Roles Index ───────────────────────────────────────────────────────

        public async Task<IActionResult> Index()
        {
            if (!await CanManageRolesAsync()) return Forbid();

            var roles = await _roleManager.Roles
                .Include(r => r.PermissionGroups)
                    .ThenInclude(rpg => rpg.PermissionGroup)
                .OrderBy(r => r.HierarchyLevel)
                .ToListAsync();

            var vmList = new List<RoleListViewModel>();
            foreach (var role in roles)
            {
                var users = await _userManager.GetUsersInRoleAsync(role.Name!);
                vmList.Add(new RoleListViewModel
                {
                    Id                  = role.Id,
                    Name                = role.Name!,
                    Description         = role.Description,
                    Color               = role.Color ?? "#6c757d",
                    Icon                = role.Icon ?? "fas fa-user",
                    HierarchyLevel      = role.HierarchyLevel,
                    IsSystemRole        = role.IsSystemRole,
                    UserCount           = users.Count,
                    PermissionGroupNames = role.PermissionGroups
                        .Select(rpg => rpg.PermissionGroup.Name)
                        .OrderBy(n => n)
                        .ToList()
                });
            }

            return View(new RoleIndexViewModel { Roles = vmList });
        }

        // ── Create Role ───────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> CreateRole()
        {
            if (!await CanManageRolesAsync()) return Forbid();
            return View(await BuildCreateVm(new CreateRoleViewModel()));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRole(CreateRoleViewModel vm)
        {
            if (!await CanManageRolesAsync()) return Forbid();
            vm = await BuildCreateVm(vm);
            if (!ModelState.IsValid) return View(vm);

            if (await _roleManager.RoleExistsAsync(vm.Name))
            {
                ModelState.AddModelError("Name", "A role with this name already exists.");
                return View(vm);
            }

            var role = new AppRole
            {
                Name           = vm.Name,
                Description    = vm.Description,
                Color          = vm.Color,
                Icon           = vm.Icon,
                HierarchyLevel = vm.HierarchyLevel,
                IsSystemRole   = false
            };
            var result = await _roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
                return View(vm);
            }

            await AssignGroupsAsync(role.Id, vm.SelectedGroupIds);
            TempData["SuccessMessage"] = $"Role '{role.Name}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ── Edit Role ─────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> EditRole(string id)
        {
            if (!await CanManageRolesAsync()) return Forbid();

            var role = await _roleManager.Roles
                .Include(r => r.PermissionGroups)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (role == null) return NotFound();

            var vm = new EditRoleViewModel
            {
                Id             = role.Id,
                Name           = role.Name!,
                Description    = role.Description,
                Color          = role.Color ?? "#6c757d",
                Icon           = role.Icon ?? "fas fa-user",
                HierarchyLevel = role.HierarchyLevel,
                IsSystemRole   = role.IsSystemRole,
                SelectedGroupIds = role.PermissionGroups.Select(rpg => rpg.PermissionGroupId).ToList()
            };

            return View(await BuildEditVm(vm));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRole(string id, EditRoleViewModel vm)
        {
            if (!await CanManageRolesAsync()) return Forbid();
            if (id != vm.Id) return NotFound();

            vm = await BuildEditVm(vm);
            if (!ModelState.IsValid) return View(vm);

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();

            // Prevent lowering Super Admin hierarchy level
            if (role.HierarchyLevel == 0 && vm.HierarchyLevel != 0)
            {
                ModelState.AddModelError("HierarchyLevel", "The Super Admin hierarchy level cannot be changed.");
                return View(vm);
            }

            role.Description    = vm.Description;
            role.Color          = vm.Color;
            role.Icon           = vm.Icon;
            role.HierarchyLevel = vm.HierarchyLevel;

            // Allow name change on non-system roles only
            if (!role.IsSystemRole) role.Name = vm.Name;

            var result = await _roleManager.UpdateAsync(role);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
                return View(vm);
            }

            // Re-sync permission groups
            var existing = await _context.AppRolePermissionGroups
                .Where(x => x.RoleId == id)
                .ToListAsync();
            _context.AppRolePermissionGroups.RemoveRange(existing);
            await _context.SaveChangesAsync();
            await AssignGroupsAsync(id, vm.SelectedGroupIds);

            TempData["SuccessMessage"] = $"Role '{role.Name}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ── Delete Role ───────────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRole(string id)
        {
            if (!await CanManageRolesAsync()) return Forbid();

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();

            if (role.IsSystemRole)
            {
                TempData["ErrorMessage"] = "System roles cannot be deleted.";
                return RedirectToAction(nameof(Index));
            }

            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
            if (usersInRole.Any())
            {
                TempData["ErrorMessage"] = $"Cannot delete role '{role.Name}' — {usersInRole.Count} user(s) are assigned to it. Reassign them first.";
                return RedirectToAction(nameof(Index));
            }

            await _roleManager.DeleteAsync(role);
            TempData["SuccessMessage"] = $"Role '{role.Name}' deleted.";
            return RedirectToAction(nameof(Index));
        }

        // ── Permission Groups ─────────────────────────────────────────────────

        public async Task<IActionResult> PermissionGroups()
        {
            if (!await CanManageRolesAsync()) return Forbid();

            var groups = await _context.PermissionGroups
                .Include(g => g.Permissions)
                .Include(g => g.Roles)
                    .ThenInclude(rpg => rpg.Role)
                .OrderBy(g => g.Name)
                .ToListAsync();

            var vm = new PermissionGroupIndexViewModel
            {
                AllKnownKeys = Permissions.All,
                Groups = groups.Select(g => new PermissionGroupViewModel
                {
                    Id               = g.Id,
                    Name             = g.Name,
                    Description      = g.Description,
                    IsSystemGroup    = g.IsSystemGroup,
                    Keys             = g.Permissions.Select(p => p.Key).OrderBy(k => k).ToList(),
                    AssignedRoleNames = g.Roles.Select(r => r.Role.Name!).OrderBy(n => n).ToList()
                }).ToList()
            };

            return View(vm);
        }

        // ── Create Permission Group ───────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> CreateGroup()
        {
            if (!await CanManageRolesAsync()) return Forbid();
            return View(new CreatePermissionGroupViewModel { AllKnownKeys = Permissions.All });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateGroup(CreatePermissionGroupViewModel vm)
        {
            if (!await CanManageRolesAsync()) return Forbid();
            vm.AllKnownKeys = Permissions.All;
            if (!ModelState.IsValid) return View(vm);

            if (await _context.PermissionGroups.AnyAsync(g => g.Name == vm.Name))
            {
                ModelState.AddModelError("Name", "A permission group with this name already exists.");
                return View(vm);
            }

            var group = new PermissionGroup
            {
                Name         = vm.Name,
                Description  = vm.Description,
                IsSystemGroup = false,
                Permissions  = vm.SelectedKeys.Distinct()
                    .Select(k => new PermissionGroupKey { Key = k }).ToList()
            };

            _context.PermissionGroups.Add(group);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Permission group '{group.Name}' created.";
            return RedirectToAction(nameof(PermissionGroups));
        }

        // ── Edit Permission Group ─────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> EditGroup(int id)
        {
            if (!await CanManageRolesAsync()) return Forbid();

            var group = await _context.PermissionGroups
                .Include(g => g.Permissions)
                .FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound();

            return View(new EditPermissionGroupViewModel
            {
                Id           = group.Id,
                Name         = group.Name,
                Description  = group.Description,
                IsSystemGroup = group.IsSystemGroup,
                SelectedKeys = group.Permissions.Select(p => p.Key).ToList(),
                AllKnownKeys = Permissions.All
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditGroup(int id, EditPermissionGroupViewModel vm)
        {
            if (!await CanManageRolesAsync()) return Forbid();
            vm.AllKnownKeys = Permissions.All;
            if (!ModelState.IsValid) return View(vm);

            var group = await _context.PermissionGroups
                .Include(g => g.Permissions)
                .FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound();

            group.Name        = vm.Name;
            group.Description = vm.Description;

            // Re-sync keys
            _context.PermissionGroupKeys.RemoveRange(group.Permissions);
            group.Permissions = vm.SelectedKeys.Distinct()
                .Select(k => new PermissionGroupKey { Key = k, PermissionGroupId = id }).ToList();

            await _context.SaveChangesAsync();

            // Bust caches for all users who have a role assigned to this group
            // (simplified: wipe all — in production this could be targeted)
            TempData["SuccessMessage"] = $"Permission group '{group.Name}' updated. Active sessions will reflect changes on next login.";
            return RedirectToAction(nameof(PermissionGroups));
        }

        // ── Delete Permission Group ───────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteGroup(int id)
        {
            if (!await CanManageRolesAsync()) return Forbid();

            var group = await _context.PermissionGroups
                .Include(g => g.Roles)
                .FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound();

            if (group.IsSystemGroup)
            {
                TempData["ErrorMessage"] = "System permission groups cannot be deleted.";
                return RedirectToAction(nameof(PermissionGroups));
            }

            if (group.Roles.Any())
            {
                TempData["ErrorMessage"] = $"Cannot delete '{group.Name}' — it is assigned to {group.Roles.Count} role(s). Unassign it first.";
                return RedirectToAction(nameof(PermissionGroups));
            }

            _context.PermissionGroups.Remove(group);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Permission group '{group.Name}' deleted.";
            return RedirectToAction(nameof(PermissionGroups));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task AssignGroupsAsync(string roleId, List<int> groupIds)
        {
            foreach (var gid in groupIds.Distinct())
            {
                if (await _context.PermissionGroups.AnyAsync(g => g.Id == gid))
                {
                    _context.AppRolePermissionGroups.Add(new AppRolePermissionGroup
                    {
                        RoleId            = roleId,
                        PermissionGroupId = gid
                    });
                }
            }
            await _context.SaveChangesAsync();
        }

        private async Task<CreateRoleViewModel> BuildCreateVm(CreateRoleViewModel vm)
        {
            vm.AvailableGroups = await BuildGroupPickers();
            return vm;
        }

        private async Task<EditRoleViewModel> BuildEditVm(EditRoleViewModel vm)
        {
            vm.AvailableGroups = await BuildGroupPickers();
            return vm;
        }

        private async Task<List<PermissionGroupPickerItem>> BuildGroupPickers()
        {
            return await _context.PermissionGroups
                .Include(g => g.Permissions)
                .OrderBy(g => g.Name)
                .Select(g => new PermissionGroupPickerItem
                {
                    Id          = g.Id,
                    Name        = g.Name,
                    Description = g.Description,
                    Keys        = g.Permissions.Select(p => p.Key).ToList()
                })
                .ToListAsync();
        }
    }
}
