using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorthOaks.Shared.Model.Contracts
{


    public class ContractReadDto
    {
        public int Id { get; set; }
        public string FileName { get; set; } = default!;
        public DateTime UploadDate { get; set; }
        public int UserId { get; set; }
        public string? UploadedBy { get; set; }
        public string FileUrl { get; set; }
        public bool IsPublic { get; set; } = false;

    }

}