using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Deskmate
{
    static class TaskNoteMarkdown
    {
        internal const string Code = "```", Divider = "---";
        internal static readonly FontFamily Mono = new FontFamily("Consolas");
        static readonly Regex Inline = new Regex(@"(`[^\x60]+\x60|\*\*\*[^*]+\*\*\*|\*\*[^*]+\*\*|\*[^*]+\*|__[^_]+__|(?<!\w)_[^_]+_(?!\w)|\[[^\]]+\]\([^)]+\))");
        static readonly Regex TableSeparator = new Regex(@"^:?-{3,}:?$");
        static readonly Regex Rule = new Regex(@"^([-*_])(?:[ \t]*\1){2,}[ \t]*$");

        internal static void Load(FlowDocument document, string source)
        {
            document.Blocks.Clear();
            string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith(Code, StringComparison.Ordinal))
                {
                    int opening = i;
                    while (i + 1 < lines.Length && !lines[i + 1].TrimStart().StartsWith(Code, StringComparison.Ordinal)) i++;
                    var block = new Paragraph(new Run(string.Join("\n", lines, opening + 1, i - opening)));
                    Style(block, lines[opening].TrimStart());
                    document.Blocks.Add(block);
                    if (i + 1 < lines.Length) i++;
                    continue;
                }
                string[] cells, separators;
                if (TryTable(lines, i, out cells, out separators))
                {
                    Table table = NewTable(cells.Length);
                    table.Tag = separators;
                    AddRow(table, cells, true);
                    i += 2;
                    while (i < lines.Length && lines[i].Contains("|"))
                    {
                        string[] row = SplitRow(lines[i]);
                        if (row.Length != cells.Length) break;
                        AddRow(table, row, false);
                        i++;
                    }
                    i--;
                    document.Blocks.Add(table);
                    continue;
                }
                var paragraph = new Paragraph();
                Match blockPrefix = NoteEditor.Block.Match(line);
                string prefix = "";
                if (Rule.IsMatch(line.Trim())) { prefix = Divider; line = ""; }
                else if (blockPrefix.Success && blockPrefix.Groups[1].Length == 0)
                {
                    prefix = blockPrefix.Groups[2].Value;
                    line = line.Substring(blockPrefix.Length);
                }
                Style(paragraph, prefix);
                AddInline(paragraph, line);
                document.Blocks.Add(paragraph);
            }
            if (!(document.Blocks.LastBlock is Paragraph))
            {
                // A trailing editable paragraph lets the user continue after a table.
                document.Blocks.Add(new Paragraph { Tag = "" });
            }
        }

        internal static void Style(Paragraph paragraph, string prefix)
        {
            paragraph.Tag = prefix;
            paragraph.ClearValue(Paragraph.FontSizeProperty);
            paragraph.ClearValue(Paragraph.FontFamilyProperty);
            paragraph.BorderThickness = new Thickness(0);
            paragraph.Padding = new Thickness(0);
            if (prefix.StartsWith("#", StringComparison.Ordinal))
                paragraph.FontSize = prefix.Length == 1 ? 25 : prefix.Length == 2 ? 21 : 17;
            else if (prefix.StartsWith(Code, StringComparison.Ordinal))
                paragraph.FontFamily = Mono;
            else if (prefix == ">")
            {
                paragraph.SetResourceReference(Paragraph.BorderBrushProperty, "NoteBorder");
                paragraph.BorderThickness = new Thickness(2, 0, 0, 0);
                paragraph.Padding = new Thickness(10, 0, 0, 0);
            }
            else if (prefix == Divider)
            {
                paragraph.SetResourceReference(Paragraph.BorderBrushProperty, "NoteBorder");
                paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
            }
            string marker = Marker(prefix);
            if (marker.Length > 0)
            {
                var glyph = new Run(marker);
                glyph.SetResourceReference(Run.ForegroundProperty, "NoteMuted");
                if (paragraph.Inlines.FirstInline == null) paragraph.Inlines.Add(glyph);
                else paragraph.Inlines.InsertBefore(paragraph.Inlines.FirstInline, glyph);
            }
        }

        internal static string Marker(string prefix)
        {
            if (prefix.EndsWith("[ ]", StringComparison.Ordinal)) return "\u2610  ";
            if (prefix.EndsWith("[x]", StringComparison.OrdinalIgnoreCase)) return "\u2611  ";
            if (prefix == "-" || prefix == "+" || prefix == "*") return "\u2022  ";
            return prefix.Length > 0 && char.IsDigit(prefix[0]) ? prefix + "  " : "";
        }

        static void AddInline(Paragraph paragraph, string value)
        {
            int offset = 0;
            foreach (Match match in Inline.Matches(value))
            {
                if (match.Index > offset) paragraph.Inlines.Add(new Run(value.Substring(offset, match.Index - offset)));
                string token = match.Value;
                if (token[0] == '[')
                {
                    int end = token.IndexOf(']');
                    // Keep the destination as document data. The editing surface never navigates.
                    var link = new Span(new Run(token.Substring(1, end - 1))) { Tag = token.Substring(end + 2, token.Length - end - 3) };
                    link.TextDecorations = TextDecorations.Underline;
                    paragraph.Inlines.Add(link);
                }
                else
                {
                    int count = 1;
                    while (token[count] == token[0]) count++;
                    var run = new Run(token.Substring(count, token.Length - 2 * count));
                    if (token[0] == '`') run.FontFamily = Mono;
                    else
                    {
                        if (count >= 2) run.FontWeight = FontWeights.Bold;
                        if (count != 2) run.FontStyle = FontStyles.Italic;
                    }
                    paragraph.Inlines.Add(run);
                }
                offset = match.Index + match.Length;
            }
            if (offset < value.Length) paragraph.Inlines.Add(new Run(value.Substring(offset)));
        }

        static bool TryTable(string[] lines, int index, out string[] cells, out string[] separators)
        {
            cells = separators = null;
            if (index + 1 >= lines.Length || !lines[index].Contains("|")) return false;
            cells = SplitRow(lines[index]);
            separators = SplitRow(lines[index + 1]);
            return cells.Length == separators.Length && separators.All(x => TableSeparator.IsMatch(x));
        }

        static string[] SplitRow(string line)
        {
            string row = line.Trim();
            if (row.StartsWith("|", StringComparison.Ordinal)) row = row.Substring(1);
            if (row.EndsWith("|", StringComparison.Ordinal) && !row.EndsWith("\\|", StringComparison.Ordinal))
                row = row.Substring(0, row.Length - 1);
            return Regex.Split(row, @"(?<!\\)\|").Select(x => x.Trim().Replace("\\|", "|")).ToArray();
        }

        internal static Table NewTable(int columns)
        {
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 10), BorderThickness = new Thickness(0, 0, 1, 1) };
            table.SetResourceReference(Table.BorderBrushProperty, "NoteBorder");
            for (int i = 0; i < columns; i++) table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup());
            return table;
        }

        internal static void AddRow(Table table, string[] values, bool header)
        {
            var row = new TableRow();
            table.RowGroups[0].Rows.Add(row);
            foreach (string value in values)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                AddInline(paragraph, value ?? "");
                var cell = new TableCell(paragraph) { Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(1, 1, 0, 0) };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "NoteBorder");
                if (header) cell.SetResourceReference(TableCell.BackgroundProperty, "NoteHeader");
                row.Cells.Add(cell);
            }
        }

        internal static string Save(FlowDocument document)
        {
            var lines = new List<string>();
            foreach (Block block in document.Blocks)
            {
                var table = block as Table;
                if (table != null)
                {
                    foreach (TableRowGroup group in table.RowGroups)
                    {
                        bool header = true;
                        foreach (TableRow row in group.Rows)
                        {
                            var cells = row.Cells.Select(cell => string.Join(" ", cell.Blocks.OfType<Paragraph>()
                                .Select(p => WriteInline(p.Inlines, false).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' '))).Replace("|", "\\|").Trim()).ToArray();
                            lines.Add(Row(cells));
                            if (header) lines.Add(Row(table.Tag as string[] ?? cells.Select(x => "---")));
                            header = false;
                        }
                    }
                    // A pipe in the next paragraph could otherwise turn it into a table row.
                    var following = block.NextBlock as Paragraph;
                    if (block.NextBlock is Table || (following != null && Plain(following).IndexOf('|') >= 0))
                        lines.Add("");
                    continue;
                }
                var paragraph = block as Paragraph;
                if (paragraph == null) continue;
                string prefix = paragraph.Tag as string ?? "";
                string plain = Plain(paragraph);
                if (prefix.StartsWith(Code, StringComparison.Ordinal))
                {
                    lines.Add(prefix);
                    lines.Add(plain);
                    lines.Add(Code);
                    continue;
                }
                if (prefix == Divider && plain.Length == 0) { lines.Add(Divider); continue; }
                string body = WriteInline(paragraph.Inlines, false);
                string marker = Marker(prefix);
                if (marker.Length > 0)
                {
                    if (body.StartsWith(marker, StringComparison.Ordinal)) body = body.Substring(marker.Length);
                    else prefix = "";
                }
                if (prefix == Divider) prefix = "";
                lines.Add(prefix.Length == 0 ? body : (prefix + " " + body).TrimEnd());
            }
            // Keep internal empty blocks and omit the editor's trailing insertion paragraph.
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return string.Join("\n", lines);
        }

        static string Row(IEnumerable<string> cells) { return "| " + string.Join(" | ", cells) + " |"; }

        internal static string Plain(Paragraph paragraph)
        {
            return new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Replace("\r\n", "\n");
        }

        static string WriteInline(InlineCollection inlines, bool inLink)
        {
            var result = new StringBuilder();
            var runText = new StringBuilder();
            string marker = "";
            Action flush = delegate
            {
                if (runText.Length == 0) return;
                string value = runText.ToString();
                string core = value.Trim();
                if (marker.Length == 0 || core.Length == 0) result.Append(value);
                else
                {
                    int start = value.IndexOf(core, StringComparison.Ordinal);
                    result.Append(value.Substring(0, start)).Append(marker).Append(core).Append(marker)
                        .Append(value.Substring(start + core.Length));
                }
                runText.Clear();
            };
            foreach (System.Windows.Documents.Inline inline in inlines)
            {
                var run = inline as Run;
                if (run != null)
                {
                    string next = run.FontFamily.Source == Mono.Source ? "`" :
                        (run.FontWeight == FontWeights.Bold ? "**" : "") + (run.FontStyle == FontStyles.Italic ? "*" : "");
                    if (next != marker) { flush(); marker = next; }
                    runText.Append(run.Text);
                    continue;
                }
                flush();
                var span = inline as Span;
                if (span != null)
                {
                    string destination = span.Tag as string;
                    string label = WriteInline(span.Inlines, inLink || destination != null);
                    if (destination != null && !inLink) result.Append('[').Append(label).Append("](").Append(destination).Append(')');
                    else result.Append(label);
                }
                else if (inline is LineBreak) result.Append('\n');
            }
            flush();
            return result.ToString();
        }
    }
}
