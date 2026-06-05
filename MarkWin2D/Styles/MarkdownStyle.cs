using Windows.UI;

namespace MarkWin2D.Styles;

public class MarkdownStyle
{
    static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    public Color TextColor { get; set; } = C(220, 220, 220);
    public Color BackgroundColor { get; set; } = Color.FromArgb(230, 28, 28, 30);
    public Color CodeBackgroundColor { get; set; } = Color.FromArgb(120, 40, 40, 44);
    public Color CodeBorderColor { get; set; } = Color.FromArgb(80, 60, 60, 66);
    public Color LinkColor { get; set; } = C(79, 140, 255);
    public Color QuoteBarColor { get; set; } = C(100, 100, 100);
    public Color QuoteBackgroundColor { get; set; } = Color.FromArgb(120, 35, 35, 40);
    public Color TableBorderColor { get; set; } = C(70, 70, 75);
    public Color TableHeaderBackground { get; set; } = Color.FromArgb(160, 45, 45, 50);
    public Color HorizontalRuleColor { get; set; } = C(80, 80, 85);
    public Color TaskCheckColor { get; set; } = C(100, 200, 100);
    public Color HeadingBorderColor { get; set; } = C(80, 80, 85);
    public Color InlineCodeBackground { get; set; } = Color.FromArgb(120, 50, 50, 55);
    public Color InlineCodeBorder { get; set; } = Color.FromArgb(80, 70, 70, 75);
    public Color ListMarkerColor { get; set; } = C(160, 160, 160);

    public string FontFamily { get; set; } = "Microsoft YaHei";
    public string CodeFontFamily { get; set; } = "Consolas";

    public float H1FontSize { get; set; } = 26;
    public float H2FontSize { get; set; } = 22;
    public float H3FontSize { get; set; } = 18;
    public float H4FontSize { get; set; } = 16;
    public float H5FontSize { get; set; } = 14;
    public float H6FontSize { get; set; } = 13;
    public float BodyFontSize { get; set; } = 15;
    public float CodeFontSize { get; set; } = 13;

    public float DocumentMargin { get; set; } = 16;
    public float ParagraphSpacing { get; set; } = 8;
    public float HeadingMarginTop { get; set; } = 14;
    public float HeadingMarginBottom { get; set; } = 6;
    public float HeadingBorderHeight { get; set; } = 1;
    public float ListIndent { get; set; } = 22;
    public float ListItemSpacing { get; set; } = 4;
    public float CodeBlockPadding { get; set; } = 12;
    public float CodeBlockMargin { get; set; } = 8;
    public float QuoteBarWidth { get; set; } = 3;
    public float QuotePadding { get; set; } = 12;
    public float QuoteMargin { get; set; } = 6;
    public float TableCellPadding { get; set; } = 6;
    public float HorizontalRuleMargin { get; set; } = 12;
    public float TaskCheckSize { get; set; } = 14;
    public float TaskCheckMargin { get; set; } = 6;
    public float BulletMargin { get; set; } = 8;

    public float MaxImageWidth { get; set; } = 0;
    public Color ImagePlaceholderColor { get; set; } = C(60, 60, 65);
    public Color ImageBorderColor { get; set; } = C(80, 80, 85);
    public ImageAlignment ImageAlignment { get; set; } = ImageAlignment.Left;
}
