using NorthOaks.Shared.Model.Users;
using CMPS4110_NorthOaksProj.Models.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CMPS4110_NorthOaksProj.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly Data.Services.TokenService tokenService;
        private readonly SignInManager<User> signInManager;
        private readonly UserManager<User> userManager;

        public AuthController(Data.Services.TokenService _tokenService, SignInManager<User> _signInManager, UserManager<User> _userManager)
        {
            tokenService = _tokenService;
            signInManager = _signInManager;
            userManager = _userManager;
        }

        [HttpPost("login")]
        public async Task<ActionResult<string>> Login([FromBody] LoginDto loginDto)
        {
            var user = await userManager.FindByNameAsync(loginDto.UserName);
            if (user == null)
                return Unauthorized("Invalid username or password.");
            var result = await signInManager.CheckPasswordSignInAsync(user, loginDto.Password, false);
            if (!result.Succeeded)
                return Unauthorized("Invalid username or password.");
            var token = tokenService.GenerateToken(user);
            return Ok(token);
        }

        [HttpPost("logout")]
        public async Task<ActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            return Ok("User logged out successfully.");
        }
    }
}
