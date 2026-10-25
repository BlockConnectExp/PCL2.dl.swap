using System;

namespace PclDlSwap.Payload.Hook
{
    /// <summary>
    /// Reinterprets raw pointer values as managed references (and back) inside detour thunks.
    ///
    /// Managed hooks must never declare reference-typed parameters. A reverse P/Invoke stub would
    /// marshall them (string to LPWStr, object to IUnknown, ...) instead of handing over the object
    /// reference the JIT call site put in the register. Declaring every reference parameter as
    /// <see cref="IntPtr"/> keeps the thunk byte-for-byte transparent; this helper turns that raw
    /// value back into a GC-tracked reference while the hook runs.
    /// </summary>
    public static unsafe class ObjectPointer
    {
        public static object ToObject(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return null;
            return *(object*)&pointer;
        }

        public static T ToObject<T>(IntPtr pointer) where T : class
        {
            return (T)ToObject(pointer);
        }

        public static IntPtr ToPointer(object value)
        {
            if (value == null) return IntPtr.Zero;
            return *(IntPtr*)&value;
        }
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
