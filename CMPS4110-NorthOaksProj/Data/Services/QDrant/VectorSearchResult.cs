namespace CMPS4110_NorthOaksProj.Data.Services.QDrant
{ 
        public class VectorSearchResult
        {
            public Guid PointId { get; set; }
            public float Score { get; set; }
            public int ContractId { get; set; }
            public int ChunkIndex { get; set; }

            public string ChunkText { get; set; } 
            public int PageNumber { get; set; }  
    }
}
