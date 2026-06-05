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
    private readonly MarkdownPipeline _pipeline;
    private CanvasTextFormat? _bf, _cf;
    private CanvasDevice? _device;
    private CanvasDevice Device => _device ??= CanvasDevice.GetSharedDevice();
    private readonly Dictionary<Table, TableLayoutInfo> _tableCache = new();
    private IImageProvider? _imageProvider;
    private readonly List<LinkHitTarget> _linkTargets = new();

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
        ComputeLayout((float)MarkdownCanvas.ActualWidth);
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

    private void OnDraw(CanvasControl s, CanvasDrawEventArgs a)
    {
        var ss = a.DrawingSession;
        foreach (var t in _linkTargets) t.Layout.Dispose();
        _linkTargets.Clear();
        if (_layoutDirty && s.ActualWidth > 0)
            ComputeLayout((float)s.ActualWidth);

        if (_document == null) return;
        foreach (var e in _layout) Draw(ss, e);
        foreach (var img in _imageEntries) DrawImage(ss, img);
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
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
        _totalHeight = DocumentStyle.DocumentMargin;
        float cw = w - DocumentStyle.DocumentMargin * 2;
        if (cw < 0) cw = w;
        foreach (var b in _document) Layout(b, DocumentStyle.DocumentMargin, cw, 0);
        _totalHeight += DocumentStyle.DocumentMargin;
        _lastWidth = w;
        _layoutDirty = false;

        if (!float.IsNaN(_totalHeight) &&
        (float.IsNaN((float)MarkdownCanvas.Height) ||
         Math.Abs(MarkdownCanvas.Height - _totalHeight) > 1))
        {
            MarkdownCanvas.Height = _totalHeight;
            ScrollViewer.InvalidateMeasure();
        }
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
        using var lo = new CanvasTextLayout(Device, t, fmt, w, 100000);
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
        using var lo = new CanvasTextLayout(Device, t, fmt, e.W, 100000);
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
        float imgY = _totalHeight + hh;
        foreach (var img in images)
        {
            float ih = ImageHeight(img.Url, w);
            _imageEntries.Add(new ImageEntry(img.Url, img.Alt, x, imgY, w, ih));
            imgY += ih;
            hh += ih;
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
            float rw = Math.Min(img.W, DocumentStyle.MaxImageWidth > 0 ? DocumentStyle.MaxImageWidth : img.W);
            float scale = Math.Min(rw / iw, img.H / ih);
            float dw = iw * scale;
            float dh = ih * scale;
            float dx = img.X;
            dx += DocumentStyle.ImageAlignment switch
            {
                ImageAlignment.Right => img.W - dw,
                ImageAlignment.Center => (img.W - dw) / 2,
                _ => 0
            };
            float dy = img.Y + (img.H - dh) / 2;
            var dstRect = new Windows.Foundation.Rect(dx, dy, dw, dh);
            var srcRect = new Windows.Foundation.Rect(0, 0, bmp.Size.Width, bmp.Size.Height);
            ss.DrawImage(bmp, dstRect, srcRect, 1, CanvasImageInterpolation.HighQualityCubic);
        }
        else
        {
            ss.FillRectangle(img.X, img.Y, img.W, img.H, DocumentStyle.ImagePlaceholderColor);
            ss.DrawRectangle(img.X, img.Y, img.W, img.H, DocumentStyle.ImageBorderColor);
            using var mf = new CanvasTextFormat { FontFamily = DocumentStyle.FontFamily, FontSize = DocumentStyle.BodyFontSize, WordWrapping = CanvasWordWrapping.Wrap };
            ss.DrawText(img.Alt, img.X + 4, img.Y + 4, DocumentStyle.TextColor, mf);
        }
    }

    float ImageHeight(string url, float w)
    {
        var bmp = ImageProvider?.GetImage(url, Device);
        if (bmp != null)
        {
            float mw = DocumentStyle.MaxImageWidth > 0 ? Math.Min(DocumentStyle.MaxImageWidth, w) : w;
            float scale = mw / (float)bmp.Size.Width;
            return (float)bmp.Size.Height * scale + DocumentStyle.ParagraphSpacing;
        }
        return 200 + DocumentStyle.ParagraphSpacing;
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
        using var lo = new CanvasTextLayout(Device, code, _cf!, w - pad * 2, 100000);
        float hh = pad * 2 + (float)lo.DrawBounds.Height + mar * 2;
        _totalHeight += hh; Store(c, hh, w, x, 0);
    }

    void DrawCode(CanvasDrawingSession ss, LeafBlock c, LayoutEntry e)
    {
        string code = CodeText(c);
        if (string.IsNullOrEmpty(code)) return;
        float pad = DocumentStyle.CodeBlockPadding, mar = DocumentStyle.CodeBlockMargin;
        ss.FillRectangle(e.X, e.Y + mar, e.W, e.H - mar * 2, DocumentStyle.CodeBackgroundColor);
        using var lo = new CanvasTextLayout(Device, code, _cf!, e.W - pad * 2, 100000);
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
        var cw = ColWidths(rows, cc, w);
        var rh = new float[rows.Count];
        float ty = _totalHeight;
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var row = rows[ri];
            float mh = 0;
            for (int i = 0; i < row.Count && i < cc; i++)
                if (row[i] is TableCell cell)
                {
                    string txt = CellText(cell);
                    using var lo = new CanvasTextLayout(Device, txt, _bf!, cw[i] - DocumentStyle.TableCellPadding * 2, 100000);
                    float h = (float)lo.DrawBounds.Height + DocumentStyle.TableCellPadding * 2;
                    if (h > mh) mh = h;
                }
            rh[ri] = mh > 0 ? mh : 20;
            _totalHeight += rh[ri] + 1;
        }
        _tableCache[t] = new TableLayoutInfo { ColWidths = cw, RowHeights = rh };
        Store(t, _totalHeight - ty, w, x, 0);
    }

    void DrawTable(CanvasDrawingSession ss, Table t, LayoutEntry e)
    {
        if (!_tableCache.TryGetValue(t, out var cache)) return;
        var cw = cache.ColWidths;
        var rh = cache.RowHeights;
        var rows = t.OfType<TableRow>().ToList();
        if (rows.Count == 0) return;
        int cc = cw.Length;
        float cy = e.Y;
        for (int ri = 0; ri < rows.Count && ri < rh.Length; ri++)
        {
            var row = rows[ri];
            bool isH = row.IsHeader;
            float mh = rh[ri];
            if (isH) ss.FillRectangle(e.X, cy, e.W, mh, DocumentStyle.TableHeaderBackground);
            float cx = e.X;
            for (int i = 0; i < row.Count && i < cc; i++)
            {
                if (row[i] is TableCell cell)
                {
                    string txt = CellText(cell);
                    if (isH)
                    {
                        using var hf = new CanvasTextFormat { FontFamily = DocumentStyle.FontFamily, FontSize = DocumentStyle.BodyFontSize, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } };
                        ss.DrawText(txt, cx + DocumentStyle.TableCellPadding, cy + DocumentStyle.TableCellPadding, DocumentStyle.TextColor, hf);
                    }
                    else ss.DrawText(txt, cx + DocumentStyle.TableCellPadding, cy + DocumentStyle.TableCellPadding, DocumentStyle.TextColor, _bf!);
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
                    using var lo = new CanvasTextLayout(Device, t, _bf!, 10000, 100000);
                    float tw = (float)lo.DrawBounds.Width + DocumentStyle.TableCellPadding * 2 + 2;
                    if (tw > w[i]) w[i] = tw;
                }
        float total = 0; for (int i = 0; i < cc; i++) total += w[i] + 1;
        if (total > maxW && total > cc) { float sc = (maxW - cc) / (total - cc); for (int i = 0; i < cc; i++) w[i] *= sc; }
        else if (total < maxW && cc > 0) { float extra = (maxW - total) / cc; for (int i = 0; i < cc; i++) w[i] += extra; }
        return w;
    }

    static string CellText(TableCell cell)
    {
        var sb = new StringBuilder();
        foreach (var c in cell) if (c is ParagraphBlock p) sb.Append(TextOf(p.Inline));
        return sb.ToString().TrimEnd();
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

    CanvasTextLayout? BuildRichLayout(List<TextRun> runs, float w)
    {
        var sb = new StringBuilder();
        foreach (var r in runs) sb.Append(r.T);
        string ft = sb.ToString();
        if (string.IsNullOrEmpty(ft)) return null;
        var lo = new CanvasTextLayout(Device, ft, _bf!, w, 100000);
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

    struct TextRun
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