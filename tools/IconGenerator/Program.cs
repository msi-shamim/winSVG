// IconGenerator — renders an SVG at multiple sizes and packs the results into
// a single Windows .ico file (PNG-compressed entries, supported since Vista).
//
// Usage: IconGenerator <input.svg> <output.ico> [preview.png]
//   input.svg    Source vector logo.
//   output.ico   Multi-size icon written here.
//   preview.png  Optional 256px PNG preview for visual inspection.
using SkiaSharp;
using Svg.Skia;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: IconGenerator <input.svg> <output.ico> [preview.png]");
    return 1;
}

string inputSvgPath = args[0];
string outputIcoPath = args[1];
string? previewPngPath = args.Length > 2 ? args[2] : null;

// Standard Windows icon sizes: shell list views, dialogs, taskbar, jumbo.
int[] iconEdgeLengths = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

using var svgDocument = new SKSvg();
SKPicture? parsedPicture = svgDocument.Load(inputSvgPath);
if (parsedPicture is null)
{
    Console.Error.WriteLine($"Could not parse SVG: {inputSvgPath}");
    return 1;
}

List<byte[]> pngEncodedImages = new();
foreach (int edgeLength in iconEdgeLengths)
{
    pngEncodedImages.Add(RenderSvgToPngBytes(parsedPicture, edgeLength));
}

WriteIconFile(outputIcoPath, iconEdgeLengths, pngEncodedImages);
Console.WriteLine($"Wrote {outputIcoPath} with sizes: {string.Join(", ", iconEdgeLengths)}");

if (previewPngPath is not null)
{
    File.WriteAllBytes(previewPngPath, pngEncodedImages[^1]);
    Console.WriteLine($"Wrote preview: {previewPngPath}");
}

return 0;

/// <summary>
/// Rasterizes the SVG picture into a square PNG of the given edge length,
/// scaled to fit and centered, with a transparent background.
/// </summary>
static byte[] RenderSvgToPngBytes(SKPicture picture, int edgeLength)
{
    SKRect drawingBounds = picture.CullRect;
    float scaleToFit = edgeLength / Math.Max(drawingBounds.Width, drawingBounds.Height);
    float scaledWidth = drawingBounds.Width * scaleToFit;
    float scaledHeight = drawingBounds.Height * scaleToFit;

    var imageInfo = new SKImageInfo(edgeLength, edgeLength, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var renderSurface = SKSurface.Create(imageInfo);
    SKCanvas renderCanvas = renderSurface.Canvas;

    renderCanvas.Clear(SKColors.Transparent);
    renderCanvas.Translate((edgeLength - scaledWidth) / 2f, (edgeLength - scaledHeight) / 2f);
    renderCanvas.Scale(scaleToFit);
    renderCanvas.Translate(-drawingBounds.Left, -drawingBounds.Top);
    renderCanvas.DrawPicture(picture);
    renderCanvas.Flush();

    using SKImage renderedImage = renderSurface.Snapshot();
    using SKData encodedPng = renderedImage.Encode(SKEncodedImageFormat.Png, 100);
    return encodedPng.ToArray();
}

/// <summary>
/// Writes a Windows ICO file containing one PNG-compressed entry per size.
/// Layout: ICONDIR header, then one ICONDIRENTRY per image, then image data.
/// </summary>
static void WriteIconFile(string outputPath, int[] edgeLengths, List<byte[]> pngImages)
{
    const int IconDirHeaderSize = 6;
    const int IconDirEntrySize = 16;

    using var iconFileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
    using var iconWriter = new BinaryWriter(iconFileStream);

    // ICONDIR: reserved (0), resource type (1 = icon), image count.
    iconWriter.Write((ushort)0);
    iconWriter.Write((ushort)1);
    iconWriter.Write((ushort)edgeLengths.Length);

    int nextImageDataOffset = IconDirHeaderSize + IconDirEntrySize * edgeLengths.Length;

    for (int imageIndex = 0; imageIndex < edgeLengths.Length; imageIndex++)
    {
        int edgeLength = edgeLengths[imageIndex];
        byte[] pngData = pngImages[imageIndex];

        // ICONDIRENTRY: width/height bytes use 0 to mean 256.
        iconWriter.Write((byte)(edgeLength >= 256 ? 0 : edgeLength));
        iconWriter.Write((byte)(edgeLength >= 256 ? 0 : edgeLength));
        iconWriter.Write((byte)0);            // color palette count (none)
        iconWriter.Write((byte)0);            // reserved
        iconWriter.Write((ushort)1);          // color planes
        iconWriter.Write((ushort)32);         // bits per pixel
        iconWriter.Write(pngData.Length);     // image data size
        iconWriter.Write(nextImageDataOffset); // image data offset

        nextImageDataOffset += pngData.Length;
    }

    foreach (byte[] pngData in pngImages)
    {
        iconWriter.Write(pngData);
    }
}
