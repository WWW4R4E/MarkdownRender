# MarkWin2D

基于 **Win2D** 的 **GitHub Flavored Markdown** 渲染控件，用于 **WinUI 3** 应用。

## 功能

- 标题 (H1–H6)
- 粗体、斜体、删除线
- 行内代码 & 围栏代码块
- 有序 / 无序 / 任务列表
- 嵌套块引用
- 表格（自动列宽）
- 分隔线
- 超链接（可点击，支持开关）
- **图片**（通过 `IImageProvider` 接入）
- **图片对齐** — 左对齐 / 居中 / 右对齐

## 安装

```
dotnet add package MarkWin2D
```

## 快速开始

### XAML

```xml
<UserControl
    xmlns:md="using:MarkWin2D.Controls">
    <md:MarkWin2DControl x:Name="MarkdownView" />
</UserControl>
```

### 代码

```csharp
MarkdownView.Text = "# Hello\n**粗体** `代码`";
MarkdownView.AreLinksEnabled = true;   // 开启链接点击
```

## 图片

控件不自带下载。实现 `IImageProvider` 后挂载：

```csharp
public class MyImageProvider : IImageProvider
{
    private readonly HttpClient _http = new();
    private readonly Dictionary<string, CanvasBitmap> _cache = new();

    public event EventHandler? ImagesInvalidated;

    public CanvasBitmap? GetImage(string url, CanvasDevice device)
    {
        if (_cache.TryGetValue(url, out var bmp)) return bmp;
        _ = LoadAsync(url, device);
        return null;
    }

    private async Task LoadAsync(string url, CanvasDevice device)
    {
        var bytes = await _http.GetByteArrayAsync(url);
        using var ms = new MemoryStream(bytes);
        var bmp = await CanvasBitmap.LoadAsync(device, ms.AsRandomAccessStream());
        _cache[url] = bmp;
        ImagesInvalidated?.Invoke(this, EventArgs.Empty);
    }
}

MarkdownView.ImageProvider = new MyImageProvider();
```

## 图片对齐

```csharp
MarkdownView.DocumentStyle.ImageAlignment = ImageAlignment.Center;
// Left（默认）、Center、Right
```

## 样式

所有样式属性通过 `MarkdownStyle` 暴露：

```csharp
MarkdownView.DocumentStyle = new MarkdownStyle
{
    FontFamily = "Consolas",
    BodyFontSize = 14,
    TextColor = Colors.White,
    ImageAlignment = ImageAlignment.Center
};
```

### DocumentStyle 完整配置

你可以通过 `DocumentStyle` 属性精细控制所有渲染样式：

```csharp
MarkdownView.DocumentStyle = new MarkdownStyle
{
    // 颜色
    TextColor = Colors.White,
    BackgroundColor = Color.FromArgb(230, 28, 28, 30),
    CodeBackgroundColor = Color.FromArgb(120, 40, 40, 44),
    LinkColor = Color.FromArgb(79, 140, 255),
    QuoteBarColor = Color.FromArgb(100, 100, 100),
    QuoteBackgroundColor = Color.FromArgb(120, 35, 35, 40),
    TableBorderColor = Color.FromArgb(70, 70, 75),
    TableHeaderBackground = Color.FromArgb(160, 45, 45, 50),
    HorizontalRuleColor = Color.FromArgb(80, 80, 85),
    HeadingBorderColor = Color.FromArgb(80, 80, 85),
    ListMarkerColor = Color.FromArgb(160, 160, 160),
    TaskCheckColor = Color.FromArgb(100, 200, 100),

    // 字体
    FontFamily = "Microsoft YaHei",
    CodeFontFamily = "Consolas",

    // 字号
    H1FontSize = 26,
    H2FontSize = 22,
    H3FontSize = 18,
    H4FontSize = 16,
    H5FontSize = 14,
    H6FontSize = 13,
    BodyFontSize = 15,
    CodeFontSize = 13,

    // 间距
    DocumentMargin = 16,
    ParagraphSpacing = 8,
    HeadingMarginTop = 14,
    HeadingMarginBottom = 6,
    ListIndent = 22,
    ListItemSpacing = 4,
    CodeBlockPadding = 12,
    CodeBlockMargin = 8,
    QuoteBarWidth = 3,
    QuotePadding = 12,
    QuoteMargin = 6,
    TableCellPadding = 6,
    HorizontalRuleMargin = 12,

    // 图片
    MaxImageWidth = 0,          // 0 表示不限制
    ImageAlignment = ImageAlignment.Left,  // Left / Center / Right / Stretch
    ImagePlaceholderColor = Color.FromArgb(60, 60, 65),
    ImageBorderColor = Color.FromArgb(80, 80, 85),

    // 任务列表
    TaskCheckSize = 14,
    TaskCheckMargin = 6,
};
```

## 虚拟化

控件基于 `CanvasVirtualControl` 实现虚拟化渲染，只对可视区域内的内容进行绘制，不会将全部文档一次性渲染到画布上：

- 滚动时自动按需绘制可见区域中的文本、代码块、表格和图片
- 表格列宽自动计算，超宽表格支持横向滚动
- 即使文档内容极长，也不会因一次性绘制全部内容导致卡顿

## 高度限制

Win2D 存在单次纹理最大高度限制（通常为 **16384 像素**）。控件内部已对此进行处理：

- 所有 `CanvasTextLayout` 创建时都传入 `_maxTextureHeight = 16384`，避免越界崩溃
- 超出高度的内容通过 `CanvasVirtualControl` 的虚拟化机制分段渲染
- 布局计算采用浮点坐标，支持超长文档的正常滚动浏览

## 背景合成

控件支持与 XAML 背景进行合成，适合与 **MicaBackdrop**、**AcrylicBackdrop** 等透明背景效果配合使用。

`DocumentStyle.BackgroundColor` 默认带有 alpha 通道（半透明）。当你在 XAML 中为页面设置了背景（如 Mica 云母效果）时，会与文档背景自然混合：

```xml
<Page>
    <Page.SystemBackdrop>
        <MicaBackdrop />
    </Page.SystemBackdrop>
    <md:MarkWin2DControl x:Name="MarkdownView" />
</Page>
```

你也可以将 `BackgroundColor` 的 alpha 设为 0，完全透出 XAML 背景：

```csharp
MarkdownView.DocumentStyle.BackgroundColor = Colors.Transparent;
```

## 链接点击

默认启用超链接点击。点击链接时会使用系统默认浏览器打开：

```csharp
MarkdownView.AreLinksEnabled = true;   // 开启（默认）
MarkdownView.AreLinksEnabled = false;  // 关闭
```