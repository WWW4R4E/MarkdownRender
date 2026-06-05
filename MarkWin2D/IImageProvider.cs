#nullable enable
using Microsoft.Graphics.Canvas;

namespace MarkWin2D;

public interface IImageProvider
{
    CanvasBitmap? GetImage(string url, CanvasDevice device);
    event EventHandler? ImagesInvalidated;
}