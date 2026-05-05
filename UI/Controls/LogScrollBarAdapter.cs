using Godot;

namespace JmcLogConsole.UI.Controls;

public sealed class LogScrollBarAdapter
{
    private readonly VScrollBar scrollBar;
    private bool updating;

    public LogScrollBarAdapter(VScrollBar scrollBar)
    {
        this.scrollBar = scrollBar;
    }

    public event Action<int>? ValueChangedByUser;

    public void Connect()
    {
        scrollBar.ValueChanged += OnValueChanged;
    }

    public void Update(int totalRows, int viewportRows, int firstRow)
    {
        updating = true;
        try
        {
            int safeViewportRows = Math.Max(1, viewportRows);
            scrollBar.MinValue = 0;
            scrollBar.MaxValue = Math.Max(safeViewportRows, totalRows);
            scrollBar.Page = safeViewportRows;
            scrollBar.Step = 1;
            scrollBar.Visible = totalRows > safeViewportRows;
            scrollBar.Value = Math.Clamp(firstRow, 0, Math.Max(0, totalRows - safeViewportRows));
        }
        finally
        {
            updating = false;
        }
    }

    private void OnValueChanged(double value)
    {
        if (updating)
        {
            return;
        }

        ValueChangedByUser?.Invoke((int)Math.Round(value));
    }
}
