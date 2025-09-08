using System;

namespace CMPS4110_NorthOaksProj.Models.Contracts
{
    // DTO returned to the frontend when fetching contract info
    public class ContractReadDto
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public DateTime UploadDate { get; set; }
        public int UserId { get; set; }
        public string? UploadedBy { get; set; }
    }
}
