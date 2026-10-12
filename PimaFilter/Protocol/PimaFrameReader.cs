namespace PimaFilter.Protocol;

public sealed class PimaFrameReader
{
    private readonly List<byte> _pending = [];
    private readonly byte[] _buffer = new byte[512];

    public async Task<byte[]?> ReadFrameAsync(
        Stream stream,
        int maximumFrameBytes,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var endIndex = _pending.IndexOf(0x0D);
            if (endIndex >= 0)
            {
                var frame = _pending.GetRange(0, endIndex + 1).ToArray();
                _pending.RemoveRange(0, endIndex + 1);
                return frame;
            }

            if (_pending.Count >= maximumFrameBytes)
            {
                throw new InvalidDataException(
                    $"Mesajul depaseste limita de {maximumFrameBytes} bytes fara terminator CR.");
            }

            var read = await stream.ReadAsync(_buffer, cancellationToken);
            if (read == 0)
            {
                if (_pending.Count == 0)
                {
                    return null;
                }

                var incomplete = _pending.ToArray();
                _pending.Clear();
                return incomplete;
            }

            _pending.AddRange(_buffer.AsSpan(0, read).ToArray());
            if (_pending.Count > maximumFrameBytes)
            {
                throw new InvalidDataException(
                    $"Mesajul depaseste limita de {maximumFrameBytes} bytes.");
            }
        }
    }
}
