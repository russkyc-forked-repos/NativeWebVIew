using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NativeWebView.Interop;

internal static class MacOSMainThreadDispatch
{
    private static readonly DispatchCallback Callback = Invoke;
    private static readonly Lazy<IntPtr> MainQueue = new(() =>
        NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/system/libdispatch.dylib"), "_dispatch_main_q"));

    internal static void Post(Action action)
    {
        var owner = GCHandle.Alloc(action);
        try { dispatch_async_f(MainQueue.Value, GCHandle.ToIntPtr(owner), Callback); }
        catch { owner.Free(); throw; }
    }

    private static void Invoke(IntPtr state)
    {
        var owner = GCHandle.FromIntPtr(state);
        try { ((Action)owner.Target!)(); }
        catch (Exception ex) { Trace.TraceError("NativeWebView AppKit cleanup failed: {0}", ex.GetType().Name); }
        finally { owner.Free(); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DispatchCallback(IntPtr state);
    [DllImport("/usr/lib/system/libdispatch.dylib")] private static extern void dispatch_async_f(IntPtr queue, IntPtr state, DispatchCallback callback);
}
