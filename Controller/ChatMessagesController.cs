using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Models.Chat;
using CMPS4110_NorthOaksProj.Models.Chat.Dtos;

namespace CMPS4110_NorthOaksProj.Controller
{
    [ApiController]
    [Route("api/chatmessages")]
    public class ChatMessagesController : ControllerBase
    {
        private readonly DataContext _db;
        public ChatMessagesController(DataContext db) => _db = db;

        
        [HttpGet("session/{sessionId:int}")]
        public async Task<ActionResult<IEnumerable<ChatMessageDto>>> GetBySession(int sessionId)
        {
            var exists = await _db.ChatSessions.AnyAsync(s => s.Id == sessionId);
            if (!exists) return NotFound();

            var messages = await _db.ChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    SessionId = m.SessionId,
                    Message = m.Message,
                    Response = m.Response,
                    Timestamp = m.Timestamp
                })
                .ToListAsync();

            return Ok(messages);
        }

       
        [HttpPost]
        public async Task<ActionResult<ChatMessageDto>> Create(CreateChatMessageDto dto)
        {
            var exists = await _db.ChatSessions.AnyAsync(s => s.Id == dto.SessionId);
            if (!exists) return BadRequest("Session does not exist.");

            var entity = new ChatMessage
            {
                SessionId = dto.SessionId,
                Message = dto.Message,
                Response = dto.Response
            };

            _db.ChatMessages.Add(entity);
            await _db.SaveChangesAsync();

            var result = new ChatMessageDto
            {
                Id = entity.Id,
                SessionId = entity.SessionId,
                Message = entity.Message,
                Response = entity.Response,
                Timestamp = entity.Timestamp
            };

            return CreatedAtAction(nameof(GetBySession), new { sessionId = entity.SessionId }, result);
        }

        
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var msg = await _db.ChatMessages.FindAsync(id);
            if (msg == null) return NotFound();

            _db.ChatMessages.Remove(msg);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
