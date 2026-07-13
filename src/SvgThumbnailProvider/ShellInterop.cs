using System.Runtime.InteropServices;
using ComIStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace SvgThumbnailProvider;

/// <summary>
/// Alpha channel type reported back to the Windows Shell for a generated thumbnail.
/// Mirrors the native WTS_ALPHATYPE enumeration from thumbcache.h.
/// </summary>
public enum WTS_ALPHATYPE
{
    /// <summary>Alpha type is unknown to the provider.</summary>
    WTSAT_UNKNOWN = 0,

    /// <summary>The bitmap is an opaque 24-bit style image (no alpha).</summary>
    WTSAT_RGB = 1,

    /// <summary>The bitmap is a 32-bit image with a valid alpha channel.</summary>
    WTSAT_ARGB = 2,
}

/// <summary>
/// COM interface the Windows Shell calls to request a thumbnail image.
/// The GUID is fixed by Windows (thumbcache.h) and must not be changed.
/// </summary>
[ComImport]
[Guid("e357fccd-a995-4576-b01f-234630154e96")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IThumbnailProvider
{
    /// <summary>
    /// Asks the provider to render a thumbnail whose longest edge is
    /// <paramref name="requestedSquareEdgeLength"/> pixels.
    /// </summary>
    /// <param name="requestedSquareEdgeLength">Maximum width and height, in pixels, of the requested thumbnail.</param>
    /// <param name="thumbnailBitmapHandle">Receives a GDI HBITMAP owned by the caller after return.</param>
    /// <param name="thumbnailAlphaType">Receives how the alpha channel of the bitmap should be interpreted.</param>
    void GetThumbnail(uint requestedSquareEdgeLength, out IntPtr thumbnailBitmapHandle, out WTS_ALPHATYPE thumbnailAlphaType);
}

/// <summary>
/// COM interface the Windows Shell uses to hand the provider the file content
/// as a stream (Explorer never gives thumbnail handlers a file path directly).
/// The GUID is fixed by Windows (propsys.h) and must not be changed.
/// </summary>
[ComImport]
[Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithStream
{
    /// <summary>
    /// Supplies the raw file content stream and the storage access mode (STGM flags).
    /// </summary>
    /// <param name="fileContentStream">COM stream positioned at the start of the file content.</param>
    /// <param name="storageAccessMode">STGM access mode flags requested by the Shell.</param>
    void Initialize(ComIStream fileContentStream, uint storageAccessMode);
}

/// <summary>
/// Minimal GDI32 / kernel32 platform invoke declarations needed to convert a
/// Skia-rendered pixel buffer into the HBITMAP that the Shell expects.
/// </summary>
internal static class NativeGdiMethods
{
    /// <summary>Uncompressed RGB bitmap compression constant (BI_RGB).</summary>
    internal const uint BI_RGB = 0;

    /// <summary>Color table contains literal RGB values (DIB_RGB_COLORS).</summary>
    internal const uint DIB_RGB_COLORS = 0;

    /// <summary>
    /// Bitmap header describing the device-independent bitmap layout requested
    /// from CreateDIBSection. Mirrors the native BITMAPINFOHEADER structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    /// <summary>
    /// Creates a writable device-independent bitmap section and returns both the
    /// HBITMAP handle and a pointer to its pixel bits.
    /// </summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr CreateDIBSection(
        IntPtr deviceContextHandle,
        in BITMAPINFOHEADER bitmapInfo,
        uint colorUsage,
        out IntPtr bitmapPixelBits,
        IntPtr fileMappingHandle,
        uint fileMappingOffset);

    /// <summary>Releases a GDI object handle (used only on failure paths here).</summary>
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr gdiObjectHandle);

    /// <summary>Copies a block of unmanaged memory from source to destination.</summary>
    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    internal static extern void CopyUnmanagedMemory(IntPtr destinationPointer, IntPtr sourcePointer, UIntPtr byteCount);
}
