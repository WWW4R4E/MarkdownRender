namespace MarkWin2D.Layout;

internal class TableLayoutInfo
{
    public float[] ColWidths { get; set; } = [];
    public float[] RowHeights { get; set; } = [];
    public List<MarkWin2D.Controls.MarkWin2DControl.TextRun>[]? CellRuns { get; set; }
}
