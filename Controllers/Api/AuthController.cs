using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OfficeTaskManagement.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace OfficeTaskManagement.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IConfiguration _configuration;

        public AuthController(UserManager<User> userManager, SignInManager<User> signInManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
        }

        public class LoginModel
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email) 
                ?? await _userManager.FindByNameAsync(model.Email);
                
            if (user != null && await _userManager.CheckPasswordAsync(user, model.Password))
            {
                var authClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? ""),
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                };

                var userRoles = await _userManager.GetRolesAsync(user);
                foreach (var userRole in userRoles)
                {
                    authClaims.Add(new Claim(ClaimTypes.Role, userRole));
                }

                var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? ""));

                var token = new JwtSecurityToken(
                    issuer: _configuration["Jwt:Issuer"],
                    audience: _configuration["Jwt:Audience"],
                    expires: DateTime.UtcNow.AddDays(14),
                    claims: authClaims,
                    signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
                );

                return Ok(new
                {
                    token = new JwtSecurityTokenHandler().WriteToken(token),
                    expiration = token.ValidTo,
                    user = new { user.Id, user.FullName, user.Email, Roles = userRoles }
                });
            }
            return Unauthorized();
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet("portal-link")]
        public async Task<IActionResult> GetPortalLink([FromQuery] string returnUrl = "/")
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Unauthorized();

            if (string.IsNullOrEmpty(user.SecurityStamp))
            {
                await _userManager.UpdateSecurityStampAsync(user);
            }

            // Generate a secure, single-use token that expires quickly (e.g., in 1 minute)
            var tokenProvider = _userManager.Options.Tokens.PasswordResetTokenProvider;
            var token = await _userManager.GenerateUserTokenAsync(user, tokenProvider, "PortalLogin");

            // Encode the token securely for URL transmission to avoid VS Code Uri.parse quirks
            var encodedToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var encodedReturnUrl = Uri.EscapeDataString(returnUrl);

            // Construct the portal login URL (This would ideally use Url.Action or similar if routing allows)
            var portalUrl = $"/api/auth/exchange?token={encodedToken}&userId={user.Id}&returnUrl={encodedReturnUrl}";

            return Ok(new { url = portalUrl });
        }

        [HttpGet("exchange")]
        public async Task<IActionResult> ExchangeToken([FromQuery] string token, [FromQuery] string userId, [FromQuery] string returnUrl = "/")
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId)) return BadRequest();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Unauthorized();

            var decodedTokenBytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(token);
            var originalToken = Encoding.UTF8.GetString(decodedTokenBytes);

            var tokenProvider = _userManager.Options.Tokens.PasswordResetTokenProvider;
            var isValid = await _userManager.VerifyUserTokenAsync(user, tokenProvider, "PortalLogin", originalToken);

            if (!isValid) return Unauthorized("Invalid or expired session token.");

            // Sign the user in using Cookie authentication (default Identity scheme)
            await _signInManager.SignInAsync(user, isPersistent: false);

            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return Redirect("~/");
            }
        }
    }
}
