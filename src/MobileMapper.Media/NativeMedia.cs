using System.Runtime.InteropServices;
namespace MobileMapper.Media;
[StructLayout(LayoutKind.Sequential)]
public struct MediaStats
{
    public ulong Decoded, Presented, Replaced, Generation;
    public int Width, Height, Error;
}
/// <summary>Owns the native decoder/presenter. Dispose only after decode workers have stopped.</summary>
public sealed class NativeMedia : IDisposable
{
    private nint handle;
    public NativeMedia(bool warp = false)
    {
        if (Api.mm_abi_version() != 1) throw new InvalidOperationException("Unsupported media DLL ABI.");
        Check(Api.mm_create(out handle, warp ? 1 : 0));
    }
    public nint GetSwapChain() { Check(Api.mm_get_swapchain(handle, out var value)); return value; }
    public void Resize(int w, int h, float sx, float sy) => Check(Api.mm_resize(handle, w, h, sx, sy));
    public void Reset(long generation) => Check(Api.mm_reset(handle, checked((ulong)generation)));
    public unsafe void Decode(ReadOnlySpan<byte> packet, long pts, bool config)
    {
        fixed (byte* p = packet) Check(Api.mm_decode(handle, p, packet.Length, pts, config ? 1 : 0));
    }
    public MediaStats Stats { get { Check(Api.mm_get_stats(handle, out var stats)); return stats; } }
    public void Dispose() { if (handle != 0) { Api.mm_destroy(handle); handle = 0; } }
    private static void Check(int result)
    {
        if (result != 0) throw new InvalidOperationException($"Native media operation failed ({result}). Restart the session; verify the graphics driver and DLL dependencies.");
    }
    private static class Api
    {
        private const string Dll = "MobileMapperMedia.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern uint mm_abi_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mm_create(out nint handle, int warp);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mm_get_swapchain(nint handle, out nint chain);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mm_resize(nint handle, int w, int h, float sx, float sy);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mm_reset(nint handle, ulong generation);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern unsafe int mm_decode(nint handle, byte* packet, int length, long pts, int config);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int mm_get_stats(nint handle, out MediaStats stats);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void mm_destroy(nint handle);
    }
}
