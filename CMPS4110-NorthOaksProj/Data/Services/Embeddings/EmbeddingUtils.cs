
namespace CMPS4110_NorthOaksProj.Data.Services.Embeddings
{
    public static class EmbeddingUtils
    {
        public static float[] L2Normalize(float[] vector)
        {
            var norm = MathF.Sqrt(vector.Sum(v => v * v));
            if (norm == 0) return vector;
            return vector.Select(v => v / norm).ToArray();
        }

        public static float[][] NormalizeBatch(float[][] embeddings)
        {
            return embeddings.Select(L2Normalize).ToArray();
        }
    }
}
