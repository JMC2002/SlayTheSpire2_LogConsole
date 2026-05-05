using System.Text;
using Godot;
using JmcLogConsole.Core;
using JmcLogConsole.ViewModels;
using JmcModLib.Utils;

namespace JmcLogConsole.UI.Controls;

public partial class VirtualLogView : Control
{
    private const int ScrollBarWidth = 14;
    private const int HorizontalScrollBarHeight = 14;
    private const int HorizontalPadding = 8;
    private const int WheelRows = 3;
    private const float HorizontalWheelPixels = 96f;
    private const float BottomBlankRows = 1f;
    private static readonly Color BackgroundColor = new(0.08f, 0.09f, 0.10f);
    private static readonly Color AlternateRowColor = new(1f, 1f, 1f, 0.025f);
    private static readonly Color SelectionColor = new(0.25f, 0.48f, 0.78f, 0.72f);
    private static readonly Color SelectedTextColor = new(0.96f, 0.98f, 1.00f);
    private static readonly Color EmptyTextColor = new(0.54f, 0.57f, 0.60f);
    private static readonly Color ErrorTextColor = new(1.00f, 0.42f, 0.42f);

    private readonly VScrollBar scrollBar = new();
    private readonly HScrollBar horizontalScrollBar = new();
    private readonly ColorRect inputLayer = new();
    private readonly LogSelectionState selectionState = new();
    private readonly List<ColorRect> rowBackgrounds = [];
    private readonly List<ColorRect> selectionBackgrounds = [];
    private readonly List<Label> rowLabels = [];
    private LogScrollBarAdapter? scrollBarAdapter;
    private LogViewportModel? model;
    private LogLineFormatter formatter = LogLineFormatter.FromSettings();
    private LogViewportSnapshot snapshot = LogViewportSnapshot.Empty();
    private Font? logFont;
    private int logFontSize = 16;
    private int lineSpacing = 2;
    private float rowHeight = 24f;
    private float horizontalOffset;
    private float maxVisibleLineWidth;
    private Vector2 lastObservedSize = new(-1f, -1f);
    private int viewportRows = 1;
    private int wrapColumns = 120;
    private int diagnosticsRemaining = 16;
    private int drawDiagnosticsRemaining = 8;
    private int eventDiagnosticsRemaining = 48;
    private string lastDrawDiagnosticKey = string.Empty;
    private string lastRefreshDiagnosticKey = string.Empty;

    public event Action<LogViewportSnapshot>? SnapshotChanged;
    public event Action<int>? FontZoomRequested;

    public string EmptyText { get; set; } = "暂无日志。";
    public string NoMatchesText { get; set; } = "没有匹配的日志。";

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        CustomMinimumSize = new Vector2(0f, 240f);
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);

        scrollBar.Name = "LogScrollBar";
        scrollBar.MouseFilter = MouseFilterEnum.Stop;
        AddChild(scrollBar);
        scrollBarAdapter = new LogScrollBarAdapter(scrollBar);
        scrollBarAdapter.ValueChangedByUser += OnScrollBarValueChangedByUser;
        scrollBarAdapter.Connect();

        horizontalScrollBar.Name = "LogHorizontalScrollBar";
        horizontalScrollBar.MouseFilter = MouseFilterEnum.Stop;
        horizontalScrollBar.Visible = false;
        horizontalScrollBar.MinValue = 0;
        horizontalScrollBar.Step = HorizontalWheelPixels;
        horizontalScrollBar.ValueChanged += OnHorizontalScrollBarValueChanged;
        AddChild(horizontalScrollBar);

        inputLayer.Name = "LogInputSurface";
        inputLayer.Color = BackgroundColor;
        inputLayer.MouseFilter = MouseFilterEnum.Stop;
        inputLayer.FocusMode = FocusModeEnum.All;
        inputLayer.GuiInput += OnInputLayerGuiInput;
        AddChild(inputLayer);

        LayoutInteractiveChildren();
        LogDiagnostic("Ready");
        Callable.From(() => Refresh()).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            return;
        }

        if ((Size - lastObservedSize).LengthSquared() <= 0.25f)
        {
            return;
        }

        lastObservedSize = Size;
        OnViewportSizeChanged("Process");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            OnViewportSizeChanged("NotificationResized");
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (HandlePointerInput(@event, eventPositionIsGlobal: true))
        {
            GetViewport()?.SetInputAsHandled();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (HandlePointerInput(@event, eventPositionIsGlobal: false))
        {
            AcceptEvent();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } key && HandleKeyInput(key))
        {
            AcceptEvent();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (HandlePointerInput(@event, eventPositionIsGlobal: true))
        {
            GetViewport()?.SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } key && (HasFocus() || selectionState.HasSelection) && HandleKeyInput(key))
        {
            GetViewport()?.SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), BackgroundColor);
        LogDrawDiagnostic();

        if (logFont == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.FilterError))
        {
            DrawSingleLine(snapshot.FilterError, ErrorTextColor);
            return;
        }

        if (snapshot.Lines.Count == 0)
        {
            string text = snapshot.TotalCount > 0 ? NoMatchesText : EmptyText;
            DrawSingleLine(text, EmptyTextColor);
        }
    }

    public void SetModel(LogViewportModel viewportModel)
    {
        model = viewportModel;
        LogDiagnostic("SetModel");
        Refresh(forceFollowTail: true);
    }

    public void SetFormatter(LogLineFormatter lineFormatter)
    {
        formatter = lineFormatter;
        Refresh();
    }

    public void SetLogFont(Font font, int fontSize, int spacing)
    {
        logFont = font;
        logFontSize = Math.Clamp(fontSize, 8, 96);
        lineSpacing = Math.Clamp(spacing, 0, 32);
        rowHeight = MathF.Max(logFontSize + lineSpacing + 6, font.GetHeight(logFontSize) + lineSpacing + 2);
        ApplyRowFonts();
        LogDiagnostic($"SetLogFont font={font.GetType().Name} size={logFontSize} rowHeight={rowHeight:0.##}");
        Refresh();
    }

    public void Refresh(bool forceFollowTail = false)
    {
        if (model == null)
        {
            QueueRedraw();
            return;
        }

        if (forceFollowTail)
        {
            model.ScrollToEnd();
            selectionState.Clear();
        }

        horizontalOffset = 0f;
        horizontalScrollBar.Visible = false;
        viewportRows = CalculateViewportRows();
        wrapColumns = CalculateWrapColumns();
        snapshot = model.CreateSnapshot(snapshot.FirstRow, viewportRows, formatter, wrapColumns);
        scrollBarAdapter?.Update(snapshot.TotalRows, viewportRows, snapshot.FirstRow);
        UpdateHorizontalScrollRange();
        int adjustedViewportRows = CalculateViewportRows();
        int adjustedWrapColumns = CalculateWrapColumns();
        if (adjustedViewportRows != viewportRows || adjustedWrapColumns != wrapColumns)
        {
            viewportRows = adjustedViewportRows;
            wrapColumns = adjustedWrapColumns;
            snapshot = model.CreateSnapshot(snapshot.FirstRow, viewportRows, formatter, wrapColumns);
            scrollBarAdapter?.Update(snapshot.TotalRows, viewportRows, snapshot.FirstRow);
            UpdateHorizontalScrollRange();
        }

        LayoutInteractiveChildren();
        UpdateRowLabels();
        LogRefreshDiagnostic(forceFollowTail);
        SnapshotChanged?.Invoke(snapshot);
        QueueRedraw();
    }

    public void ScrollToEnd()
    {
        model?.ScrollToEnd();
        selectionState.Clear();
        Refresh();
    }

    public string BuildFilteredPlainText()
    {
        return TryBuildSelectedPlainText(out string selectedText)
            ? selectedText
            : model?.BuildPlainText(formatter, filteredOnly: true) ?? string.Empty;
    }

    public bool CopySelectionToClipboard()
    {
        if (!TryBuildSelectedPlainText(out string selectedText))
        {
            return false;
        }

        DisplayServer.ClipboardSet(selectedText);
        LogEventDiagnostic($"CopySelection length={selectedText.Length}");
        selectionState.Clear();
        UpdateRowLabels();
        return true;
    }

    private bool HandlePointerInput(InputEvent @event, bool eventPositionIsGlobal, Vector2 localOffset = default)
    {
        if (!IsVisibleInTree())
        {
            return false;
        }

        switch (@event)
        {
            case InputEventMouseButton mouseButton:
                return HandleMouseButton(mouseButton, eventPositionIsGlobal, localOffset);
            case InputEventMouseMotion mouseMotion:
                return HandleMouseMotion(mouseMotion, eventPositionIsGlobal, localOffset);
            default:
                return false;
        }
    }

    private bool HandleMouseButton(InputEventMouseButton mouseButton, bool eventPositionIsGlobal, Vector2 localOffset)
    {
        Vector2 local = GetLocalMousePosition(mouseButton.Position, eventPositionIsGlobal) + localOffset;
        bool insideTextArea = IsInsideTextArea(local);

        if (mouseButton.Pressed && insideTextArea && mouseButton.ButtonIndex == MouseButton.WheelUp)
        {
            if (mouseButton.CtrlPressed || mouseButton.MetaPressed)
            {
                FontZoomRequested?.Invoke(1);
            }
            else if (mouseButton.ShiftPressed)
            {
                ScrollHorizontally(-HorizontalWheelPixels);
            }
            else
            {
                ScrollByRows(-WheelRows);
            }

            return true;
        }

        if (mouseButton.Pressed && insideTextArea && mouseButton.ButtonIndex == MouseButton.WheelDown)
        {
            if (mouseButton.CtrlPressed || mouseButton.MetaPressed)
            {
                FontZoomRequested?.Invoke(-1);
            }
            else if (mouseButton.ShiftPressed)
            {
                ScrollHorizontally(HorizontalWheelPixels);
            }
            else
            {
                ScrollByRows(WheelRows);
            }

            return true;
        }

        if (mouseButton.Pressed && insideTextArea && mouseButton.ButtonIndex == MouseButton.Right)
        {
            return CopySelectionToClipboard();
        }

        if (mouseButton.ButtonIndex != MouseButton.Left)
        {
            return false;
        }

        if (mouseButton.Pressed)
        {
            if (!TryGetNearestTextPosition(local, out LogTextPosition position))
            {
                selectionState.Clear();
                UpdateRowLabels();
                return insideTextArea;
            }

            selectionState.Begin(position);
            LogEventDiagnostic($"SelectBegin row={position.Row} col={position.Column} local={local}");
            UpdateRowLabels();
            return true;
        }

        if (!selectionState.IsDragging)
        {
            return false;
        }

        if (TryGetNearestTextPosition(local, out LogTextPosition releasePosition))
        {
            selectionState.End(releasePosition);
            LogEventDiagnostic($"SelectEnd row={releasePosition.Row} col={releasePosition.Column} hasSelection={selectionState.HasSelection}");
        }
        else
        {
            selectionState.Clear();
            LogEventDiagnostic("SelectClear release outside text");
        }

        UpdateRowLabels();
        return true;
    }

    private bool HandleMouseMotion(InputEventMouseMotion mouseMotion, bool eventPositionIsGlobal, Vector2 localOffset)
    {
        if (!selectionState.IsDragging)
        {
            return false;
        }

        Vector2 local = GetLocalMousePosition(mouseMotion.Position, eventPositionIsGlobal) + localOffset;
        if (!TryGetNearestTextPosition(local, out LogTextPosition position))
        {
            return false;
        }

        selectionState.Update(position);
        UpdateRowLabels();
        return true;
    }

    private bool HandleKeyInput(InputEventKey key)
    {
        if ((key.CtrlPressed || key.MetaPressed) && key.Keycode == Key.C)
        {
            return CopySelectionToClipboard();
        }

        switch (key.Keycode)
        {
            case Key.Pageup:
                ScrollByRows(-viewportRows);
                return true;
            case Key.Pagedown:
                ScrollByRows(viewportRows);
                return true;
            case Key.Home:
                model?.ScrollToStart();
                selectionState.Clear();
                Refresh();
                return true;
            case Key.End:
                ScrollToEnd();
                return true;
            case Key.Up:
                ScrollByRows(-1);
                return true;
            case Key.Down:
                ScrollByRows(1);
                return true;
            case Key.Left:
                ScrollHorizontally(-HorizontalWheelPixels);
                return true;
            case Key.Right:
                ScrollHorizontally(HorizontalWheelPixels);
                return true;
            default:
                return false;
        }
    }

    private void ScrollByRows(int rows)
    {
        if (model == null)
        {
            return;
        }

        int before = snapshot.FirstRow;
        model.ScrollByRows(rows, viewportRows, formatter, wrapColumns);
        selectionState.Clear();
        Refresh();
        LogEventDiagnostic($"ScrollRows delta={rows} before={before} after={snapshot.FirstRow} totalRows={snapshot.TotalRows} viewportRows={viewportRows}");
    }

    private void OnScrollBarValueChangedByUser(int value)
    {
        if (model == null)
        {
            return;
        }

        model.SetFirstVisibleRow(value, viewportRows, formatter, wrapColumns);
        selectionState.Clear();
        Refresh();
    }

    private void OnHorizontalScrollBarValueChanged(double value)
    {
        horizontalOffset = Math.Clamp((float)value, 0f, GetMaxHorizontalOffset());
        UpdateRowLabels();
        QueueRedraw();
    }

    private void OnInputLayerGuiInput(InputEvent @event)
    {
        if (HandlePointerInput(@event, eventPositionIsGlobal: false))
        {
            inputLayer.AcceptEvent();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } key && HandleKeyInput(key))
        {
            inputLayer.AcceptEvent();
        }
    }

    private void OnRowBackgroundGuiInput(ColorRect rowBackground, InputEvent @event)
    {
        Vector2 rowOffset = rowBackground.Position;
        if (HandlePointerInput(@event, eventPositionIsGlobal: false, localOffset: rowOffset))
        {
            rowBackground.AcceptEvent();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } key && HandleKeyInput(key))
        {
            rowBackground.AcceptEvent();
        }
    }

    private void OnViewportSizeChanged(string reason)
    {
        LayoutInteractiveChildren();
        selectionState.Clear();
        Refresh();
        LogEventDiagnostic($"ViewportSizeChanged reason={reason} size={Size} textSize=({GetTextWidth():0.##},{GetTextHeight():0.##}) rows={viewportRows}");
    }

    private void ScrollHorizontally(float deltaPixels)
    {
        float next = Math.Clamp(horizontalOffset + deltaPixels, 0f, GetMaxHorizontalOffset());
        if (Math.Abs(next - horizontalOffset) < 0.5f)
        {
            return;
        }

        horizontalOffset = next;
        horizontalScrollBar.Value = horizontalOffset;
        UpdateRowLabels();
        QueueRedraw();
        LogEventDiagnostic($"ScrollHorizontal delta={deltaPixels:0.##} offset={horizontalOffset:0.##} max={GetMaxHorizontalOffset():0.##}");
    }

    private int CalculateViewportRows()
    {
        if (rowHeight <= 0f)
        {
            return 1;
        }

        float usableHeight = MathF.Max(rowHeight, GetTextHeight() - rowHeight * BottomBlankRows);
        return Math.Max(1, (int)MathF.Floor(usableHeight / rowHeight));
    }

    private int CalculateWrapColumns()
    {
        if (logFont == null)
        {
            return 120;
        }

        float charWidth = logFont.GetStringSize("M", fontSize: logFontSize).X;
        if (charWidth <= 0f)
        {
            charWidth = MathF.Max(1f, logFontSize * 0.6f);
        }

        return Math.Max(16, (int)MathF.Floor(GetContentWidth() / charWidth));
    }

    private void DrawSingleLine(string text, Color color)
    {
        if (logFont == null)
        {
            return;
        }

        float baseline = MathF.Max(logFontSize + 6, rowHeight - 4f);
        DrawString(
            logFont,
            new Vector2(HorizontalPadding, baseline),
            text,
            HorizontalAlignment.Left,
            GetTextWidth() - HorizontalPadding * 2,
            logFontSize,
            color);
    }

    private float GetTextWidth()
    {
        return scrollBar.Visible
            ? MathF.Max(0f, Size.X - ScrollBarWidth)
            : Size.X;
    }

    private float GetTextHeight()
    {
        return horizontalScrollBar.Visible
            ? MathF.Max(0f, Size.Y - HorizontalScrollBarHeight)
            : Size.Y;
    }

    private float GetContentWidth()
    {
        return MathF.Max(0f, GetTextWidth() - HorizontalPadding * 2);
    }

    private float GetMaxHorizontalOffset()
    {
        return MathF.Max(0f, maxVisibleLineWidth - GetContentWidth());
    }

    private void LayoutInteractiveChildren()
    {
        float textWidth = GetTextWidth();
        float textHeight = GetTextHeight();

        scrollBar.Position = new Vector2(MathF.Max(0f, Size.X - ScrollBarWidth), 0f);
        scrollBar.Size = new Vector2(ScrollBarWidth, textHeight);

        horizontalScrollBar.Position = new Vector2(0f, MathF.Max(0f, Size.Y - HorizontalScrollBarHeight));
        horizontalScrollBar.Size = new Vector2(textWidth, HorizontalScrollBarHeight);

        inputLayer.Position = Vector2.Zero;
        inputLayer.Size = new Vector2(textWidth, textHeight);

        KeepInteractiveChildrenOnTop();
    }

    private void EnsureRowLabels(int count)
    {
        while (rowLabels.Count < count)
        {
            var rowBackground = new ColorRect
            {
                Name = "LogLineBackground" + rowBackgrounds.Count,
                MouseFilter = MouseFilterEnum.Stop
            };
            rowBackground.GuiInput += @event => OnRowBackgroundGuiInput(rowBackground, @event);
            AddChild(rowBackground);
            rowBackgrounds.Add(rowBackground);

            var selectionBackground = new ColorRect
            {
                Name = "LogLineSelection" + selectionBackgrounds.Count,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(selectionBackground);
            selectionBackgrounds.Add(selectionBackground);

            var label = new Label
            {
                Name = "LogLine" + rowLabels.Count,
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true
            };
            label.AddThemeConstantOverride("line_spacing", 0);
            AddChild(label);
            rowLabels.Add(label);
            ApplyLabelFont(label);
        }

        KeepInteractiveChildrenOnTop();
    }

    private void UpdateRowLabels()
    {
        int desiredCount = Math.Max(viewportRows + 1, snapshot.Lines.Count);
        EnsureRowLabels(desiredCount);

        float width = GetTextWidth();
        float textHeight = GetTextHeight();
        for (int index = 0; index < rowLabels.Count; index++)
        {
            Label label = rowLabels[index];
            ColorRect rowBackground = rowBackgrounds[index];
            ColorRect selectionBackground = selectionBackgrounds[index];

            if (index >= snapshot.Lines.Count || width <= 0f || textHeight <= 0f)
            {
                label.Visible = false;
                rowBackground.Visible = false;
                selectionBackground.Visible = false;
                continue;
            }

            int absoluteRow = snapshot.FirstRow + index;
            LogRenderLine line = snapshot.Lines[index];
            float y = index * rowHeight;
            float contentWidth = GetContentWidth();
            float labelWidth = MathF.Max(contentWidth + horizontalOffset, GetTextPrefixWidth(line.Text, line.Text.Length) + HorizontalPadding);

            rowBackground.Visible = true;
            rowBackground.Position = new Vector2(0f, y);
            rowBackground.Size = new Vector2(width, rowHeight);
            rowBackground.Color = absoluteRow % 2 == 1 ? AlternateRowColor : new Color(0f, 0f, 0f, 0.001f);

            if (TryGetSelectedColumnsForRow(absoluteRow, line.Text, out int selectionStart, out int selectionEnd))
            {
                float startX = HorizontalPadding - horizontalOffset + GetTextPrefixWidth(line.Text, selectionStart);
                float endX = HorizontalPadding - horizontalOffset + GetTextPrefixWidth(line.Text, selectionEnd);
                if (selectionEnd >= line.Text.Length)
                {
                    endX = MathF.Max(endX, HorizontalPadding + contentWidth);
                }

                selectionBackground.Visible = true;
                selectionBackground.Position = new Vector2(startX, y + 1f);
                selectionBackground.Size = new Vector2(MathF.Max(2f, endX - startX), MathF.Max(1f, rowHeight - 2f));
                selectionBackground.Color = SelectionColor;
                label.Modulate = SelectedTextColor;
            }
            else
            {
                selectionBackground.Visible = false;
                label.Modulate = line.Color;
            }

            label.Visible = true;
            label.Text = line.Text;
            label.Position = new Vector2(HorizontalPadding - horizontalOffset, y);
            label.Size = new Vector2(labelWidth, rowHeight);
        }

        KeepInteractiveChildrenOnTop();
    }

    private void ApplyRowFonts()
    {
        foreach (Label label in rowLabels)
        {
            ApplyLabelFont(label);
        }
    }

    private void ApplyLabelFont(Label label)
    {
        if (logFont != null)
        {
            label.AddThemeFontOverride("font", logFont);
            label.AddThemeFontOverride("normal_font", logFont);
        }

        label.AddThemeFontSizeOverride("font_size", logFontSize);
        label.AddThemeFontSizeOverride("normal_font_size", logFontSize);
    }

    private void UpdateHorizontalScrollRange()
    {
        horizontalOffset = 0f;
        if (horizontalScrollBar.Visible)
        {
            horizontalScrollBar.Visible = false;
            LayoutInteractiveChildren();
        }

        maxVisibleLineWidth = 0f;
        foreach (LogRenderLine line in snapshot.Lines)
        {
            maxVisibleLineWidth = MathF.Max(maxVisibleLineWidth, GetTextPrefixWidth(line.Text, line.Text.Length));
        }
    }

    private void KeepInteractiveChildrenOnTop()
    {
        if (inputLayer.GetParent() == this)
        {
            MoveChild(inputLayer, 0);
        }

        if (horizontalScrollBar.GetParent() == this)
        {
            MoveChild(horizontalScrollBar, GetChildCount() - 1);
        }

        if (scrollBar.GetParent() == this)
        {
            MoveChild(scrollBar, GetChildCount() - 1);
        }
    }

    private Vector2 GetLocalMousePosition(Vector2 eventPosition, bool eventPositionIsGlobal)
    {
        if (!eventPositionIsGlobal)
        {
            return eventPosition;
        }

        Vector2 relativeToControl = eventPosition - GlobalPosition;
        if (IsInsideTextArea(relativeToControl))
        {
            return relativeToControl;
        }

        return eventPosition;
    }

    private bool IsInsideTextArea(Vector2 localPosition)
    {
        return localPosition.X >= 0f
            && localPosition.Y >= 0f
            && localPosition.X < GetTextWidth()
            && localPosition.Y < GetTextHeight();
    }

    private bool TryGetNearestTextPosition(Vector2 localPosition, out LogTextPosition position)
    {
        position = default;
        if (snapshot.Lines.Count == 0)
        {
            return false;
        }

        float renderedHeight = Math.Min(snapshot.Lines.Count, viewportRows) * rowHeight;
        if (localPosition.Y < 0f || localPosition.Y >= renderedHeight)
        {
            return false;
        }

        int visibleIndex = (int)MathF.Floor(localPosition.Y / MathF.Max(1f, rowHeight));
        visibleIndex = Math.Clamp(visibleIndex, 0, snapshot.Lines.Count - 1);

        LogRenderLine line = snapshot.Lines[visibleIndex];
        int column = GetColumnAtX(line.Text, localPosition.X - HorizontalPadding + horizontalOffset);
        position = new LogTextPosition(snapshot.FirstRow + visibleIndex, column);
        return true;
    }

    private int GetColumnAtX(string text, float x)
    {
        if (text.Length == 0 || logFont == null)
        {
            return 0;
        }

        float target = Math.Clamp(x, 0f, MathF.Max(GetContentWidth(), maxVisibleLineWidth));
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (GetTextPrefixWidth(text, middle) <= target)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (low < text.Length)
        {
            float left = GetTextPrefixWidth(text, low);
            float right = GetTextPrefixWidth(text, low + 1);
            if (target - left > right - target)
            {
                low++;
            }
        }

        return low;
    }

    private bool TryGetSelectedColumnsForRow(int absoluteRow, string text, out int startColumn, out int endColumn)
    {
        startColumn = 0;
        endColumn = 0;
        if (!selectionState.TryGetOrderedRange(out LogTextPosition start, out LogTextPosition end))
        {
            return false;
        }

        if (absoluteRow < start.Row || absoluteRow > end.Row)
        {
            return false;
        }

        if (start.Row == end.Row)
        {
            startColumn = Math.Clamp(start.Column, 0, text.Length);
            endColumn = Math.Clamp(end.Column, 0, text.Length);
        }
        else if (absoluteRow == start.Row)
        {
            startColumn = Math.Clamp(start.Column, 0, text.Length);
            endColumn = text.Length;
        }
        else if (absoluteRow == end.Row)
        {
            startColumn = 0;
            endColumn = Math.Clamp(end.Column, 0, text.Length);
        }
        else
        {
            startColumn = 0;
            endColumn = text.Length;
        }

        return endColumn > startColumn || (start.Row != end.Row && text.Length == 0);
    }

    private float GetTextPrefixWidth(string text, int charCount)
    {
        if (logFont == null || charCount <= 0 || text.Length == 0)
        {
            return 0f;
        }

        string value = charCount >= text.Length ? text : text[..charCount];
        return logFont.GetStringSize(value, fontSize: logFontSize).X;
    }

    private bool TryBuildSelectedPlainText(out string text)
    {
        text = string.Empty;
        if (model == null || !selectionState.TryGetOrderedRange(out LogTextPosition start, out LogTextPosition end))
        {
            return false;
        }

        int rowCount = end.Row - start.Row + 1;
        IReadOnlyList<LogRenderLine> lines = model.CreateRenderLinesForRows(start.Row, rowCount, formatter, wrapColumns);
        if (lines.Count == 0)
        {
            return false;
        }

        var builder = new StringBuilder();
        for (int index = 0; index < lines.Count; index++)
        {
            int absoluteRow = start.Row + index;
            string lineText = lines[index].Text;
            if (TryGetSelectedColumnsForRow(absoluteRow, lineText, out int startColumn, out int endColumn))
            {
                int safeStart = Math.Clamp(startColumn, 0, lineText.Length);
                int safeEnd = Math.Clamp(endColumn, safeStart, lineText.Length);
                builder.Append(lineText.AsSpan(safeStart, safeEnd - safeStart));
            }

            if (index < lines.Count - 1)
            {
                builder.AppendLine();
            }
        }

        text = builder.ToString();
        return text.Length > 0;
    }

    private void LogRefreshDiagnostic(bool forceFollowTail)
    {
        string key = $"refresh:{Size}:{viewportRows}:{snapshot.TotalRows}:{snapshot.Lines.Count}:{snapshot.FilterError}:{logFont != null}";
        if (string.Equals(key, lastRefreshDiagnosticKey, StringComparison.Ordinal))
        {
            return;
        }

        lastRefreshDiagnosticKey = key;
        LogDiagnostic(
            $"Refresh forceTail={forceFollowTail} size={Size} min={CustomMinimumSize} rows={viewportRows} rowHeight={rowHeight:0.##} total={snapshot.TotalCount} match={snapshot.MatchCount} totalRows={snapshot.TotalRows} first={snapshot.FirstRow} last={snapshot.LastRow} lines={snapshot.Lines.Count} follow={snapshot.FollowTail} fontReady={logFont != null} scrollVisible={scrollBar.Visible} err=\"{snapshot.FilterError ?? string.Empty}\"");
    }

    private void LogDrawDiagnostic()
    {
        string key = $"draw:{Size}:{snapshot.Lines.Count}:{snapshot.TotalRows}:{logFont != null}:{IsVisibleInTree()}";
        if (string.Equals(key, lastDrawDiagnosticKey, StringComparison.Ordinal))
        {
            return;
        }

        lastDrawDiagnosticKey = key;
        string sample = snapshot.Lines.Count > 0
            ? snapshot.Lines[0].Text[..Math.Min(80, snapshot.Lines[0].Text.Length)]
            : string.Empty;
        LogDiagnostic(
            $"Draw size={Size} visible={Visible}/{IsVisibleInTree()} lines={snapshot.Lines.Count} totalRows={snapshot.TotalRows} fontReady={logFont != null} textWidth={GetTextWidth():0.##} sample=\"{sample}\"",
            draw: true);
    }

    private void LogDiagnostic(string message, bool draw = false)
    {
        if (!LogConsoleSettings.EnableWindowDiagnostics)
        {
            return;
        }

        if (draw)
        {
            if (drawDiagnosticsRemaining <= 0)
            {
                return;
            }

            drawDiagnosticsRemaining--;
        }
        else
        {
            if (diagnosticsRemaining <= 0)
            {
                return;
            }

            diagnosticsRemaining--;
        }

        ModLogger.Info($"[LogConsole.VirtualViewDiag] {message}");
    }

    private void LogEventDiagnostic(string message)
    {
        if (!LogConsoleSettings.EnableWindowDiagnostics || eventDiagnosticsRemaining <= 0)
        {
            return;
        }

        eventDiagnosticsRemaining--;
        ModLogger.Info($"[LogConsole.VirtualViewEvent] {message}");
    }
}
