using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CMPS4110_NorthOaksProj.Data.Base;

namespace CMPS4110_NorthOaksProj.Models.Contracts
{
    public class ContractEmbedding : IEntityBase
    {
        public int Id { get; set; }
        [Required] public int ContractId { get; set; }
        [ForeignKey("ContractId")]public Contract Contract { get; set; } = null!;
        [Required, Column(TypeName = "nvarchar(max)")] public string ChunkText { get; set; } = "";
        [Required] public int ChunkIndex { get; set; }
        [Required] public Guid QdrantPointId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


    }
}
