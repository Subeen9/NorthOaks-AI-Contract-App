        using System;
        using System.Linq;

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

                public static float GetVectorNorm(float[] vector)
                {
                    return MathF.Sqrt(vector.Sum(v => v * v));
                }

                public static void PrintNormStats(float[][] vectors)
                {
                    if (vectors.Length == 0)
                    {
                        Console.WriteLine("No vectors to check.");
                        return;
                    }

                    var norms = vectors.Select(GetVectorNorm).ToArray();
                    var avg = norms.Average();
                    var min = norms.Min();
                    var max = norms.Max();

                    Console.WriteLine($"Embedding Norm Stats -> Avg: {avg:F4}, Min: {min:F4}, Max: {max:F4}");
                }
            }
        }
