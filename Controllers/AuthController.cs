using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace YoutubeGenerator.Controllers
{
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IConfiguration configuration, ILogger<AuthController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Authenticates the user and sets a secure authentication cookie.
        /// Default fallback credentials: admin / admin123
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Username and password are required." });
            }

            var expectedUser = _configuration["Dashboard:AdminUsername"] 
                               ?? Environment.GetEnvironmentVariable("DASHBOARD_ADMIN_USER") 
                               ?? "admin";

            var expectedPass = _configuration["Dashboard:AdminPassword"] 
                               ?? Environment.GetEnvironmentVariable("DASHBOARD_ADMIN_PASS") 
                               ?? "admin123";

            if (request.Username.Equals(expectedUser, StringComparison.OrdinalIgnoreCase) && request.Password == expectedPass)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, request.Username),
                    new Claim(ClaimTypes.Role, "Admin"),
                    new Claim("Permission", "ReadSensitive"),
                    new Claim("Permission", "WriteBags"),
                    new Claim("Permission", "ManageWorkers")
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

                _logger.LogInformation($"User '{request.Username}' successfully authenticated.");

                return Ok(new
                {
                    success = true,
                    message = "Authenticated successfully.",
                    user = new
                    {
                        username = request.Username,
                        role = "Admin",
                        permissions = new[] { "read", "write", "configure", "sensitive_read" }
                    }
                });
            }

            _logger.LogWarning($"Failed login attempt for username '{request.Username}'.");
            return Unauthorized(new { success = false, message = "Invalid username or password." });
        }

        /// <summary>
        /// Logs out the current user and clears the authentication cookie.
        /// </summary>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { success = true, message = "Logged out successfully." });
        }

        /// <summary>
        /// Checks current authentication status and user details.
        /// </summary>
        [HttpGet("me")]
        [HttpGet("status")]
        public IActionResult GetCurrentUser()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Ok(new
                {
                    isAuthenticated = true,
                    user = new
                    {
                        username = User.Identity.Name ?? "admin",
                        role = "Admin",
                        permissions = new[] { "read", "write", "configure", "sensitive_read" }
                    }
                });
            }

            return Ok(new
            {
                isAuthenticated = false,
                user = (object?)null
            });
        }
    }
}
