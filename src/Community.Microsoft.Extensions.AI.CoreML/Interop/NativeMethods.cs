using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Community.Microsoft.Extensions.AI.CoreML.Interop;

/// <summary>
/// Native callback for non-streaming completion.
/// </summary>
/// <param name="resultUtf8">UTF-8 pointer for the completion result, or <c>0</c> on failure.</param>
/// <param name="errorUtf8">UTF-8 pointer for the error message, or <c>0</c> on success.</param>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void CompletionCallback(nint resultUtf8, nint errorUtf8);

/// <summary>
/// Native callback for streaming completion updates.
/// </summary>
/// <param name="chunkUtf8">UTF-8 pointer for a streamed chunk, or <c>0</c> when complete/error.</param>
/// <param name="isDone">True on the terminal callback invocation.</param>
/// <param name="errorUtf8">UTF-8 pointer for the terminal error message, or <c>0</c> on success.</param>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void StreamingCompletionCallback(
    nint chunkUtf8,
    [MarshalAs(UnmanagedType.I1)] bool isDone,
    nint errorUtf8);

/// <summary>
/// Native method declarations for the Swift AppleIntelligence bridge.
/// </summary>
internal static partial class NativeMethods
{
    private const string LibraryName = "AppleIntelligenceBridge";

    [LibraryImport(LibraryName, EntryPoint = "aib_is_available")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool IsAvailable();

    [LibraryImport(LibraryName, EntryPoint = "aib_complete", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Complete(string messagesJson, CompletionCallback callback);

    [LibraryImport(LibraryName, EntryPoint = "aib_complete_streaming", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CompleteStreaming(string messagesJson, StreamingCompletionCallback callback);
}
