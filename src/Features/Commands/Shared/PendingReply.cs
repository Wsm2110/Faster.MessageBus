using Faster.MessageBus.Shared;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace Faster.MessageBus.Features.Commands.Shared;

/// <summary>
/// Represents a pending reply for a request-response operation, implemented as a reusable and low-allocation
/// <see cref="IValueTaskSource{TResult}"/>. This class is designed to back a <see cref="ValueTask{TResult}"/>,
/// avoiding the need to allocate a <see cref="Task{TResult}"/> object for each asynchronous operation.
/// It is typically used in object pools to manage high-throughput scenarios efficiently.
/// </summary>
/// <typeparam name="T">The type of the result for the pending operation.</typeparam>
public sealed class PendingReply<T> : IValueTaskSource<T>
{
    private ManualResetValueTaskSourceCore<T> _core;
    private static long _correlationIdCounter = 0;

    /// <summary>
    /// A unique identifier used to correlate a request with its corresponding reply message.
    /// </summary>
    public long CorrelationId { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PendingReply{T}"/> class.
    /// </summary>
    /// <param name="runContinuationsAsync">
    /// A value indicating whether to force continuations to run asynchronously. 
    /// Setting this to <c>false</c> can improve performance by allowing synchronous completion,
    /// but may lead to stack-diving if not handled carefully. Defaults to <c>false</c>.
    /// </param>
    public PendingReply(bool runContinuationsAsync = false)
    {
        // WyHash64 is not a standard part of .NET, assuming it's a custom or third-party implementation
        // for generating a fast, non-cryptographic hash.
        CorrelationId = Interlocked.Increment(ref _correlationIdCounter);
        _core = default;
        _core.RunContinuationsAsynchronously = runContinuationsAsync;
    }

    /// <summary>
    /// Creates a <see cref="ValueTask{TResult}"/> that represents this pending operation.
    /// This method allows consumers to await the completion of the reply without extra allocations.
    /// </summary>
    /// <returns>A <see cref="ValueTask{TResult}"/> linked to this source.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<T> AsValueTask() => new ValueTask<T>(this, _core.Version);

    /// <summary>
    /// Transitions the underlying <see cref="ValueTask{TResult}"/> to the <see cref="TaskStatus.RanToCompletion"/> state.
    /// </summary>
    /// <param name="result">The result value to complete the operation with.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult(T result) => _core.SetResult(result);

    /// <summary>
    /// Transitions the underlying <see cref="ValueTask{TResult}"/> to the <see cref="TaskStatus.Faulted"/> state.
    /// </summary>
    /// <param name="error">The exception to complete the operation with.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception error) => _core.SetException(error);

    /// <summary>
    /// Resets the state of the object, making it reusable for another operation.
    /// This is crucial when pooling <see cref="PendingReply{T}"/> instances.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => _core.Reset();

    /// <summary>
    /// Gets the result of the operation. This is called by the awaiter of the <see cref="ValueTask{TResult}"/>.
    /// </summary>
    /// <param name="token">The unique token identifying the operation version.</param>
    /// <returns>The result of the completed operation.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetResult(short token)
    {
        return _core.GetResult(token);
    }

    /// <summary>
    /// Gets the status of the operation.
    /// </summary>
    /// <param name="token">The unique token identifying the operation version.</param>
    /// <returns>The current status of the operation (e.g., Pending, Succeeded, Faulted).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    /// <summary>
    /// Gets whether the pending reply has already been completed (successfully or faulted).
    /// </summary>
    public bool IsCompleted
    {
        get
        {
            // ValueTaskSourceStatus.Pending means not completed; anything else is completed
            return _core.GetStatus(_core.Version) != ValueTaskSourceStatus.Pending;
        }
    }

    public MeshContext Target { get; internal set; }

    /// <summary>
    /// Schedules the continuation action that will be invoked when the operation completes.
    /// </summary>
    /// <param name="continuation">The action to invoke when the operation completes.</param>
    /// <param name="state">The state object to pass to the continuation.</param>
    /// <param name="token">The unique token identifying the operation version.</param>
    /// <param name="flags">Flags describing the behavior of the continuation.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}