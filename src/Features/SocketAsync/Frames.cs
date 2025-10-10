using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.SocketAsync;

/// <summary>Per-send work item for the channel.</summary>
internal sealed class Frames
{
    public List<ArraySegment<byte>> Segments = new();
    public TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public byte[]? RentedHeader; // returned after send
}
