using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Models.Chat;
using CMPS4110_NorthOaksProj.Models.Chat.Dtos;
using CMPS4110_NorthOaksProj.Data.Services.Chat.Messages;

namespace CMPS4110_NorthOaksProj.Controller
{
    [ApiController]
    [Route("api/chatmessages")]
    public class ChatMessagesController : ControllerBase
    {
        private readonly IChatMessagesService _chatMessagesService;
        public ChatMessagesController(IChatMessagesService chatMessagesService) => _chatMessagesService = chatMessagesService;

        
        [HttpGet("session/{sessionId:int}")]
        public async Task<ActionResult<IEnumerable<ChatMessageDto>>> GetBySession(int sessionId)
        {
            var messages = await _chatMessagesService.GetBySession(sessionId);
            if (messages == null || !messages.Any()) return NotFound();
            return Ok(messages);
        }

        [HttpPost]
        public async Task<ActionResult<ChatMessageDto>> Create(CreateChatMessageDto dto)
        {
           var result = await _chatMessagesService.Create(dto);
           if (result == null) return BadRequest("Invalid session ID.");
           return CreatedAtAction(nameof(GetBySession), new { sessionId = result.SessionId }, result);
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            var action = await _chatMessagesService.Delete(id);
            if (!action) return NotFound();
            return NoContent();
        }
    }
}
