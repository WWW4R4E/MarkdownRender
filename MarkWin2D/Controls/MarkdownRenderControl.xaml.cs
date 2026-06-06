#nullable enable
using System.Diagnostics;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkWin2D.Layout;
using MarkWin2D.Styles;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace MarkWin2D.Controls;

public sealed partial class MarkWin2DControl : UserControl
{
    private MarkdownDocument? _document;
    private readonly List<LayoutEntry> _layout = new();
    private readonly List<ImageEntry> _imageEntries = new();
    private bool _layoutDirty = true;
    private float _lastWidth;
    private float _totalHeight;
    private float _maxContentWidth;
    private bool _isInternalResize;
    private readonly MarkdownPipeline _pipeline;
    private CanvasTextFormat? _bf, _cf;
    private CanvasDevice? _device;
    private CanvasDevice Device => _device ??= CanvasDevice.GetSharedDevice();
    private readonly Dictionary<Table, TableLayoutInfo> _tableCache = new();
    private IImageProvider? _imageProvider;
    private readonly List<LinkHitTarget> _linkTargets = new();
    private float _maxTextureHeight = 16384;

    public bool AreLinksEnabled { get; set; } = true;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarkWin2DControl),
            new PropertyMetadata(string.Empty, (d, e) => ((MarkWin2DControl)d).OnTextChanged((string)e.OldValue, (string)e.NewValue)));

    public static readonly DependencyProperty DocumentStyleProperty =
        DependencyProperty.Register(nameof(DocumentStyle), typeof(MarkdownStyle), typeof(MarkWin2DControl),
            new PropertyMetadata(new MarkdownStyle(), (d, e) => ((MarkWin2DControl)d).OnStyleChanged()));

    public MarkWin2DControl()
    {
        InitializeComponent();
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
        MarkdownCanvas.RegionsInvalidated += OnRegionsInvalidated;
        MarkdownCanvas.SizeChanged += OnCanvasSizeChanged;
        MarkdownCanvas.PointerPressed += OnCanvasPointerPressed;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public MarkdownStyle DocumentStyle
    {
        get => (MarkdownStyle)GetValue(DocumentStyleProperty);
        set => SetValue(DocumentStyleProperty, value);
    }

    public IImageProvider? ImageProvider
    {
        get => _imageProvider;
        set
        {
            if (_imageProvider != null) _imageProvider.ImagesInvalidated -= OnImagesInvalidated;
            _imageProvider = value;
            if (_imageProvider != null) _imageProvider.ImagesInvalidated += OnImagesInvalidated;
        }
    }

    private void OnImagesInvalidated(object? sender, EventArgs e)
    {
        float vw = _lastWidth > 0 ? _lastWidth : (float)ScrollViewer.ViewportWidth;
        if (vw > 0) ComputeLayout(vw);
        MarkdownCanvas.Invalidate();
    }

    private void OnTextChanged(string oldVal, string newVal)
    {
        if (oldVal != newVal)
        {
            _document = Markdown.Parse(newVal ?? string.Empty, _pipeline);
            ComputeLayout((float)MarkdownCanvas.ActualWidth);
            MarkdownCanvas.Invalidate();
        }
    }

    private void OnStyleChanged()
    {
        FreeFormats();
        ComputeLayout((float)MarkdownCanvas.ActualWidth);
        MarkdownCanvas.Invalidate();
    }

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        if (_layoutDirty && sender.ActualWidth > 0)
            ComputeLayout((float)sender.ActualWidth);
        if (_document == null) return;

        foreach (var t in _linkTargets) t.Layout.Dispose();
        _linkTargets.Clear();

        foreach (var region in args.InvalidatedRegions)
        {
            using var ds = sender.CreateDrawingSession(region);
            float rY = (float)region.Y, rH = (float)region.Height;
            float rB = rY + rH;

            foreach (var e in _layout)
            {
                if (e.Y + e.H <= rY || e.Y >= rB) continue;
                Draw(ds, e);
            }
            foreach (var img in _imageEntries)
            {
                if (img.Y + img.H <= rY || img.Y >= rB) continue;
                DrawImage(ds, img);
            }
        }
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isInternalResize) return;
        float w = (float)e.NewSize.Width;
        if (_document != null && Math.Abs(w - _lastWidth) > 0.5f)
            ComputeLayout(w);
    }

    void ComputeLayout(float w)
    {
        if (_document == null || w <= 0) { _layoutDirty = true; return; }
        EnsureFormats();
        _layout.Clear();
        _imageEntries.Clear();
        _tableCache.Clear();
        _maxContentWidth = w;
        _totalHeight = DocumentStyle.DocumentMargin;
        float cw = w - DocumentStyle.DocumentMargin * 2;
        if (cw < 0) cw = w;
        foreach (var b in _document) Layout(b, DocumentStyle.DocumentMargin, cw, 0);
        _totalHeight += DocumentStyle.DocumentMargin;
        _lastWidth = w;
        _layoutDirty = false;

        _isInternalResize = true;
        bool sizeChanged = false;
        float finalW = Math.Max(w, _maxContentWidth);
        if (Math.Abs(MarkdownCanvas.Width - finalW) > 0.5f)
        {
            MarkdownCanvas.Width = finalW;
            sizeChanged = true;
        }
        if (!float.IsNaN(_totalHeight) &&
            (float.IsNaN((float)MarkdownCanvas.Height) ||
             Math.Abs(MarkdownCanvas.Height - _totalHeight) > 1))
        {
            MarkdownCanvas.Height = _totalHeight;
            sizeChanged = true;
        }
        _isInternalResize = false;
        if (sizeChanged)
            ScrollViewer.InvalidateMeasure();

        if (MarkdownCanvas.ReadyToDraw)
            MarkdownCanvas.Invalidate();
    }

    void EnsureFormats()
    {
        _bf ??= new CanvasTextFormat
        {
            FontFamily = DocumentStyle.FontFamily,
            FontSize = DocumentStyle.BodyFontSize,
            WordWrapping = CanvasWordWrapping.Wrap,
            LineSpacingMode = CanvasLineSpacingMode.Proportional,
            LineSpacing = 1.4f
        };
        _cf ??= new CanvasTextFormat
        {
            FontFamily = DocumentStyle.CodeFontFamily,
            FontSize = DocumentStyle.CodeFontSize,
            WordWrapping = CanvasWordWrapping.NoWrap
        };
        // try { _maxTextureHeight = Device.MaximumBitmapSizeInPixels; } catch { }
    }

    void FreeFormats()
    {
        _bf?.Dispose(); _bf = null;
        _cf?.Dispose(); _cf = null;
    }

    void Layout(Block b, float x, float w, int indent)
    {
        switch (b)
        {
            case HeadingBlock h: LayHeading(h, x, w); break;
            case ParagraphBlock p: LayInlines(p.Inline, x, w); break;
            case FencedCodeBlock fc: LayCode(fc, x, w); break;
            case CodeBlock ic: LayCode(ic, x, w); break;
            case ListBlock l: LayList(l, x, w, indent); break;
            case QuoteBlock q: LayQuote(q, x, w); break;
            case Table t: LayTable(t, x, w); break;
            case ThematicBreakBlock: LayHR(x, w); break;
        }
    }

    void Store(Block b, float h, float w, float x, int indent)
    {
        _layout.Add(new LayoutEntry(b, _totalHeight - h, h, w, x, indent));
    }

    void LayHeading(HeadingBlock h, float x, float w)
    {
        string t = TextOf(h.Inline);
        if (string.IsNullOrEmpty(t)) return;
        float fs = h.Level switch { 1 => DocumentStyle.H1FontSize, 2 => DocumentStyle.H2FontSize, 3 => DocumentStyle.H3FontSize, 4 => DocumentStyle.H4FontSize, 5 => DocumentStyle.H5FontSize, _ => DocumentStyle.H6FontSize };
        using var fmt = new CanvasTextFormat { FontFamily = DocumentStyle.FontFamily, FontSize = fs, FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 }, WordWrapping = CanvasWordWrapping.Wrap };
        using var lo = new CanvasTextLayout(Device, t, fmt, w, _maxTextureHeight);
        float textH = (float)lo.DrawBounds.Height;
        float hh = DocumentStyle.HeadingMarginTop + textH + DocumentStyle.HeadingMarginBottom + (h.Level <= 2 ? DocumentStyle.HeadingBorderHeight + 8 : 0);
        _totalHeight += hh; Store(h, hh, w, x, 0);
    }

    void DrawHeading(CanvasDrawingSession ss, HeadingBlock h, LayoutEntry e)
    {
        string t = TextOf(h.Inline);
        if (string.IsNullOrEmpty(t)) return;
        float fs = h.Level switch { 1 => DocumentStyle.H1FontSize, 2 => DocumentStyle.H2FontSize, 3 => DocumentStyle.H3FontSize, 4 => DocumentStyle.H4FontSize, 5 => DocumentStyle.H5FontSize, _ => DocumentStyle.H6FontSize };
        using var fmt = new CanvasTextFormat { FontFamily = DocumentStyle.FontFamily, FontSize = fs, FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 }, WordWrapping = CanvasWordWrapping.Wrap };
        using var lo = new CanvasTextLayout(Device, t, fmt, e.W, _maxTextureHeight);
        float y0 = e.Y + DocumentStyle.HeadingMarginTop;
        ss.DrawTextLayout(lo, e.X, y0, DocumentStyle.TextColor);
        if (h.Level <= 2)
        {
            float lineY = y0 + (float)lo.DrawBounds.Height + 8;
            ss.DrawLine(e.X, lineY, e.X + e.W, lineY, DocumentStyle.HeadingBorderColor, 1);
        }
    }

    void LayInlines(ContainerInline? inl, float x, float w)
    {
        var runs = GetRuns(inl, out var hasTask, out var images);
        if (runs.Count == 0 && images.Count == 0) return;
        float taskOff = hasTask ? DocumentStyle.TaskCheckSize + DocumentStyle.TaskCheckMargin : 0;
        float textH = 0;
        if (runs.Count > 0)
        {
            using var lo = BuildRichLayout(runs, w - taskOff);
            if (lo != null) textH = (float)lo.DrawBounds.Height;
        }
        float hh = textH + (textH > 0 ? DocumentStyle.ParagraphSpacing : 0);
        if (images.Count > 0)
        {
            float imgY = _totalHeight + hh;
            float cx = x;
            float lineH = 0;
            foreach (var img in images)
            {
                var (iw, ih) = GetImageDimensions(img.Url, img.Alt, w);
                if (cx + iw > x + w && cx > x)
                {
                    imgY += lineH; lineH = 0; cx = x;
                }
                _imageEntries.Add(new ImageEntry(img.Url, img.Alt, cx, imgY, iw, ih));
                cx += iw + 4;
                if (ih > lineH) lineH = ih;
                _maxContentWidth = Math.Max(_maxContentWidth, cx);
            }
            hh = (imgY + lineH) - _totalHeight +4;
        }
        if (hh <= 0) hh = DocumentStyle.ParagraphSpacing;
        _totalHeight += hh;
        var block = inl?.ParentBlock;
        if (block != null) Store(block, hh, w, x, 0);
    }

    void DrawInlines(CanvasDrawingSession ss, ContainerInline? inl, LayoutEntry e)
    {
        var runs = GetRuns(inl, out var hasTask, out _);
        if (runs.Count == 0) return;
        float taskOff = hasTask ? DocumentStyle.TaskCheckSize + DocumentStyle.TaskCheckMargin : 0;
        var lo = BuildRichLayout(runs, e.W - taskOff);
        if (lo == null) return;
        float dx = e.X;
        if (hasTask)
        {
            float cs = DocumentStyle.TaskCheckSize;
            float cy = e.Y + 3;
            ss.DrawRectangle(dx, cy, cs, cs, DocumentStyle.ListMarkerColor);
            if (GetTaskCheck(inl) == true)
            {
                ss.DrawLine(dx + 2, cy + cs / 2, dx + cs / 2, cy + cs - 2, DocumentStyle.TaskCheckColor, 2);
                ss.DrawLine(dx + cs / 2, cy + cs - 2, dx + cs - 2, cy + 2, DocumentStyle.TaskCheckColor, 2);
            }
            dx += taskOff;
        }
        ss.DrawTextLayout(lo, dx, e.Y, DocumentStyle.TextColor);
        if (AreLinksEnabled)
            _linkTargets.Add(new LinkHitTarget(lo, dx, e.Y, runs));
        else
            lo.Dispose();
    }

    void DrawImage(CanvasDrawingSession ss, ImageEntry img)
    {
        var bmp = ImageProvider?.GetImage(img.Url, Device);
        if (bmp != null)
        {
            float iw = (float)bmp.Size.Width;
            float ih = (float)bmp.Size.Height;
            float dw, dh, dx;
            if (DocumentStyle.ImageAlignment == ImageAlignment.Stretch)
            {
                float rw = Math.Min(img.W, DocumentStyle.MaxImageWidth > 0 ? DocumentStyle.MaxImageWidth : img.W);
                float scale = Math.Min(rw / iw, img.H / ih);
                dw = iw * scale;
                dh = ih * scale;
                dx = img.X + (img.W - dw) / 2;
            }
            else
            {
                dw = Math.Min(iw, img.W);
                dh = Math.Min(ih, img.H);
                dx = img.X;
                dx += DocumentStyle.ImageAlignment switch
                {
                    ImageAlignment.Right => img.W - dw,
                    ImageAlignment.Center => (img.W - dw) / 2,
                    _ => 0
                };
            }
            float dy = img.Y + (img.H - dh) / 2;
            float sdx = MathF.Floor(dx);
            float sdy = MathF.Floor(dy);
            float sdw = MathF.Ceiling(dx + dw) - sdx;
            float sdh = MathF.Ceiling(dy + dh) - sdy;
            var dstRect = new Windows.Foundation.Rect(sdx, sdy, sdw, sdh);
            var srcRect = new Windows.Foundation.Rect(0, 0, bmp.Size.Width, bmp.Size.Height);
            ss.DrawImage(bmp, dstRect, srcRect, 1, CanvasImageInterpolation.HighQualityCubic);
        }
        else
        {
            float fx = MathF.Floor(img.X);
            float fy = MathF.Floor(img.Y);
            float fw = MathF.Ceiling(img.X + img.W) - fx;
            float fh = MathF.Ceiling(img.Y + img.H) - fy;
            ss.DrawRectangle(fx, fy, fw, fh, DocumentStyle.ImageBorderColor);
            using var mf = new CanvasTextFormat { FontFamily = DocumentStyle.FontFamily, FontSize = DocumentStyle.BodyFontSize, WordWrapping = CanvasWordWrapping.Wrap };
            ss.DrawText(img.Alt, fx + 5, fy + 4, Math.Max(0, fw - 4), Math.Max(0, fh - 8), DocumentStyle.TextColor, mf);
        }
    }

    (float Width, float Height) GetImageDimensions(string url, string alt, float maxW)
    {
        var bmp = ImageProvider?.GetImage(url, Device);
        if (bmp != null)
        {
            float iw = (float)bmp.Size.Width;
            float ih = (float)bmp.Size.Height;
            float maxAllowed = DocumentStyle.MaxImageWidth > 0 ? Math.Min(DocumentStyle.MaxImageWidth, maxW) : maxW;
            if (DocumentStyle.ImageAlignment == ImageAlignment.Stretch)
            {
                float scale = maxAllowed / iw;
                return (maxAllowed, ih * scale + DocumentStyle.ParagraphSpacing);
            }
            if (iw > maxAllowed)
            {
                float scale = maxAllowed / iw;
                return (maxAllowed, ih * scale + DocumentStyle.ParagraphSpacing);
            }
            return (iw, ih + DocumentStyle.ParagraphSpacing);
        }
        using var altLayout = new CanvasTextLayout(Device, alt, _bf!, maxW, _maxTextureHeight);
        float aw = Math.Min((float)altLayout.DrawBounds.Width + 12, maxW);
        int lineCount = Math.Max(1, altLayout.LineCount);
        float lineH = DocumentStyle.BodyFontSize * 1.4f;
        float ah = lineH * lineCount;
        return (aw, ah + DocumentStyle.ParagraphSpacing);
    }

    static bool? GetTaskCheck(ContainerInline? inl)
    {
        if (inl == null) return null;
        foreach (var x in inl) if (x is TaskList tl) return tl.Checked;
        return null;
    }

    void LayCode(LeafBlock c, float x, float w)
    {
        string code = CodeText(c);
        if (string.IsNullOrEmpty(code)) return;
        float pad = DocumentStyle.CodeBlockPadding, mar = DocumentStyle.CodeBlockMargin;
        using var lo = new CanvasTextLayout(Device, code, _cf!, w - pad * 2, _maxTextureHeight);
        float codeW = (float)lo.DrawBounds.Width + pad * 2;
        float hh = pad * 2 + (float)lo.DrawBounds.Height + mar * 2;
        _totalHeight += hh; Store(c, hh, w, x, 0);
        _maxContentWidth = Math.Max(_maxContentWidth, x + codeW);
    }

    void DrawCode(CanvasDrawingSession ss, LeafBlock c, LayoutEntry e)
    {
        string code = CodeText(c);
        if (string.IsNullOrEmpty(code)) return;
        float pad = DocumentStyle.CodeBlockPadding, mar = DocumentStyle.CodeBlockMargin;
        ss.FillRectangle(e.X, e.Y + mar, e.W, e.H - mar * 2, DocumentStyle.CodeBackgroundColor);
        using var lo = new CanvasTextLayout(Device, code, _cf!, e.W - pad * 2, _maxTextureHeight);
        ss.DrawTextLayout(lo, e.X + pad, e.Y + mar + pad, DocumentStyle.TextColor);
    }

    static string CodeText(LeafBlock c)
    {
        if (c.Lines.Lines == null) return "";
        var sb = new StringBuilder();
        foreach (var line in c.Lines.Lines)
            sb.AppendLine(line.ToString());
        return sb.ToString().TrimEnd();
    }

    void LayList(ListBlock l, float x, float w, int indent)
    {
        foreach (var item in l)
        {
            if (item is not ListItemBlock li) continue;
            float iy = _totalHeight;
            float childX = x + DocumentStyle.ListIndent;
            float childW = w - DocumentStyle.ListIndent;
            foreach (var child in li) Layout(child, childX, childW, indent + 1);
            _totalHeight += DocumentStyle.ListItemSpacing;
            Store(li, _totalHeight - iy, w, x, indent);
        }
    }

    void DrawList(CanvasDrawingSession ss, ListBlock l, LayoutEntry e)
    {
        int idx = 0;
        foreach (var item in l)
        {
            if (item is not ListItemBlock li) continue;
            float mx = e.X + e.Indent * DocumentStyle.ListIndent;

            if (HasTask(li))
            {
                float cs = DocumentStyle.TaskCheckSize;
                float cy = e.Y + 3;
                ss.DrawRectangle(mx, cy, cs, cs, DocumentStyle.ListMarkerColor);
                if (GetTaskCheck(li) == true)
                {
                    ss.DrawLine(mx + 2, cy + cs / 2, mx + cs / 2, cy + cs - 2, DocumentStyle.TaskCheckColor, 2);
                    ss.DrawLine(mx + cs / 2, cy + cs - 2, mx + cs - 2, cy + 2, DocumentStyle.TaskCheckColor, 2);
                }
            }
            else if (l.IsOrdered)
            {
                using var mf = new CanvasTextFormat { FontFamily = DocumentStyle.FontFamily, FontSize = DocumentStyle.BodyFontSize };
                ss.DrawText($"{idx + 1}.", mx, e.Y, DocumentStyle.ListMarkerColor, mf);
            }
            else
            {
                ss.FillCircle(mx + 3, e.Y + 7, 3, DocumentStyle.ListMarkerColor);
            }
            idx++;
        }
    }

    static bool HasTask(ListItemBlock li)
    {
        foreach (var c in li) if (c is ParagraphBlock p && p.Inline != null) foreach (var x in p.Inline) if (x is TaskList) return true;
        return false;
    }

    static bool? GetTaskCheck(ListItemBlock li)
    {
        foreach (var c in li) if (c is ParagraphBlock p && p.Inline != null) foreach (var x in p.Inline) if (x is TaskList tl) return tl.Checked;
        return null;
    }

    void LayQuote(QuoteBlock q, float x, float w)
    {
        float innerX = x + DocumentStyle.QuoteBarWidth + DocumentStyle.QuotePadding;
        float innerW = w - DocumentStyle.QuoteBarWidth - DocumentStyle.QuotePadding;
        float qy = _totalHeight;
        _totalHeight += DocumentStyle.QuoteMargin;
        foreach (var c in q) Layout(c, innerX, innerW, 0);
        _totalHeight += DocumentStyle.QuoteMargin;
        Store(q, _totalHeight - qy, w, x, 0);
    }

    void DrawQuote(CanvasDrawingSession ss, QuoteBlock q, LayoutEntry e)
    {
        ss.FillRectangle(e.X, e.Y, e.W, e.H, DocumentStyle.QuoteBackgroundColor);
        ss.FillRectangle(e.X, e.Y, DocumentStyle.QuoteBarWidth, e.H, DocumentStyle.QuoteBarColor);
        foreach (var c in q) { var idx = _layout.FindIndex(x => ReferenceEquals(x.B, c)); if (idx >= 0) Draw(ss, _layout[idx]); }
    }

    void LayTable(Table t, float x, float w)
    {
        var rows = t.OfType<TableRow>().ToList();
        if (rows.Count == 0) return;
        int cc = rows.Max(r => r.Count);
        if (cc == 0) return;

        var allRuns = new List<TextRun>[rows.Count][];
        var allImages = new List<ImageInfo>[rows.Count][];
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var row = rows[ri];
            allRuns[ri] = new List<TextRun>[row.Count];
            allImages[ri] = new List<ImageInfo>[row.Count];
            for (int i = 0; i < row.Count; i++)
            {
                if (row[i] is TableCell cell)
                {
                    var (runs, images) = GetCellContent(cell);
                    allRuns[ri][i] = runs;
                    allImages[ri][i] = images;
                }
                else
                {
                    allRuns[ri][i] = new List<TextRun>();
                    allImages[ri][i] = new List<ImageInfo>();
                }
            }
        }

        var cw = ColWidths(rows, cc, w);
        var rh = new float[rows.Count];
        float ty = _totalHeight;

        for (int ri = 0; ri < rows.Count; ri++)
        {
            var row = rows[ri];
            float mh = 0;
            float cx = x;
            for (int i = 0; i < row.Count && i < cc; i++)
            {
                if (row[i] is TableCell cell)
                {
                    var runs = allRuns[ri][i];
                    var imgs = allImages[ri][i];
                    float textH = 0, imgTotalH = 0;

                    if (runs.Count > 0)
                    {
                        using var lo = BuildRichLayout(runs, cw[i] - DocumentStyle.TableCellPadding * 2);
                        if (lo != null) textH = (float)lo.DrawBounds.Height;
                    }

                    float iy = _totalHeight + DocumentStyle.TableCellPadding + textH;
                    foreach (var imgInfo in imgs)
                    {
                        var (iw, ih) = GetImageDimensions(imgInfo.Url, imgInfo.Alt, cw[i] - DocumentStyle.TableCellPadding * 2);
                        _imageEntries.Add(new ImageEntry(imgInfo.Url, imgInfo.Alt, cx + DocumentStyle.TableCellPadding, iy, iw, ih));
                        iy += ih;
                        imgTotalH += ih;
                    }

                    float cellH = textH + imgTotalH + DocumentStyle.TableCellPadding * 2;
                    if (cellH > mh) mh = cellH;
                }
                cx += cw[i] + 1;
            }
            rh[ri] = mh > 0 ? mh : 20;
            _totalHeight += rh[ri] + 1;
        }

        float tableW = x;
        for (int i = 0; i < cc; i++) tableW += cw[i] + 1;
        _maxContentWidth = Math.Max(_maxContentWidth, tableW);

        var flatRuns = new List<TextRun>[rows.Count * cc];
        for (int ri = 0; ri < rows.Count; ri++)
            for (int i = 0; i < rows[ri].Count && i < cc; i++)
                flatRuns[ri * cc + i] = allRuns[ri][i];

        _tableCache[t] = new TableLayoutInfo { ColWidths = cw, RowHeights = rh, CellRuns = flatRuns };
        Store(t, _totalHeight - ty, w, x, 0);
    }

    void DrawTable(CanvasDrawingSession ss, Table t, LayoutEntry e)
    {
        if (!_tableCache.TryGetValue(t, out var cache)) return;
        var cw = cache.ColWidths;
        var rh = cache.RowHeights;
        var allRuns = cache.CellRuns;
        var rows = t.OfType<TableRow>().ToList();
        if (rows.Count == 0) return;
        int cc = cw.Length;
        float tableEndX = e.X;
        for (int i = 0; i < cc; i++) tableEndX += cw[i] + 1;
        float cy = e.Y;
        for (int ri = 0; ri < rows.Count && ri < rh.Length; ri++)
        {
            var row = rows[ri];
            bool isH = row.IsHeader;
            float mh = rh[ri];
            if (isH) ss.FillRectangle(e.X, cy, tableEndX - e.X, mh, DocumentStyle.TableHeaderBackground);
            float cx = e.X;
            for (int i = 0; i < row.Count && i < cc; i++)
            {
                if (row[i] is TableCell cell)
                {
                    int idx = ri * cc + i;
                    var runs = (allRuns != null && idx < allRuns.Length) ? allRuns[idx] : null;
                    if (runs != null && runs.Count > 0)
                    {
                        if (isH)
                        {
                            using var hf = new CanvasTextFormat { FontFamily = DocumentStyle.FontFamily, FontSize = DocumentStyle.BodyFontSize, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }, WordWrapping = CanvasWordWrapping.Wrap };
                            using var lo = BuildRichLayout(runs, cw[i] - DocumentStyle.TableCellPadding * 2, hf);
                            if (lo != null)
                                ss.DrawTextLayout(lo, cx + DocumentStyle.TableCellPadding, cy + DocumentStyle.TableCellPadding, DocumentStyle.TextColor);
                        }
                        else
                        {
                            using var lo = BuildRichLayout(runs, cw[i] - DocumentStyle.TableCellPadding * 2);
                            if (lo != null)
                                ss.DrawTextLayout(lo, cx + DocumentStyle.TableCellPadding, cy + DocumentStyle.TableCellPadding, DocumentStyle.TextColor);
                        }
                    }
                    ss.DrawLine(cx, cy, cx, cy + mh, DocumentStyle.TableBorderColor);
                }
                cx += cw[i] + 1;
            }
            ss.DrawLine(cx, cy, cx, cy + mh, DocumentStyle.TableBorderColor);
            ss.DrawLine(e.X, cy, cx, cy, DocumentStyle.TableBorderColor);
            ss.DrawLine(e.X, cy + mh, cx, cy + mh, DocumentStyle.TableBorderColor);
            cy += mh + 1;
        }
    }

    float[] ColWidths(List<TableRow> rows, int cc, float maxW)
    {
        float[] w = new float[cc];
        for (int i = 0; i < cc; i++) w[i] = 60;
        foreach (var row in rows)
            for (int i = 0; i < row.Count && i < cc; i++)
                if (row[i] is TableCell cell)
                {
                    string t = CellText(cell);
                    using var lo = new CanvasTextLayout(Device, t, _bf!, _maxTextureHeight, _maxTextureHeight);
                    float tw = (float)lo.DrawBounds.Width + DocumentStyle.TableCellPadding * 2 + 2;
                    if (tw > w[i]) w[i] = tw;
                }
        float total = 0; for (int i = 0; i < cc; i++) total += w[i] + 1;
        if (total < maxW && cc > 0) { float extra = (maxW - total) / cc; for (int i = 0; i < cc; i++) w[i] += extra; }
        return w;
    }

    static string CellText(TableCell cell)
    {
        var sb = new StringBuilder();
        foreach (var c in cell) if (c is ParagraphBlock p) sb.Append(TextOf(p.Inline));
        return sb.ToString().TrimEnd();
    }

    (List<TextRun> runs, List<ImageInfo> images) GetCellContent(TableCell cell)
    {
        var runs = new List<TextRun>();
        var images = new List<ImageInfo>();
        bool hasTask = false;
        foreach (var c in cell)
            if (c is ParagraphBlock p && p.Inline != null)
                Walk(p.Inline, runs, false, false, false, false, null, ref hasTask, images);
        return (runs, images);
    }

    void LayHR(float x, float w) { float hh = DocumentStyle.HorizontalRuleMargin * 2 + 2; _totalHeight += hh; Store(new ThematicBreakBlock(null!), hh, w, x, 0); }

    void DrawHR(CanvasDrawingSession ss, LayoutEntry e) { ss.DrawLine(e.X, e.Y + DocumentStyle.HorizontalRuleMargin, e.X + e.W, e.Y + DocumentStyle.HorizontalRuleMargin, DocumentStyle.HorizontalRuleColor, 1); }

    void Draw(CanvasDrawingSession ss, LayoutEntry e)
    {
        switch (e.B)
        {
            case HeadingBlock h: DrawHeading(ss, h, e); break;
            case ParagraphBlock p: DrawInlines(ss, p.Inline, e); break;
            case FencedCodeBlock fc: DrawCode(ss, fc, e); break;
            case CodeBlock ic: DrawCode(ss, ic, e); break;
            case ListBlock l: DrawList(ss, l, e); break;
            case QuoteBlock q: DrawQuote(ss, q, e); break;
            case Table t: DrawTable(ss, t, e); break;
            case ThematicBreakBlock: DrawHR(ss, e); break;
            case LeafBlock lb when lb.Inline != null: DrawInlines(ss, lb.Inline, e); break;
        }
    }

    List<TextRun> GetRuns(ContainerInline? inl, out bool hasTask, out List<ImageInfo> images)
    {
        hasTask = false;
        images = new List<ImageInfo>();
        var runs = new List<TextRun>();
        if (inl == null) return runs;
        Walk(inl, runs, false, false, false, false, null, ref hasTask, images);
        return runs;
    }

    static void Walk(Inline inl, List<TextRun> runs, bool b, bool i, bool s, bool lk, string? lu, ref bool hasTask, List<ImageInfo> images)
    {
        switch (inl)
        {
            case LiteralInline lit:
                runs.Add(new TextRun { T = lit.Content.ToString(), B = b, I = i, S = s, L = lk, U = lu ?? "" });
                break;
            case CodeInline code:
                runs.Add(new TextRun { T = code.Content, C = true, L = lk, U = lu ?? "" });
                break;
            case TaskList tl:
                hasTask = true;
                break;
            case EmphasisInline em:
                bool st = em.DelimiterChar == '~';
                bool nb = b || (!st && em.DelimiterCount >= 2);
                bool ni = i || (!st && (em.DelimiterCount == 1 || em.DelimiterCount == 3));
                bool ns = s || st;
                foreach (var ch in (ContainerInline)em) Walk(ch, runs, nb, ni, ns, lk, lu, ref hasTask, images);
                break;
            case LinkInline li when li.IsImage:
                images.Add(new ImageInfo(li.Url ?? "", TextOf(li)));
                break;
            case LinkInline li when !li.IsImage:
                foreach (var ch in (ContainerInline)li) Walk(ch, runs, b, i, s, true, li.Url, ref hasTask, images);
                break;
            case AutolinkInline al:
                runs.Add(new TextRun { T = al.Url, L = true, U = al.Url });
                break;
            case LineBreakInline:
                runs.Add(new TextRun { T = "\n" });
                break;
            case HtmlInline html:
                runs.Add(new TextRun { T = html.Tag });
                break;
            case ContainerInline ci:
                foreach (var ch in ci) Walk(ch, runs, b, i, s, lk, lu, ref hasTask, images);
                break;
        }
    }

    CanvasTextLayout? BuildRichLayout(List<TextRun> runs, float w, CanvasTextFormat? baseFormat = null)
    {
        var sb = new StringBuilder();
        foreach (var r in runs) sb.Append(r.T);
        string ft = sb.ToString();
        if (string.IsNullOrEmpty(ft)) return null;
        var lo = new CanvasTextLayout(Device, ft, baseFormat ?? _bf!, w, _maxTextureHeight);
        try
        {
            int off = 0;
            var fwBold = new Windows.UI.Text.FontWeight { Weight = 700 };
            foreach (var r in runs)
            {
                int len = r.T.Length; if (len <= 0) continue;
                if (r.C)
                {
                    lo.SetFontFamily(off, len, DocumentStyle.CodeFontFamily);
                    lo.SetFontSize(off, len, DocumentStyle.CodeFontSize);
                }
                else
                {
                    if (r.B) lo.SetFontWeight(off, len, fwBold);
                    if (r.I) lo.SetFontStyle(off, len, Windows.UI.Text.FontStyle.Italic);
                }
                if (r.S) lo.SetStrikethrough(off, len, true);
                if (r.L) { lo.SetColor(off, len, DocumentStyle.LinkColor); lo.SetUnderline(off, len, true); }
                off += len;
            }
            return lo;
        }
        catch
        {
            lo.Dispose();
            throw;
        }
    }

    static string TextOf(ContainerInline? inl)
    {
        if (inl == null) return "";
        var sb = new StringBuilder();
        WalkText(inl, sb);
        return sb.ToString().TrimEnd();
    }

    static void WalkText(Inline inl, StringBuilder sb)
    {
        switch (inl)
        {
            case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
            case CodeInline code: sb.Append(code.Content); break;
            case LineBreakInline: sb.Append(' '); break;
            case LinkInline li when li.IsImage:
                sb.Append('[');
                if (li is ContainerInline c) foreach (var ch in c) WalkText(ch, sb);
                sb.Append(']');
                break;
            case ContainerInline ci:
                foreach (var ch in ci) WalkText(ch, sb);
                break;
        }
    }

    readonly record struct LayoutEntry(Block B, float Y, float H, float W, float X, int Indent);

    internal struct TextRun
    {
        public string T = "";
        public bool B, I, S, C, L;
        public string U = "";
        public TextRun() { }
    }

    readonly record struct LinkHitTarget(CanvasTextLayout Layout, float X, float Y, List<TextRun> Runs);

    private void OnCanvasPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!AreLinksEnabled) return;
        var pos = e.GetCurrentPoint(MarkdownCanvas).Position;
        foreach (var hit in _linkTargets)
        {
            float lx = (float)(pos.X - hit.X);
            float ly = (float)(pos.Y - hit.Y);
            if (!hit.Layout.HitTest(lx, ly)) continue;
            hit.Layout.HitTest(lx, ly, out CanvasTextLayoutRegion region);
            if (region.CharacterCount == 0) continue;
            int charIdx = (int)region.CharacterIndex;
            int off = 0;
            foreach (TextRun r in hit.Runs)
            {
                if (r.L && r.U.Length > 0 && charIdx >= off && charIdx < off + r.T.Length)
                {
                    try { Process.Start(new ProcessStartInfo(r.U) { UseShellExecute = true }); } catch { }
                    return;
                }
                off += r.T.Length;
            }
        }
    }
}