using Win32;
using static Win32.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public static unsafe class COMUtils
{
    public static void COMRelease<T>(T** ptrPtr) where T : unmanaged, IUnknown.Interface
    {
        if (*ptrPtr != null)
        {
            (*ptrPtr)->Release();
            *ptrPtr = null;
        }
    }

    public static void COMRelease<T>(ref T* ptrRef) where T : unmanaged, IUnknown.Interface
    {
        if (ptrRef != null)
        {
            ptrRef->Release();
            ptrRef = null;
        }
    }

    public static TTo* COMCast<TFrom, TTo>(TFrom* from) where TFrom : unmanaged, IUnknown.Interface
                                                        where TTo : unmanaged, INativeGuid
    {
        TTo* Result;
        ThrowIfFailed(from->QueryInterface(__uuidof<TTo>(), (void**)&Result));
        return Result;
    }

    public static TTo* COMCastAndReleaseOld<TFrom, TTo>(TFrom* from) where TFrom : unmanaged, IUnknown.Interface
                                                                     where TTo : unmanaged, INativeGuid
    {
        TTo* Result;
        ThrowIfFailed(from->QueryInterface(__uuidof<TTo>(), (void**)&Result));
        COMRelease(&from);
        return Result;
    }

    public static bool TryCOMCast<TFrom, TTo>(TFrom* from, out TTo* to) where TFrom : unmanaged, IUnknown.Interface
                                                                        where TTo : unmanaged, INativeGuid
    {
        TTo* Result;
        HResult ErrorCode = from->QueryInterface(__uuidof<TTo>(), (void**)&Result);
        to = Result;
        return ErrorCode.Success;
    }
}
