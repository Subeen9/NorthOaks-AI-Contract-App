using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Models.Users;
using NorthOaks.Shared.Model.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CMPS4110_NorthOaksProj.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly DataContext _context;
        private readonly UserManager<User> _userManager;

        public UsersController(DataContext context, UserManager<User> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<ActionResult<List<UserDto>>> GetAll()
        {
            var users = await _context.Users
                .Select(x => new UserDto
                {
                    Id = x.Id,
                    UserName = x.UserName,
                    FirstName = x.FirstName,
                    LastName = x.LastName,
                    Email = x.Email,
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<UserDto>> GetById(int id)
        {
            var user = await _context.Users.FindAsync(id);

            if (user == null)
                return NotFound();

            var userDto = new UserDto
            {
                Id = user.Id,
                UserName = user.UserName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
            };

            return Ok(userDto);
        }

        // Get current authenticated user
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<UserDto>> GetCurrentUser()
        {
            var currentUserName =
                User.FindFirstValue("preferred_username") ??
                User.FindFirstValue("unique_name") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.Identity?.Name ??
                "unknown_user";

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName.ToLower());

            if (user == null)
                return Unauthorized("User not found");

            var userDto = new UserDto
            {
                Id = user.Id,
                UserName = user.UserName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email
            };

            return Ok(userDto);
        }

        [HttpPost]
        public async Task<ActionResult<UserDto>> Create([FromBody] UserDto newUserDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var newUser = new User
            {
                UserName = newUserDto.UserName,
                FirstName = newUserDto.FirstName,
                LastName = newUserDto.LastName,
                Email = newUserDto.Email.ToLowerInvariant(),
            };

            var result = await _userManager.CreateAsync(newUser, newUserDto.Password);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            var userDto = new UserDto
            {
                Id = newUser.Id,
                UserName = newUser.UserName,
                FirstName = newUser.FirstName,
                LastName = newUser.LastName,
                Email = newUser.Email,
            };

            return CreatedAtAction(nameof(GetById), new { id = newUser.Id }, userDto);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<UserDto>> Edit(int id, [FromBody] UserDto updatedUserDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var existingUser = await _userManager.FindByIdAsync(id.ToString());

            if (existingUser == null)
                return NotFound();

            existingUser.UserName = updatedUserDto.UserName;
            existingUser.FirstName = updatedUserDto.FirstName;
            existingUser.LastName = updatedUserDto.LastName;
            existingUser.Email = updatedUserDto.Email.ToLowerInvariant();

            var result = await _userManager.UpdateAsync(existingUser);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            var userDto = new UserDto
            {
                Id = existingUser.Id,
                UserName = existingUser.UserName,
                FirstName = existingUser.FirstName,
                LastName = existingUser.LastName,
                Email = existingUser.Email,
            };

            return Ok(userDto);
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());

            if (user == null)
                return NotFound();

            var result = await _userManager.DeleteAsync(user);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return NoContent();
        }
    }
}