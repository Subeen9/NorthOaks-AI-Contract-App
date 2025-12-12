using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using System.Text;

namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{
    public static class ContextBuilder
    {
        //  Deduplication (Jaccard similarity)
        private static double JaccardSimilarity(string a, string b)
        {
            var setA = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var setB = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var intersection = setA.Intersect(setB).Count();
            var union = setA.Union(setB).Count();
            return (double)intersection / union;
        }

        public static List<string> DeduplicateChunks(List<string> chunks, double threshold = 0.8)
        {
            var uniqueChunks = new List<string>();
            foreach (var chunk in chunks)
            {
                bool isDuplicate = uniqueChunks.Any(u => JaccardSimilarity(chunk, u) > threshold);
                if (!isDuplicate)
                    uniqueChunks.Add(chunk);
            }
            return uniqueChunks;
        }

    }
}
