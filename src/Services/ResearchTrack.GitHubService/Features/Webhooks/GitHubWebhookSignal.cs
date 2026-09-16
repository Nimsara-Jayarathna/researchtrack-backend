using System.Threading.Channels;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public sealed class GitHubWebhookSignal
{
    private readonly Channel<byte> _channel = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public void Pulse() => _channel.Writer.TryWrite(1);

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _channel.Reader.ReadAsync(cancellationToken);
        while (_channel.Reader.TryRead(out _)) { }
    }
}
