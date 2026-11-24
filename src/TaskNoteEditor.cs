using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Wpf = System.Windows;
using Media = System.Windows.Media;
using Input = System.Windows.Input;
using Documents = System.Windows.Documents;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace Deskmate
{
    sealed class TaskNoteEditor : ElementHost
    {
        readonly RichTextBox editor;
        readonly System.Windows.Controls.TextBlock placeholder = new System.Windows.Controls.TextBlock
        {
            IsHitTestVisible = false, Margin = new Wpf.Thickness(14, 8, 14, 0),
            VerticalAlignment = Wpf.VerticalAlignment.Top, FontSize = 14
        };
        string initialMarkdown;
        internal Action OpenBlockMenu, SaveTask, CancelTask;
        // The host's ribbon owns the insert buttons; these report back to it
        internal Action<bool> TableFocusChanged;
        internal Action<string> Notice;

        public TaskNoteEditor()
        {
            AccessibleName = Lang.T("Catatan");
            var document = new Documents.FlowDocument
            {
                FontFamily = new Media.FontFamily("Segoe UI"),
                FontSize = 14,
                PagePadding = new Wpf.Thickness(14, 8, 14, 14)
            };
            var paragraphStyle = new Wpf.Style(typeof(Documents.Paragraph));
            paragraphStyle.Setters.Add(new Wpf.Setter(Documents.Paragraph.MarginProperty, new Wpf.Thickness(0, 0, 0, 6)));
            document.Resources.Add(typeof(Documents.Paragraph), paragraphStyle);
            editor = new RichTextBox(document)
            {
                BorderThickness = new Wpf.Thickness(0),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                IsUndoEnabled = true,
                AllowDrop = false,
                AcceptsTab = false
            };
            Wpf.Automation.AutomationProperties.SetName(editor, AccessibleName);
            editor.PreviewTextInput += delegate(object sender, Input.TextCompositionEventArgs e)
            {
                var paragraph = editor.CaretPosition.Paragraph;
                if (e.Text == "/" && editor.Selection.IsEmpty && paragraph != null && FindCell() == null
                    && TaskNoteMarkdown.Plain(paragraph).Length == 0 && OpenBlockMenu != null)
                {
                    e.Handled = true;
                    BeginInvoke(OpenBlockMenu);
                }
            };
            editor.PreviewKeyDown += HandleKeys;
            editor.PreviewMouseLeftButtonDown += ToggleChecklist;
            Wpf.DataObject.AddPastingHandler(editor, Paste);
            BuildTableTools();
        }

        internal void SetTheme(Color background, Color text, Color muted, Color header)
        {
            BackColor = background;
            editor.Background = Brush(background);
            editor.Foreground = Brush(text);
            editor.CaretBrush = Brush(text);
            editor.Document.Resources["NoteBorder"] = Brush(Ink.Mix(background, text, 0.22f));
            editor.Document.Resources["NoteMuted"] = Brush(muted);
            editor.Document.Resources["NoteHeader"] = Brush(header);
            ((Wpf.FrameworkElement)Child).Resources["EditorSurface"] = Brush(background);
            ((Wpf.FrameworkElement)Child).Resources["EditorText"] = Brush(text);
            ((Wpf.FrameworkElement)Child).Resources["EditorHover"] = Brush(header);
            ((Wpf.FrameworkElement)Child).Resources["EditorBorder"] = Brush(Ink.Mix(background, text, 0.30f));
            tableTools.Background = Brush(background);
            tableHint.Foreground = placeholder.Foreground = Brush(muted);
            tableError.Foreground = Brush(Ink.Base.Negative);
        }

        static Media.Brush Brush(Color color)
        {
            var brush = new Media.SolidColorBrush(Media.Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        internal void LoadMarkdown(string source)
        {
            if (initialMarkdown != null) return;
            TaskNoteMarkdown.Load(editor.Document, source);
            UpdateDocumentWidth();
            initialMarkdown = TaskNoteMarkdown.Save(editor.Document);
            editor.CaretPosition = editor.Document.ContentStart;
            editor.IsUndoEnabled = false;
            editor.IsUndoEnabled = true;
        }

        internal string SaveMarkdown(string original)
        {
            string markdown = TaskNoteMarkdown.Save(editor.Document);
            return markdown == initialMarkdown ? original : markdown;
        }

        internal void FocusEditor() { Focus(); editor.Focus(); }

        internal Point CaretLocation()
        {
            Wpf.Rect rect = editor.CaretPosition.GetCharacterRect(Documents.LogicalDirection.Forward);
            return rect.IsEmpty ? new Point(16, 24) : PointToClient(
                ToDrawingPoint(editor.PointToScreen(rect.BottomLeft)));
        }

        static Point ToDrawingPoint(Wpf.Point point) { return new Point((int)point.X, (int)point.Y); }

        internal void ToggleBold() { Format(Documents.EditingCommands.ToggleBold); }
        internal void ToggleItalic() { Format(Documents.EditingCommands.ToggleItalic); }

        void Format(Input.RoutedUICommand command)
        {
            FocusEditor();
            if (command.CanExecute(null, editor)) command.Execute(null, editor);
        }

        internal void ApplyBlock(string prefix)
        {
            if (prefix == "|") { ShowTablePicker(); return; }
            FocusEditor();
            var paragraph = editor.CaretPosition.Paragraph;
            if (paragraph == null || FindCell() != null) return;
            editor.BeginChange();
            try
            {
                ReplaceStyle(paragraph, prefix.TrimEnd());
                if (prefix == TaskNoteMarkdown.Divider && TaskNoteMarkdown.Plain(paragraph).Length == 0)
                {
                    var next = new Documents.Paragraph { Tag = "" };
                    editor.Document.Blocks.InsertAfter(paragraph, next);
                    editor.CaretPosition = next.ContentStart;
                }
                else MoveAfterMarker(paragraph);
            }
            finally { editor.EndChange(); }
        }

        static void ReplaceStyle(Documents.Paragraph paragraph, string prefix)
        {
            string oldMarker = TaskNoteMarkdown.Marker(paragraph.Tag as string ?? "");
            if (oldMarker.Length > 0 && TaskNoteMarkdown.Plain(paragraph).StartsWith(oldMarker, StringComparison.Ordinal))
            {
                var start = paragraph.ContentStart.GetInsertionPosition(Documents.LogicalDirection.Forward);
                var end = start.GetPositionAtOffset(oldMarker.Length);
                if (end != null) new Documents.TextRange(start, end).Text = "";
            }
            TaskNoteMarkdown.Style(paragraph, prefix);
        }

        void MoveAfterMarker(Documents.Paragraph paragraph)
        {
            string glyph = TaskNoteMarkdown.Marker(paragraph.Tag as string ?? "");
            var first = paragraph.ContentStart.GetInsertionPosition(Documents.LogicalDirection.Forward);
            editor.CaretPosition = first.GetPositionAtOffset(glyph.Length) ?? first;
        }

        void ToggleChecklist(object sender, Input.MouseButtonEventArgs e)
        {
            var position = editor.GetPositionFromPoint(e.GetPosition(editor), false);
            var paragraph = position == null ? null : position.Paragraph;
            var marker = paragraph == null ? null : paragraph.Inlines.FirstInline as Documents.Run;
            if (marker == null || marker.Text.Length == 0 || "☐☑".IndexOf(marker.Text[0]) < 0
                || position.CompareTo(marker.ContentEnd) >= 0) return;
            string prefix = paragraph.Tag as string ?? "";
            if (TaskNoteMarkdown.Marker(prefix).Length == 0) return;
            e.Handled = true;
            editor.BeginChange();
            try { ReplaceStyle(paragraph, marker.Text[0] == '☐' ? "- [x]" : "- [ ]"); }
            finally { editor.EndChange(); }
        }

        void HandleKeys(object sender, Input.KeyEventArgs e)
        {
            var modifiers = Input.Keyboard.Modifiers;
            if (e.Key == Input.Key.Enter && modifiers == Input.ModifierKeys.Control)
            {
                e.Handled = true;
                if (SaveTask != null) SaveTask();
                return;
            }
            if (e.Key == Input.Key.Escape && modifiers == Input.ModifierKeys.None)
            {
                e.Handled = true;
                if (CancelTask != null) CancelTask();
                return;
            }
            var cell = FindCell();
            if (cell != null && (modifiers == Input.ModifierKeys.None || modifiers == Input.ModifierKeys.Shift)
                && (e.Key == Input.Key.Tab || e.Key == Input.Key.Enter))
            {
                e.Handled = true;
                MoveCell(cell, modifiers == Input.ModifierKeys.Shift);
                return;
            }
            if (e.Key != Input.Key.Enter || modifiers != Input.ModifierKeys.None || !editor.Selection.IsEmpty) return;
            var paragraph = editor.CaretPosition.Paragraph;
            if (paragraph == null) return;
            var prefix = paragraph.Tag as string ?? "";
            if (prefix.Length == 0 || editor.CaretPosition.CompareTo(
                paragraph.ContentEnd.GetInsertionPosition(Documents.LogicalDirection.Backward)) < 0) return;
            e.Handled = true;
            editor.BeginChange();
            try
            {
                if (prefix.StartsWith(TaskNoteMarkdown.Code, StringComparison.Ordinal))
                {
                    string body = TaskNoteMarkdown.Plain(paragraph);
                    if (body.EndsWith("\n", StringComparison.Ordinal))
                    {
                        var caret = editor.CaretPosition;
                        var end = caret.GetNextInsertionPosition(Documents.LogicalDirection.Backward);
                        if (end != null) new Documents.TextRange(end, caret).Text = "";
                        var afterCode = new Documents.Paragraph { Tag = "" };
                        editor.Document.Blocks.InsertAfter(paragraph, afterCode);
                        editor.CaretPosition = afterCode.ContentStart;
                        return;
                    }
                    editor.CaretPosition = editor.CaretPosition.InsertLineBreak();
                    return;
                }
                string glyph = TaskNoteMarkdown.Marker(prefix);
                if (glyph.Length > 0 && TaskNoteMarkdown.Plain(paragraph).TrimEnd() == glyph.TrimEnd())
                {
                    ReplaceStyle(paragraph, "");
                    return;
                }
                var next = new Documents.Paragraph();
                if (prefix.EndsWith("[x]", StringComparison.OrdinalIgnoreCase)) prefix = "- [ ]";
                int number;
                if (prefix.Length > 1 && int.TryParse(prefix.Substring(0, prefix.Length - 1), out number) && number < int.MaxValue)
                    prefix = (number + 1).ToString() + prefix[prefix.Length - 1];
                TaskNoteMarkdown.Style(next, glyph.Length > 0 ? prefix : "");
                editor.Document.Blocks.InsertAfter(paragraph, next);
                MoveAfterMarker(next);
            }
            finally { editor.EndChange(); }
        }


        readonly System.Windows.Controls.WrapPanel tableTools = new System.Windows.Controls.WrapPanel();
        readonly System.Windows.Controls.TextBlock tableHint = new System.Windows.Controls.TextBlock
        {
            TextWrapping = Wpf.TextWrapping.Wrap, VerticalAlignment = Wpf.VerticalAlignment.Center
        };
        readonly System.Windows.Controls.Button manageTable = new System.Windows.Controls.Button();
        readonly System.Windows.Controls.WrapPanel tableSize = new System.Windows.Controls.WrapPanel { Visibility = Wpf.Visibility.Collapsed };
        readonly System.Windows.Controls.TextBox rowCount = new System.Windows.Controls.TextBox { Text = "3" };
        readonly System.Windows.Controls.TextBox columnCount = new System.Windows.Controls.TextBox { Text = "2" };
        readonly System.Windows.Controls.TextBlock tableError = new System.Windows.Controls.TextBlock
        {
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };

        void BuildTableTools()
        {
            var root = new System.Windows.Controls.DockPanel();
            var toolbar = new System.Windows.Controls.StackPanel { Margin = new Wpf.Thickness(12, 10, 12, 0) };
            toolbar.Children.Add(tableTools);
            toolbar.Children.Add(tableSize);
            System.Windows.Controls.DockPanel.SetDock(toolbar, System.Windows.Controls.Dock.Top);
            root.Children.Add(toolbar);
            var body = new System.Windows.Controls.Grid();
            body.Children.Add(editor);
            placeholder.Text = Lang.English ? "Write task notes…" : "Tulis catatan tugas…";
            body.Children.Add(placeholder);
            root.Children.Add(body);
            editor.TextChanged += delegate { UpdatePlaceholder(); };
            UpdatePlaceholder();
            Child = root;
            root.Resources.Add(typeof(System.Windows.Controls.Button),
                (Wpf.Style)Wpf.Markup.XamlReader.Parse(
                @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
                    <Setter Property='Foreground' Value='{DynamicResource EditorText}'/>
                    <Setter Property='Background' Value='{DynamicResource EditorSurface}'/>
                    <Setter Property='BorderBrush' Value='{DynamicResource EditorBorder}'/>
                    <Setter Property='BorderThickness' Value='1'/>
                    <Setter Property='FontSize' Value='13'/>
                    <Setter Property='MinHeight' Value='28'/>
                    <Setter Property='Cursor' Value='Hand'/>
                    <Setter Property='Template'><Setter.Value>
                        <ControlTemplate TargetType='Button'>
                            <Border x:Name='Surface' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                                Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                                BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='5' Padding='{TemplateBinding Padding}'>
                                <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='{DynamicResource EditorHover}'/></Trigger>
                                <Trigger Property='IsPressed' Value='True'><Setter Property='Opacity' Value='0.75'/></Trigger>
                                <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='{DynamicResource EditorText}'/></Trigger>
                                <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value></Setter>
                </Style>"));
            tableHint.Margin = new Wpf.Thickness(4, 0, 0, 6);
            tableHint.FontSize = 12;
            SetupButton(manageTable, "Atur tabel ▾", ShowTableMenu);
            // Insert tools live in the host ribbon; only the table size row shows here
            tableTools.Visibility = Wpf.Visibility.Collapsed;
            tableTools.Children.Add(tableHint);

            AddSizeField("Baris", rowCount);
            AddSizeField("Kolom", columnCount);
            var create = new System.Windows.Controls.Button();
            SetupButton(create, "Buat", delegate { CreateTable(); });
            var cancel = new System.Windows.Controls.Button();
            SetupButton(cancel, "Batal", CloseTablePicker);
            tableSize.Children.Add(create);
            tableSize.Children.Add(cancel);
            tableSize.Children.Add(tableError);
            tableSize.PreviewKeyDown += delegate(object sender, Input.KeyEventArgs e)
            {
                if (e.Key == Input.Key.Escape) { e.Handled = true; CloseTablePicker(); }
                else if (e.Key == Input.Key.Enter && Input.Keyboard.Modifiers == Input.ModifierKeys.None
                    && e.OriginalSource is System.Windows.Controls.TextBox)
                { e.Handled = true; CreateTable(); }
            };
            editor.SelectionChanged += delegate { UpdateTableTools(); };
            editor.TextChanged += delegate(object sender, System.Windows.Controls.TextChangedEventArgs e)
            {
                UpdateTableTools();
                if (e.UndoAction == System.Windows.Controls.UndoAction.Undo
                    || e.UndoAction == System.Windows.Controls.UndoAction.Redo) UpdateDocumentWidth();
            };
            UpdateTableTools();
        }

        void UpdatePlaceholder()
        {
            var paragraph = editor.Document.Blocks.FirstBlock as Documents.Paragraph;
            var inline = paragraph == null ? null : paragraph.Inlines.FirstInline;
            var run = inline as Documents.Run;
            bool empty = editor.Document.Blocks.Count == 0 || (paragraph != null && paragraph.NextBlock == null &&
                (inline == null || (run != null && run.NextInline == null && run.Text.Length == 0)));
            placeholder.Visibility = empty ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        }

        static void SetupButton(System.Windows.Controls.Button button, string label, Action click)
        {
            button.Content = Lang.T(label);
            button.Margin = new Wpf.Thickness(0, 0, 6, 6);
            button.Padding = new Wpf.Thickness(10, 4, 10, 4);
            button.Click += delegate { click(); };
        }

        void AddSizeField(string label, System.Windows.Controls.TextBox field)
        {
            var caption = new System.Windows.Controls.Label
            {
                Content = Lang.T(label), Target = field, VerticalAlignment = Wpf.VerticalAlignment.Center
            };
            caption.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "EditorText");
            field.Width = 36;
            field.MaxLength = 2;
            field.VerticalContentAlignment = Wpf.VerticalAlignment.Center;
            field.Margin = new Wpf.Thickness(0, 0, 6, 6);
            Wpf.Automation.AutomationProperties.SetName(field, Lang.T(label));
            tableSize.Children.Add(caption);
            tableSize.Children.Add(field);
        }

        void UpdateDocumentWidth()
        {
            const double minimumCellWidth = 76;
            int columns = 0;
            foreach (Documents.Block block in editor.Document.Blocks)
            {
                var table = block as Documents.Table;
                if (table != null) columns = Math.Max(columns, table.Columns.Count);
            }
            double minWidth = columns == 0 ? 0
                : columns * minimumCellWidth + editor.Document.PagePadding.Left + editor.Document.PagePadding.Right;
            if (editor.Document.MinPageWidth != minWidth) editor.Document.MinPageWidth = minWidth;
        }

        void UpdateTableTools()
        {
            var cell = FindCell();
            bool inTable = cell != null;
            if (manageTable.IsEnabled != inTable || TableFocusChanged == null)
            {
                manageTable.IsEnabled = inTable;
                if (TableFocusChanged != null) TableFocusChanged(inTable);
            }
            tableHint.Text = Lang.T(cell == null ? "Klik sel untuk mengatur tabel"
                : "Tab pindah sel · Enter di sel terakhir keluar dari tabel");
        }

        void ShowTablePicker()
        {
            tableError.Text = "";
            tableSize.Visibility = Wpf.Visibility.Visible;
            rowCount.Focus();
            rowCount.SelectAll();
        }

        void CloseTablePicker()
        {
            tableSize.Visibility = Wpf.Visibility.Collapsed;
            FocusEditor();
        }

        bool CreateTable()
        {
            int rows, columns;
            if (!int.TryParse(rowCount.Text, out rows) || rows < 1 || rows > 20)
            { tableError.Text = Lang.T("Baris harus 1–20"); rowCount.Focus(); return false; }
            if (!int.TryParse(columnCount.Text, out columns) || columns < 1 || columns > 20)
            { tableError.Text = Lang.T("Kolom harus 1–20"); columnCount.Focus(); return false; }
            CloseTablePicker();
            InsertTable(rows, columns);
            return true;
        }

        void InsertTable(int rows, int columns)
        {
            FocusEditor();
            var cell = FindCell();
            Documents.Block anchor = editor.CaretPosition.Paragraph ?? editor.Document.Blocks.LastBlock as Documents.Paragraph;
            if (cell != null) anchor = (Documents.Table)((Documents.TableRowGroup)((Documents.TableRow)cell.Parent).Parent).Parent;
            if (anchor == null) return;
            editor.BeginChange();
            try {
                var table = TaskNoteMarkdown.NewTable(columns);
                for (int i = 0; i < rows; i++) TaskNoteMarkdown.AddRow(table, new string[columns], i == 0);
                if (cell == null && TaskNoteMarkdown.Plain((Documents.Paragraph)anchor).Length == 0
                    && string.IsNullOrEmpty(anchor.Tag as string))
                    editor.Document.Blocks.InsertBefore(anchor, table);
                else editor.Document.Blocks.InsertAfter(anchor, table);
                if (table.NextBlock == null) editor.Document.Blocks.InsertAfter(table, new Documents.Paragraph { Tag = "" });
                editor.CaretPosition = table.RowGroups[0].Rows[0].Cells[0].ContentStart.GetInsertionPosition(Documents.LogicalDirection.Forward);
            }
            finally { editor.EndChange(); UpdateDocumentWidth(); UpdateTableTools(); }
        }

        void ShowTableMenu()
        {
            var cell = FindCell();
            if (cell == null) return;
            var menu = new ContextMenuStrip { ShowImageMargin = false };
            MenuLook.Apply(menu);
            string[] labels = { "Sisipkan baris di atas", "Sisipkan baris di bawah", "Sisipkan kolom di kiri", "Sisipkan kolom di kanan",
                "Tulis setelah tabel", "Hapus baris", "Hapus kolom", "Hapus tabel" };
            string[] commands = { "row", "row", "column", "column", "exit", "deleteRow", "deleteColumn", "deleteTable" };
            for (int i = 0; i < labels.Length; i++)
            {
                if (i == 5) menu.Items.Add(new ToolStripSeparator());
                string command = commands[i];
                int offset = i == 1 || i == 3 ? 1 : 0;
                menu.Items.Add(Lang.T(labels[i]), null, delegate { ChangeTable(command, offset, cell); });
            }
            menu.Closed += delegate
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(menu.Dispose));
                else menu.Dispose();
            };
            if (tableMenuAnchor != null) menu.Show(tableMenuAnchor, new Point(0, tableMenuAnchor.Height + 3));
            else menu.Show(this, PointToClient(ToDrawingPoint(manageTable.PointToScreen(new Wpf.Point(0, manageTable.ActualHeight + 2)))));
        }

        Control tableMenuAnchor;
        internal void PickImage() { ChooseImage(); }
        internal void PickTable() { ShowTablePicker(); }
        internal void OpenTableMenu(Control anchor) { tableMenuAnchor = anchor; try { ShowTableMenu(); } finally { tableMenuAnchor = null; } }

        void ImageFailed()
        {
            string text = Lang.T("Gambar tidak valid atau melebihi 4 MB / 16 megapiksel");
            tableHint.Text = text;
            if (Notice != null) Notice(text);
        }

        internal void ChangeTable(string command, int offset, Documents.TableCell target = null)
        {
            var cell = target ?? FindCell();
            if (cell == null || !(cell.Parent is Documents.TableRow)) return;
            var row = (Documents.TableRow)cell.Parent;
            var group = (Documents.TableRowGroup)row.Parent;
            var table = (Documents.Table)group.Parent;
            if (!editor.Document.Blocks.Contains(table)) return;
            if (command == "exit")
            {
                FocusEditor();
                MoveOutsideTable(table, false);
                return;
            }
            int r = group.Rows.IndexOf(row), c = row.Cells.IndexOf(cell);
            bool delete = command.StartsWith("delete", StringComparison.Ordinal);
            bool whole = command == "deleteTable" || (command == "deleteRow" && group.Rows.Count == 1)
                || (command == "deleteColumn" && table.Columns.Count == 1);
            bool populated = false;
            if (delete) foreach (Documents.TableRow candidate in group.Rows)
                foreach (Documents.TableCell value in candidate.Cells)
                    if ((whole || (command == "deleteRow" && candidate == row) || (command == "deleteColumn" && candidate.Cells.IndexOf(value) == c))
                        && new Documents.TextRange(value.ContentStart, value.ContentEnd).Text.Trim().Length > 0) populated = true;
            if (populated && !KitAsk.Confirm(FindForm(), Lang.T("Bagian ini berisi data. Hapus dari catatan?"),
                "Hapus", "Batal", Lang.T("Hapus bagian tabel"))) return;
            FocusEditor();
            editor.BeginChange();
            try {
                if (whole) {
                    var next = table.NextBlock;
                    if (next == null) { next = new Documents.Paragraph(); editor.Document.Blocks.InsertAfter(table, next); }
                    editor.CaretPosition = next.ContentStart.GetInsertionPosition(Documents.LogicalDirection.Forward);
                    editor.Document.Blocks.Remove(table);
                    return;
                }
                if (command == "row") {
                    TaskNoteMarkdown.AddRow(table, new string[table.Columns.Count], false);
                    var added = group.Rows[group.Rows.Count - 1];
                    group.Rows.Remove(added);
                    group.Rows.Insert(r + offset, added);
                    r += offset;
                }
                else if (command == "column") {
                    table.Columns.Add(new Documents.TableColumn());
                    foreach (Documents.TableRow candidate in group.Rows) {
                        var added = new Documents.TableCell(new Documents.Paragraph()) { Padding = new Wpf.Thickness(8, 5, 8, 5), BorderThickness = new Wpf.Thickness(1, 1, 0, 0) };
                        added.SetResourceReference(Documents.TableCell.BorderBrushProperty, "NoteBorder");
                        candidate.Cells.Insert(c + offset, added);
                    }
                    var separators = new System.Collections.Generic.List<string>(table.Tag as string[] ?? DefaultSeparators(table.Columns.Count - 1));
                    separators.Insert(c + offset, "---");
                    table.Tag = separators.ToArray();
                    c += offset;
                }
                else if (command == "deleteRow") { group.Rows.Remove(row); r = Math.Min(r, group.Rows.Count - 1); }
                else if (command == "deleteColumn") {
                    foreach (Documents.TableRow candidate in group.Rows) candidate.Cells.RemoveAt(c);
                    table.Columns.RemoveAt(c);
                    var separators = new System.Collections.Generic.List<string>(table.Tag as string[] ?? DefaultSeparators(table.Columns.Count + 1));
                    separators.RemoveAt(c);
                    table.Tag = separators.ToArray();
                    c = Math.Min(c, table.Columns.Count - 1);
                }
                foreach (Documents.TableRow candidate in group.Rows)
                    foreach (Documents.TableCell value in candidate.Cells)
                        if (group.Rows.IndexOf(candidate) == 0) value.SetResourceReference(Documents.TableCell.BackgroundProperty, "NoteHeader");
                        else value.ClearValue(Documents.TableCell.BackgroundProperty);
                editor.CaretPosition = group.Rows[r].Cells[c].ContentStart.GetInsertionPosition(Documents.LogicalDirection.Forward);
            }
            finally { editor.EndChange(); UpdateDocumentWidth(); UpdateTableTools(); }
        }

        static string[] DefaultSeparators(int count)
        {
            var values = new string[count];
            for (int i = 0; i < count; i++) values[i] = "---";
            return values;
        }

        Documents.TableCell FindCell()
        {
            for (var node = editor.CaretPosition.Parent as Wpf.FrameworkContentElement; node != null;
                node = node.Parent as Wpf.FrameworkContentElement)
            {
                var cell = node as Documents.TableCell;
                if (cell != null) return cell;
            }
            return null;
        }

        void MoveCell(Documents.TableCell cell, bool backward)
        {
            var row = (Documents.TableRow)cell.Parent;
            var group = (Documents.TableRowGroup)row.Parent;
            var table = (Documents.Table)group.Parent;
            int col = row.Cells.IndexOf(cell) + (backward ? -1 : 1);
            int rowIndex = group.Rows.IndexOf(row);
            if (col < 0 && rowIndex == 0) { MoveOutsideTable(table, true); return; }
            if (col < 0) { row = group.Rows[rowIndex - 1]; col = row.Cells.Count - 1; }
            else if (col >= row.Cells.Count)
            {
                if (rowIndex + 1 == group.Rows.Count) { MoveOutsideTable(table, false); return; }
                row = group.Rows[rowIndex + 1];
                col = 0;
            }
            editor.CaretPosition = row.Cells[col].ContentStart.GetInsertionPosition(Documents.LogicalDirection.Forward);
            UpdateTableTools();
        }

        void MoveOutsideTable(Documents.Table table, bool before)
        {
            Documents.Block adjacent = before ? table.PreviousBlock : table.NextBlock;
            var paragraph = adjacent as Documents.Paragraph;
            if (paragraph == null)
            {
                paragraph = new Documents.Paragraph { Tag = "" };
                if (before) editor.Document.Blocks.InsertBefore(table, paragraph);
                else editor.Document.Blocks.InsertAfter(table, paragraph);
            }
            editor.CaretPosition = before
                ? paragraph.ContentEnd.GetInsertionPosition(Documents.LogicalDirection.Backward)
                : paragraph.ContentStart.GetInsertionPosition(Documents.LogicalDirection.Forward);
            paragraph.BringIntoView();
            UpdateTableTools();
        }

        void ChooseImage()
        {
            using (var dialog = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", Title = Lang.T("Tambah gambar") })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    if (new System.IO.FileInfo(dialog.FileName).Length > TaskNoteImages.MaxBytes)
                        throw new System.IO.InvalidDataException("Image must be at most 4 MB.");
                    InsertImage(TaskNoteImages.Decode(System.IO.File.ReadAllBytes(dialog.FileName)));
                }
                catch (Exception) { ImageFailed(); }
            }
        }

        internal void InsertImage(System.Windows.Media.Imaging.BitmapSource source)
        {
            string token = TaskNoteImages.Import(source);
            FocusEditor();
            editor.BeginChange();
            try
            {
                editor.Selection.Text = "";
                var position = editor.CaretPosition.GetInsertionPosition(Documents.LogicalDirection.Forward);
                if (position.Paragraph == null) position = position.InsertParagraphBreak();
                var image = TaskNoteImages.Inline(token, position);
                editor.CaretPosition = image.ElementEnd.GetInsertionPosition(Documents.LogicalDirection.Forward);
            }
            finally { editor.EndChange(); }
        }

        void Paste(object sender, Wpf.DataObjectPastingEventArgs e)
        {
            e.CancelCommand();
            if (e.DataObject.GetDataPresent(Wpf.DataFormats.Bitmap))
            {
                try { InsertImage(e.DataObject.GetData(Wpf.DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource); }
                catch (Exception) { ImageFailed(); }
                return;
            }
            string value = e.DataObject.GetData(Wpf.DataFormats.UnicodeText) as string;
            if (string.IsNullOrEmpty(value)) return;
            var paragraph = editor.CaretPosition.Paragraph;
            if (FindCell() == null && editor.Selection.IsEmpty && paragraph != null
                && editor.Document.Blocks.Contains(paragraph) && TaskNoteMarkdown.Plain(paragraph).Length == 0
                && value.IndexOf('\n') >= 0)
            {
                var fragment = new Documents.FlowDocument();
                TaskNoteMarkdown.Load(fragment, value);
                editor.BeginChange();
                try
                {
                    Documents.Block last = null;
                    while (fragment.Blocks.FirstBlock != null)
                    {
                        last = fragment.Blocks.FirstBlock;
                        fragment.Blocks.Remove(last);
                        editor.Document.Blocks.InsertBefore(paragraph, last);
                    }
                    if (last is Documents.Paragraph)
                    {
                        editor.Document.Blocks.Remove(paragraph);
                        editor.CaretPosition = last.ContentEnd.GetInsertionPosition(Documents.LogicalDirection.Backward);
                    }
                }
                finally { editor.EndChange(); UpdateDocumentWidth(); }
                return;
            }
            editor.Selection.Text = FindCell() == null ? value : value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
