namespace NorthOaks.Shared.Models
{
    public class ContractStatus
    {
        public int Id { get; set; }
        public bool IsProcessed { get; set; }
        public string? ProcessingStatus { get; set; }
    }
}
