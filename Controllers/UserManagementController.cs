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
using OfficeTaskManagement.ViewModels.UserManagement;

namespace OfficeTaskManagement.Controllers
{
    [Authorize(Roles = "Manager")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;

        public UserManagementController(UserManager<User> userManager, RoleManager<IdentityRole> roleManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
        }

        // GET: UserManagement
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.ToListAsync();
            var userListViewModels = new List<UserListViewModel>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var totalTasks = await _context.Tasks.Where(t => t.AssigneeId == user.Id).CountAsync();
                var completedTasks = await _context.Tasks.Where(t => t.AssigneeId == user.Id && t.Status == Models.Enums.TaskStatus.Done).CountAsync();

                userListViewModels.Add(new UserListViewModel
                {
                    Id = user.Id,
                    Email = user.Email,
                    FullName = user.FullName,
                    Roles = roles.ToList(),
                    IsActive = !user.LockoutEnd.HasValue || user.LockoutEnd < DateTime.UtcNow,
                    CreatedDate = user.Id != null ? DateTime.UtcNow : null,
                    TotalTasks = totalTasks,
                    CompletedTasks = completedTasks
                });
            }

            return View(userListViewModels.OrderBy(u => u.Email).ToList());
        }

        // GET: UserManagement/Create
        public async Task<IActionResult> Create()
        {
            var availableRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            var vm = new CreateUserViewModel
            {
                AvailableRoles = availableRoles
            };
            return View(vm);
        }

        // POST: UserManagement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUserViewModel vm)
        {
            vm.AvailableRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var userExists = await _userManager.FindByEmailAsync(vm.Email);
            if (userExists != null)
            {
                ModelState.AddModelError("Email", "Email already exists.");
                return View(vm);
            }

            if (vm.Password != vm.ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");
                return View(vm);
            }

            var user = new User
            {
                UserName = vm.Email,
                Email = vm.Email,
                FullName = vm.FullName,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, vm.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
                return View(vm);
            }

            // Assign roles
            if (vm.SelectedRoles != null && vm.SelectedRoles.Any())
            {
                var roleResult = await _userManager.AddToRolesAsync(user, vm.SelectedRoles);
                if (!roleResult.Succeeded)
                {
                    foreach (var error in roleResult.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    return View(vm);
                }
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: UserManagement/Edit/5
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var availableRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();

            var vm = new EditUserViewModel
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                SelectedRoles = roles.ToList(),
                AvailableRoles = availableRoles,
                IsActive = !user.LockoutEnd.HasValue || user.LockoutEnd < DateTime.UtcNow
            };

            return View(vm);
        }

        // POST: UserManagement/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, EditUserViewModel vm)
        {
            if (id != vm.Id)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            vm.AvailableRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            // Update user details
            user.FullName = vm.FullName;

            // Update lock status
            if (vm.IsActive && user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            {
                user.LockoutEnd = null;
            }
            else if (!vm.IsActive)
            {
                user.LockoutEnd = DateTime.MaxValue;
            }

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                foreach (var error in updateResult.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
                return View(vm);
            }

            // Update roles
            var currentRoles = await _userManager.GetRolesAsync(user);
            var rolesToRemove = currentRoles.Except(vm.SelectedRoles ?? new List<string>()).ToList();
            var rolesToAdd = (vm.SelectedRoles ?? new List<string>()).Except(currentRoles).ToList();

            if (rolesToRemove.Any())
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    foreach (var error in removeResult.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    return View(vm);
                }
            }

            if (rolesToAdd.Any())
            {
                var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
                if (!addResult.Succeeded)
                {
                    foreach (var error in addResult.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    return View(vm);
                }
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: UserManagement/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Check if user has any tasks assigned
            var taskCount = await _context.Tasks.Where(t => t.AssigneeId == id).CountAsync();
            if (taskCount > 0)
            {
                TempData["ErrorMessage"] = "Cannot delete user with assigned tasks. Please reassign tasks first.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = "Error deleting user.";
            }
            else
            {
                TempData["SuccessMessage"] = "User deleted successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: UserManagement/ResetPassword/5
        public async Task<IActionResult> ResetPassword(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            return View(new { userId = id, email = user.Email });
        }

        // POST: UserManagement/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string id, string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                ModelState.AddModelError("", "Passwords do not match.");
                return View(new { userId = id });
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
                return View(new { userId = id });
            }

            TempData["SuccessMessage"] = "Password reset successfully.";
            return RedirectToAction(nameof(Index));
        }

        // POST: UserManagement/ToggleStatus/5
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            {
                user.LockoutEnd = null;
                await _userManager.UpdateAsync(user);
                TempData["SuccessMessage"] = "User activated.";
            }
            else
            {
                user.LockoutEnd = DateTime.MaxValue;
                await _userManager.UpdateAsync(user);
                TempData["SuccessMessage"] = "User deactivated.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
