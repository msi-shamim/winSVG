using System.IO.Compression;
using System.Runtime.InteropServices;
using SkiaSharp;
using Svg.Skia;
using ComIStream = System.Runtime.InteropServices.ComTypes.IStream;
using ComSTATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace SvgThumbnailProvider;

/// <summary>
/// Shell thumbnail provider that rasterizes SVG (and gzip-compressed SVGZ)
/// files so Windows Explorer can show real image thumbnails in folders.
/// Explorer instantiates this class through COM, feeds it the file content via
/// <see cref="IInitializeWithStream"/>, then requests a bitmap via
/// <see cref="IThumbnailProvider"/>.
/// </summary>
[ComVisible(true)]
[Guid("D8E9B2C4-8F5A-4E1B-9C3D-7A6F2B1E0D5C")]
[ClassInterface(ClassInterfaceType.None)]
[ProgId("SvgPreview.ThumbnailProvider")]
public sealed class SvgThumbnailHandler : IThumbnailProvider, IInitializeWithStream
{
    /// <summary>Raw bytes of the SVG document handed over by the Shell.</summary>
    private byte[]? _svgDocumentBytes;

    /// <inheritdoc />
    public void Initialize(ComIStream fileContentStream, uint storageAccessMode)
    {
        _svgDocumentBytes = ReadAllBytesFromComStream(fileContentStream);
    }

    /// <inheritdoc />
    public void GetThumbnail(uint requestedSquareEdgeLength, out IntPtr thumbnailBitmapHandle, out WTS_ALPHATYPE thumbnailAlphaType)
    {
        if (_svgDocumentBytes is null || _svgDocumentBytes.Length == 0)
        {
            throw new InvalidOperationException("Thumbnail handler was not initialized with file content.");
        }

        using SKBitmap renderedBitmap = RenderSvgToBitmap(_svgDocumentBytes, (int)requestedSquareEdgeLength);

        thumbnailBitmapHandle = ConvertSkiaBitmapToHBitmap(renderedBitmap);
        thumbnailAlphaType = WTS_ALPHATYPE.WTSAT_ARGB;
    }

    /// <summary>
    /// Drains a COM stream into a managed byte array. Explorer supplies file
    /// content this way instead of exposing a file path.
    /// </summary>
    /// <param name="comStream">Source COM stream positioned at the file start.</param>
    /// <returns>The complete file content as a byte array.</returns>
    private static byte[] ReadAllBytesFromComStream(ComIStream comStream)
    {
        comStream.Stat(out ComSTATSTG streamStatistics, 1 /* STATFLAG_NONAME */);
        long expectedByteCount = streamStatistics.cbSize;

        using var collectedBytes = new MemoryStream(expectedByteCount > 0 ? (int)Math.Min(expectedByteCount, int.MaxValue) : 4096);

        byte[] transferBuffer = new byte[81920];
        IntPtr bytesReadPointer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            while (true)
            {
                comStream.Read(transferBuffer, transferBuffer.Length, bytesReadPointer);
                int bytesReadThisCall = Marshal.ReadInt32(bytesReadPointer);
                if (bytesReadThisCall <= 0)
                {
                    break;
                }

                collectedBytes.Write(transferBuffer, 0, bytesReadThisCall);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(bytesReadPointer);
        }

        return collectedBytes.ToArray();
    }

    /// <summary>
    /// Parses the SVG document (transparently un-gzipping SVGZ content) and
    /// rasterizes it into a 32-bit premultiplied BGRA bitmap that fits inside a
    /// square of the requested edge length while preserving aspect ratio.
    /// </summary>
    /// <param name="svgDocumentBytes">Raw SVG or gzip-compressed SVGZ bytes.</param>
    /// <param name="maximumEdgeLength">Longest allowed edge of the output bitmap, in pixels.</param>
    /// <returns>The rendered bitmap; the caller owns and must dispose it.</returns>
    private static SKBitmap RenderSvgToBitmap(byte[] svgDocumentBytes, int maximumEdgeLength)
    {
        using Stream svgContentStream = OpenPossiblyGzippedStream(svgDocumentBytes);

        using var svgDocument = new SKSvg();
        SKPicture? parsedPicture = svgDocument.Load(svgContentStream);
        if (parsedPicture is null)
        {
            throw new InvalidDataException("The SVG document could not be parsed.");
        }

        SKRect drawingBounds = parsedPicture.CullRect;
        if (drawingBounds.Width <= 0 || drawingBounds.Height <= 0)
        {
            throw new InvalidDataException("The SVG document has no drawable area.");
        }

        float scaleToFitRequestedSquare = maximumEdgeLength / Math.Max(drawingBounds.Width, drawingBounds.Height);
        int outputPixelWidth = Math.Max(1, (int)MathF.Round(drawingBounds.Width * scaleToFitRequestedSquare));
        int outputPixelHeight = Math.Max(1, (int)MathF.Round(drawingBounds.Height * scaleToFitRequestedSquare));

        var outputImageInfo = new SKImageInfo(outputPixelWidth, outputPixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        var outputBitmap = new SKBitmap(outputImageInfo);

        using (var drawingCanvas = new SKCanvas(outputBitmap))
        {
            drawingCanvas.Clear(SKColors.Transparent);
            drawingCanvas.Scale(scaleToFitRequestedSquare);
            drawingCanvas.Translate(-drawingBounds.Left, -drawingBounds.Top);
            drawingCanvas.DrawPicture(parsedPicture);
            drawingCanvas.Flush();
        }

        return outputBitmap;
    }

    /// <summary>
    /// Wraps the document bytes in a readable stream, detecting the gzip magic
    /// number so .svgz files are decompressed transparently.
    /// </summary>
    /// <param name="documentBytes">Raw file bytes as received from the Shell.</param>
    /// <returns>A stream yielding plain SVG markup.</returns>
    private static Stream OpenPossiblyGzippedStream(byte[] documentBytes)
    {
        bool looksGzipCompressed = documentBytes.Length > 2 && documentBytes[0] == 0x1F && documentBytes[1] == 0x8B;
        var rawMemoryStream = new MemoryStream(documentBytes, writable: false);
        return looksGzipCompressed
            ? new GZipStream(rawMemoryStream, CompressionMode.Decompress)
            : rawMemoryStream;
    }

    /// <summary>
    /// Copies a premultiplied BGRA Skia bitmap into a top-down 32-bit GDI DIB
    /// section, which is the HBITMAP format the Shell thumbnail cache expects.
    /// </summary>
    /// <param name="sourceBitmap">Rendered bitmap in Bgra8888/premultiplied format.</param>
    /// <returns>An HBITMAP handle whose ownership passes to the Shell.</returns>
    private static IntPtr ConvertSkiaBitmapToHBitmap(SKBitmap sourceBitmap)
    {
        var bitmapHeader = new NativeGdiMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<NativeGdiMethods.BITMAPINFOHEADER>(),
            biWidth = sourceBitmap.Width,
            // Negative height requests a top-down DIB, matching Skia's row order.
            biHeight = -sourceBitmap.Height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = NativeGdiMethods.BI_RGB,
        };

        IntPtr bitmapHandle = NativeGdiMethods.CreateDIBSection(
            IntPtr.Zero,
            in bitmapHeader,
            NativeGdiMethods.DIB_RGB_COLORS,
            out IntPtr destinationPixelBits,
            IntPtr.Zero,
            0);

        if (bitmapHandle == IntPtr.Zero || destinationPixelBits == IntPtr.Zero)
        {
            throw new OutOfMemoryException("CreateDIBSection failed to allocate the thumbnail bitmap.");
        }

        try
        {
            ulong pixelByteCount = (ulong)sourceBitmap.Width * (ulong)sourceBitmap.Height * 4UL;
            NativeGdiMethods.CopyUnmanagedMemory(destinationPixelBits, sourceBitmap.GetPixels(), (UIntPtr)pixelByteCount);
            return bitmapHandle;
        }
        catch
        {
            NativeGdiMethods.DeleteObject(bitmapHandle);
            throw;
        }
    }
}
