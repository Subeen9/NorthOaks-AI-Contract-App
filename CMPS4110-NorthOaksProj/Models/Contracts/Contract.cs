using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Models.Users;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMPS4110_NorthOaksProj.Models.Contracts
{
    public class Contract : IEntityBase
    {
        public int Id { get; set; }
        [Required]
        public string FileName { get; set; }
        [Required]
        public DateTime UploadDate { get; set; }
        [Required]
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; }
        public string? OCRText { get; set; }
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public bool IsProcessed { get; set; } = false;
        public string? ProcessingStatus { get; set; }
        public bool IsPublic { get; set; } = false;
        [NotMapped]
        public string? UploadedBy => $"{User?.FirstName} {User?.LastName}";
    }
}
