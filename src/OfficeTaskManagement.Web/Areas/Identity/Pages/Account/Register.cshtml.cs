// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeTaskManagement.Data;
using OfficeTaskManagement.Models;
using OfficeTaskManagement.Services;

namespace OfficeTaskManagement.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        private readonly IUserStore<User> _userStore;
        private readonly IUserEmailStore<User> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly IServiceProvider _serviceProvider;

        public RegisterModel(
            UserManager<User> userManager,
            IUserStore<User> userStore,
            SignInManager<User> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            ApplicationDbContext context,
            ITenantProvider tenantProvider,
            IServiceProvider serviceProvider)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _context = context;
            _tenantProvider = tenantProvider;
            _serviceProvider = serviceProvider;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        [BindProperty(SupportsGet = true)]
        public string InviteCode { get; set; }

        public string InvitedOrganizationName { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "Registration Type")]
            public string RegisterType { get; set; } = "create"; // "create" or "join"

            [Display(Name = "Organization Name")]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 3)]
            public string OrganizationName { get; set; }

            [Required]
            [RegularExpression(@"^[a-zA-Z0-9\-]+$", ErrorMessage = "Organization Identifier must contain only letters, numbers, and hyphens.")]
            [StringLength(50, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 3)]
            [Display(Name = "Organization Identifier (Slug)")]
            public string OrganizationSlug { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 2)]
            [Display(Name = "Full Name")]
            public string FullName { get; set; }

            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string returnUrl = null, string inviteCode = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (!string.IsNullOrEmpty(inviteCode))
            {
                InviteCode = inviteCode;
                var invite = await _context.OrganizationInvitations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(i => i.InviteCode == inviteCode && !i.IsAccepted && i.ExpiresAt > DateTime.UtcNow);

                if (invite == null)
                {
                    ModelState.AddModelError(string.Empty, "The invitation is invalid, has expired, or has already been used.");
                    return Page();
                }

                var tenant = await _context.Set<Tenant>()
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == invite.TenantId);

                if (tenant == null)
                {
                    ModelState.AddModelError(string.Empty, "Inviting organization was not found.");
                    return Page();
                }

                InvitedOrganizationName = tenant.Name;
                Input = new InputModel
                {
                    RegisterType = "join",
                    OrganizationSlug = tenant.Identifier,
                    Email = invite.Email
                };
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            OrganizationInvitation invitation = null;
            if (!string.IsNullOrEmpty(InviteCode))
            {
                invitation = await _context.OrganizationInvitations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(i => i.InviteCode == InviteCode && !i.IsAccepted && i.ExpiresAt > DateTime.UtcNow);

                if (invitation == null)
                {
                    ModelState.AddModelError(string.Empty, "The invitation is invalid, has expired, or has already been used.");
                    return Page();
                }

                Input.RegisterType = "join";
                Input.Email = invitation.Email;
            }

            // Conditional validation for Organization Name
            if (Input.RegisterType == "create" && string.IsNullOrWhiteSpace(Input.OrganizationName))
            {
                ModelState.AddModelError("Input.OrganizationName", "Organization Name is required when creating a new organization.");
            }

            if (ModelState.IsValid)
            {
                string tenantId = string.Empty;
                string organizationSlug = Input.OrganizationSlug.Trim().ToLowerInvariant();

                if (Input.RegisterType == "create")
                {
                    // Check if tenant identifier is already taken
                    var existingTenant = await _context.Set<Tenant>()
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(t => t.Identifier == organizationSlug);

                    if (existingTenant != null)
                    {
                        ModelState.AddModelError("Input.OrganizationSlug", $"The organization identifier '{organizationSlug}' is already taken.");
                        return Page();
                    }

                    // Check if email already registered for this tenant (using ignore query filters, but we need to check if user exists under new tenant, which won't exist yet, but let's check generally)
                    var existingUser = await _userManager.Users.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(u => u.NormalizedEmail == Input.Email.ToUpperInvariant());

                    if (existingUser != null)
                    {
                        ModelState.AddModelError("Input.Email", "A user with this email address already exists.");
                        return Page();
                    }

                    // Create Tenant
                    var newTenant = new Tenant
                    {
                        Name = Input.OrganizationName.Trim(),
                        Identifier = organizationSlug
                    };

                    _context.Set<Tenant>().Add(newTenant);
                    await _context.SaveChangesAsync();
                    tenantId = newTenant.Id;

                    // Seed default Roles, Permission Groups, Areas, and Holidays for the new tenant
                    await SeedData.SeedNewTenantAsync(_serviceProvider, tenantId);
                }
                else // join
                {
                    // Verify if tenant exists
                    var existingTenant = await _context.Set<Tenant>()
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(t => t.Identifier == organizationSlug);

                    if (existingTenant == null)
                    {
                        ModelState.AddModelError("Input.OrganizationSlug", $"The organization with identifier '{organizationSlug}' was not found.");
                        return Page();
                    }

                    tenantId = existingTenant.Id;

                    // Verify if user is already registered under this tenant
                    var existingUser = await _userManager.Users.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(u => u.NormalizedEmail == Input.Email.ToUpperInvariant() && u.TenantId == tenantId);

                    if (existingUser != null)
                    {
                        ModelState.AddModelError("Input.Email", "A user with this email address is already registered in this organization.");
                        return Page();
                    }
                }

                // Create User
                var user = CreateUser();
                user.FullName = Input.FullName.Trim();
                user.TenantId = tenantId;

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                
                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    // Assign appropriate default role
                    if (invitation != null)
                    {
                        await _userManager.AddToRoleAsync(user, invitation.Role);
                        invitation.IsAccepted = true;
                        _context.OrganizationInvitations.Update(invitation);
                        await _context.SaveChangesAsync();
                    }
                    else if (Input.RegisterType == "create")
                    {
                        // Creator becomes Super Admin
                        await _userManager.AddToRoleAsync(user, "Super Admin");
                    }
                    else
                    {
                        // Joinee becomes Developer
                        await _userManager.AddToRoleAsync(user, "Developer");
                    }

                    // Set cookie to remember the tenant choice
                    Response.Cookies.Append("TenantId", tenantId, new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddDays(14),
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax
                    });

                    // Temporarily set active tenant in provider
                    _tenantProvider.SetTenant(tenantId);

                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                        protocol: Request.Scheme);

                    await _emailSender.SendEmailAsync(Input.Email, "Confirm your email",
                        $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
                    }
                    else
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        private User CreateUser()
        {
            try
            {
                return Activator.CreateInstance<User>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(User)}'. " +
                    $"Ensure that '{nameof(User)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
            }
        }

        private IUserEmailStore<User> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<User>)_userStore;
        }
    }
}

