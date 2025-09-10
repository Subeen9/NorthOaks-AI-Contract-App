using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace CMPS4110_NorthOaksProj.Models.Contracts
{
   
    public class ContractUploadDto
    {
        [Required]
        public IFormFile File { get; set; }

        [Required]
        public int UserId { get; set; }
    }
}
