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
using OfficeTaskManagement.Services;
using OfficeTaskManagement.ViewModels.Organization;

namespace OfficeTaskManagement.Controllers
{
    [Authorize]
    [HasPermission(Permissions.UsersManage)]
    public class OrganizationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ITenantProvider _tenantProvider;

        public OrganizationController(
            ApplicationDbContext context,
            UserManager<User> userManager,
            ITenantProvider tenantProvider)
        {
            _context = context;
            _userManager = userManager;
            _tenantProvider = tenantProvider;
        }

        // GET: Organization
        public async Task<IActionResult> Index()
        {
            var tenantId = _tenantProvider.TenantId;
            
            // Retrieve Tenant information
            var tenant = await _context.Set<Tenant>()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
            {
                return NotFound("Tenant not found.");
            }

            // Get statistics using IgnoreQueryFilters to isolate counts accurately by TenantId
            var userCount = await _userManager.Users.IgnoreQueryFilters()
                .CountAsync(u => u.TenantId == tenantId);

            var projectCount = await _context.Projects.IgnoreQueryFilters()
                .CountAsync(p => p.TenantId == tenantId);

            var sprintCount = await _context.Sprints.IgnoreQueryFilters()
                .CountAsync(s => s.TenantId == tenantId);

            // Fetch members of the organization
            var users = await _userManager.Users.IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantId)
                .ToListAsync();

            var memberList = new List<MemberViewModel>();
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                memberList.Add(new MemberViewModel
                {
                    UserId = user.Id,
                    Email = user.Email ?? string.Empty,
                    FullName = user.FullName ?? string.Empty,
                    JobTitle = user.JobTitle ?? string.Empty,
                    Department = user.Department ?? string.Empty,
                    AvatarPath = user.AvatarPath ?? string.Empty,
                    Roles = roles.ToList()
                });
            }

            // Fetch pending invitations (query filters apply automatically for IMustHaveTenant)
            var invitations = await _context.OrganizationInvitations
                .Where(i => !i.IsAccepted && i.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();

            var invitationList = invitations.Select(i => new InvitationViewModel
            {
                Email = i.Email,
                Role = i.Role,
                InviteCode = i.InviteCode,
                ExpiresAt = i.ExpiresAt
            }).ToList();

            var viewModel = new OrganizationIndexViewModel
            {
                TenantId = tenant.Id,
                Name = tenant.Name,
                Identifier = tenant.Identifier,
                CreatedAt = tenant.CreatedAt,
                UserCount = userCount,
                ProjectCount = projectCount,
                SprintCount = sprintCount,
                Members = memberList,
                PendingInvitations = invitationList
            };

            return View(viewModel);
        }

        // POST: Organization/UpdateName
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["ErrorMessage"] = "Organization Name cannot be empty.";
                return RedirectToAction(nameof(Index));
            }

            var tenantId = _tenantProvider.TenantId;
            var tenant = await _context.Set<Tenant>()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
            {
                return NotFound("Tenant not found.");
            }

            tenant.Name = name.Trim();
            _context.Update(tenant);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Organization name updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Organization/InviteMember
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InviteMember(string email, string role)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Email address cannot be empty.";
                return RedirectToAction(nameof(Index));
            }

            var tenantId = _tenantProvider.TenantId;

            // Check if user already exists
            var existingUser = await _userManager.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpper() && u.TenantId == tenantId);

            if (existingUser != null)
            {
                TempData["ErrorMessage"] = "A user with this email address already belongs to the organization.";
                return RedirectToAction(nameof(Index));
            }

            // Check if a pending active invitation already exists for this email
            var existingInvite = await _context.OrganizationInvitations
                .FirstOrDefaultAsync(i => i.Email == email.Trim() && !i.IsAccepted && i.ExpiresAt > DateTime.UtcNow);

            if (existingInvite != null)
            {
                TempData["ErrorMessage"] = "An active invitation has already been sent to this email address.";
                return RedirectToAction(nameof(Index));
            }

            var invite = new OrganizationInvitation
            {
                Email = email.Trim(),
                TenantId = tenantId,
                Role = role ?? "Developer",
                InviteCode = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _context.OrganizationInvitations.Add(invite);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Invitation generated successfully.";
            TempData["CreatedInviteCode"] = invite.InviteCode;
            TempData["CreatedInviteEmail"] = invite.Email;

            return RedirectToAction(nameof(Index));
        }

        // POST: Organization/CancelInvite
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelInvite(string inviteCode)
        {
            if (string.IsNullOrWhiteSpace(inviteCode))
            {
                TempData["ErrorMessage"] = "Invalid invitation code.";
                return RedirectToAction(nameof(Index));
            }

            var invite = await _context.OrganizationInvitations
                .FirstOrDefaultAsync(i => i.InviteCode == inviteCode);

            if (invite == null)
            {
                TempData["ErrorMessage"] = "Invitation not found.";
                return RedirectToAction(nameof(Index));
            }

            _context.OrganizationInvitations.Remove(invite);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Invitation cancelled successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
