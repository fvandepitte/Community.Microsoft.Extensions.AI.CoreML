using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace Community.Microsoft.Extensions.AI.CoreML.Interop;

internal sealed class NativeAppleIntelligenceBridge : IAppleIntelligenceBridge
{
    public bool IsAvailable() => NativeMethods.IsAvailable();

    public string Complete(string messagesJson)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("Apple Intelligence on-device models are not available on this device.");
        }

        string? result = null;
        Exception? error = null;

        CompletionCallback callback = (resultUtf8, errorUtf8) =>
        {
            if (errorUtf8 != 0)
            {
                error = new InvalidOperationException(Marshal.PtrToStringUTF8(errorUtf8) ?? "Bridge returned an error.");
                return;
            }

            if (resultUtf8 == 0)
            {
                error = new InvalidOperationException("Bridge returned no result.");
                return;
            }

            result = Marshal.PtrToStringUTF8(resultUtf8);
        };

        NativeMethods.Complete(messagesJson, callback);
        GC.KeepAlive(callback);

        if (error is not null)
        {
            throw error;
        }

        return result ?? string.Empty;
    }

    public IAsyncEnumerable<string> CompleteStreaming(string messagesJson)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("Apple Intelligence on-device models are not available on this device.");
        }

        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        StreamingCompletionCallback callback = (chunkUtf8, isDone, errorUtf8) =>
        {
            if (errorUtf8 != 0)
            {
                channel.Writer.TryComplete(new InvalidOperationException(Marshal.PtrToStringUTF8(errorUtf8) ?? "Bridge returned an error."));
                return;
            }

            if (chunkUtf8 != 0)
            {
                channel.Writer.TryWrite(Marshal.PtrToStringUTF8(chunkUtf8) ?? string.Empty);
            }

            if (isDone)
            {
                channel.Writer.TryComplete();
            }
        };

        _ = Task.Run(() =>
        {
            try
            {
                NativeMethods.CompleteStreaming(messagesJson, callback);
                GC.KeepAlive(callback);
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        });

        return channel.Reader.ReadAllAsync();
    }
}
