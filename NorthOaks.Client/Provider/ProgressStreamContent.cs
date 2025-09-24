using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class ProgressStreamContent : HttpContent
{
    private readonly Stream _content;
    private readonly int _bufferSize;
    private readonly Action<long, long> _progress;

    public ProgressStreamContent(Stream content, int bufferSize, Action<long, long> progress)
    {
        _content = content;
        _bufferSize = bufferSize;
        _progress = progress;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
    {
        var buffer = new byte[_bufferSize];
        long size = _content.Length;
        long uploaded = 0;

        int read;
        while ((read = await _content.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await stream.WriteAsync(buffer, 0, read);
            uploaded += read;
            _progress?.Invoke(uploaded, size);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _content.Length;
        return true;
    }
}
