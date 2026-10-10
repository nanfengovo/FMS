using System.Threading.Channels;
using Fms.Domain.Logging;

namespace Fms.Application.Logging;

public sealed class LogSink : ILogSink
{
    private readonly Channel<LogWrite> _channel = Channel.CreateBounded<LogWrite>(new BoundedChannelOptions(4000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true
    });

    public void Enqueue(LogWrite write) => _channel.Writer.TryWrite(write);

    public ChannelReader<LogWrite> Reader => _channel.Reader;
}
