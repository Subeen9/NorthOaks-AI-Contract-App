using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Models.Chat;
//using CMPS4110_NorthOaksProj.Models.Chat.Dtos;
using NorthOaks.Shared.Model.Chat;

namespace CMPS4110_NorthOaksProj.Controller
{
    [ApiController]
    [Route("api/chatsessions")]
    public class ChatSessionsController : ControllerBase
    {
        private readonly DataContext _db;
        public ChatSessionsController(DataContext db) => _db = db;

        
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ChatSessionDto>>> GetAll()
        {
            var sessions = await _db.ChatSessions
                .Include(s => s.Messages)
                .Include(s => s.SessionContracts)
                .Select(s => new ChatSessionDto
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    CreatedDate = s.CreatedDate,
                    MessageCount = s.Messages.Count,
                    ContractIds = s.SessionContracts.Select(sc => sc.ContractId).ToList()
                })
                .ToListAsync();

            return Ok(sessions);
        }

       
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ChatSessionDto>> Get(int id)
        {
            var s = await _db.ChatSessions
                .Include(x => x.Messages)
                .Include(x => x.SessionContracts)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (s == null) return NotFound();

            return new ChatSessionDto
            {
                Id = s.Id,
                UserId = s.UserId,
                CreatedDate = s.CreatedDate,
                MessageCount = s.Messages.Count,
                ContractIds = s.SessionContracts.Select(sc => sc.ContractId).ToList()
            };
        }

        [HttpGet("user/{userId}")]
        public async Task<ActionResult<IEnumerable<ChatSessionDto>>> GetUserSessions(int userId)
        {
            var sessions = await _db.ChatSessions
                .Include(s => s.Messages)
                .Include(s => s.SessionContracts)
                    .ThenInclude(sc => sc.Contract)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.CreatedDate)
                .Select(s => new ChatSessionDto
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    CreatedDate = s.CreatedDate,
                    MessageCount = s.Messages.Count,
                    ContractIds = s.SessionContracts.Select(sc => sc.ContractId).ToList(),
                    Contracts = s.SessionContracts.Select(sc => new ContractInfoDto
                    {
                        Id = sc.Contract.Id,
                        FileName = sc.Contract.FileName
                    }).ToList()
                })
                .ToListAsync();

            return Ok(sessions);
        }

        [HttpPost]
        public async Task<ActionResult<ChatSessionDto>> Create(CreateChatSessionDto dto)
        {
            if (dto.ContractIds is { Count: > 2 })
                return BadRequest(new { error = "Maximum 2 contracts can be compared" });

            // reuse single-doc sessions, but always create new for comparisons
            if (dto.ContractIds is { Count: 1 })
            {
                var existing = await _db.ChatSessions
                    .Include(s => s.Messages)
                    .Include(s => s.SessionContracts)
                    .Where(s => s.UserId == dto.UserId)
                    .FirstOrDefaultAsync(s => s.SessionContracts.Any(sc => dto.ContractIds.Contains(sc.ContractId)));

                if (existing != null)
                {
                    var existingDto = new ChatSessionDto
                    {
                        Id = existing.Id,
                        UserId = existing.UserId,
                        CreatedDate = existing.CreatedDate,
                        MessageCount = existing.Messages.Count,
                        ContractIds = existing.SessionContracts.Select(sc => sc.ContractId).ToList(),
                        SessionType = (int)existing.SessionType
                    };
                    return Ok(existingDto);
                }
            }

            var entity = new ChatSession
            {
                UserId = dto.UserId,
                SessionType = (dto.ContractIds?.Count ?? 0) > 1 ? ChatSessionType.Comparison : ChatSessionType.Single
            };
            _db.ChatSessions.Add(entity);

            if (dto.ContractIds is { Count: > 0 })
            {
                foreach (var cid in dto.ContractIds.Distinct())
                {
                    _db.ChatSessionContracts.Add(new ChatSessionContract
                    {
                        ChatSession = entity,
                        ContractId = cid
                    });
                }
            }

            await _db.SaveChangesAsync();

            var result = new ChatSessionDto
            {
                Id = entity.Id,
                UserId = entity.UserId,
                CreatedDate = entity.CreatedDate,
                MessageCount = 0,
                ContractIds = dto.ContractIds ?? new List<int>(),
                SessionType = (int)entity.SessionType
            };

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, result);
        }



        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var session = await _db.ChatSessions.FindAsync(id);
            if (session == null) return NotFound();

            _db.ChatSessions.Remove(session);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
