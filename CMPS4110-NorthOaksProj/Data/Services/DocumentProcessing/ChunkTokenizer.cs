using System.Text.RegularExpressions;

namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{
    public class ChunkTokenizer
    {
        private readonly Dictionary<string, int> _vocab;
        private readonly int _clsToken = 101;
        private readonly int _sepToken = 102;

        public ChunkTokenizer(string vocabFile)
        {
            _vocab = File.ReadAllLines(vocabFile)
                         .Select((token, idx) => new { token, idx })
                         .ToDictionary(x => x.token, x => x.idx);
        }

        public int[] Tokenize(string text, int maxLength = 256)
        {
            text = text.ToLowerInvariant();
            var words = Regex.Split(text, @"\s+");
            var tokens = new List<int> { _clsToken };

            foreach (var word in words)
            {
                if (_vocab.ContainsKey(word))
                    tokens.Add(_vocab[word]);
                else
                    tokens.Add(_vocab.ContainsKey("[UNK]") ? _vocab["[UNK]"] : 100);
            }

            tokens.Add(_sepToken);

            if (tokens.Count < maxLength)
                tokens.AddRange(Enumerable.Repeat(0, maxLength - tokens.Count));
            else if (tokens.Count > maxLength)
                tokens = tokens.Take(maxLength).ToList();

            return tokens.ToArray();
        }
    }
}
