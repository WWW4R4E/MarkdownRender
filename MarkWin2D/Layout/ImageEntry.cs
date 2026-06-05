namespace MarkWin2D.Layout;

internal readonly record struct ImageInfo(string Url, string Alt);

internal readonly record struct ImageEntry(string Url, string Alt, float X, float Y, float W, float H);
