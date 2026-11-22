// ==============================================================================
// PrintPdf.cs — сохранение FlowDocument в PDF без принтера и без сторонних
// библиотек: векторный PDF. Текст преобразуется в векторные контуры
// (FormattedText.BuildGeometry), поэтому кириллица корректно отображается в
// любом просмотрщике без встраивания шрифтов и проблем с кодировками.
// ==============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ExfoTrcAnalyzerGUI;

public static class PrintPdf
{
    private const double PageW = 595.28, PageH = 841.89; // A4 в пунктах
    private const double Margin = 34;
    private const double TableFont = 8.5;
    private const double Sx = 0.75; // 1/96 дюйма (WPF px) -> 1/72 дюйма (pt)

    // число для PDF-стримов — всегда с точкой, независимая от локали культура
    private static string P(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static void Save(FlowDocument doc, string path)
    {
        // ---- 1. FlowDocument -> плоские строки/линии ----
        var items = Flatten(doc);

        // ---- 2. разбивка на страницы ----
        var pages = new List<List<Item>>();
        var cur = new List<Item>();
        double y = PageH - Margin;
        foreach (var it in items)
        {
            double need = it.GapBefore + it.Height;
            if (cur.Count > 0 && y - need < Margin + 14)
            {
                pages.Add(cur);
                cur = new List<Item>();
                y = PageH - Margin;
            }
            y -= it.GapBefore;
            y -= it.Height;
            it.Y = y; // низ полосы блока (PDF: координаты снизу вверх)
            cur.Add(it);
        }
        if (cur.Count > 0)
            pages.Add(cur);
        if (pages.Count == 0)
            pages.Add(new List<Item>());

        // ---- 3. запись PDF ----
        using var fs = new FileStream(path, FileMode.Create);
        var offsets = new List<long>();
        void W(string s) => fs.Write(Encoding.Latin1.GetBytes(s));

        int n = pages.Count;
        // объекты без пропусков: 1 catalog, 2 pages,
        // на страницу i: content = 3+2i, page = 4+2i
        W("%PDF-1.4\n");

        offsets.Add(fs.Position);
        W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets.Add(fs.Position);
        string kids = string.Join(" ", Enumerable.Range(0, n).Select(i => $"{4 + 2 * i} 0 R"));
        W($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {n} >>\nendobj\n");

        for (int i = 0; i < n; i++)
        {
            int contObj = 3 + 2 * i, pageObj = 4 + 2 * i;

            var sb = new StringBuilder("0 0 0 rg\n");
            foreach (var it in pages[i])
            {
                if (it.Line)
                {
                    double ly = it.Y; // разделитель — внизу полосы строки
                    sb.Append("0.6 w ").Append(P(Margin)).Append(' ').Append(P(ly))
                      .Append(" m ").Append(P(PageW - Margin)).Append(' ').Append(P(ly))
                      .Append(" l S\n");
                    continue;
                }

                var ft = new FormattedText(it.Text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface(it.Bold), it.FontSize, Brushes.Black, 1.0);
                var geom = ft.BuildGeometry(new Point(0, 0));
                double ascent = ft.Baseline * Sx;
                double baseline = it.Height > 0 ? it.Y + it.Height - ascent - 1 : it.Y + 3;
                AppendGeometry(sb, geom, it.X, baseline);
            }

            string content = sb.ToString();
            offsets.Add(fs.Position);
            W($"{contObj} 0 obj\n<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}endstream\nendobj\n");

            offsets.Add(fs.Position);
            W($"{pageObj} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {P(PageW)} {P(PageH)}]" +
              " /Contents " + contObj + " 0 R >>\nendobj\n");
        }

        long xref = fs.Position;
        int total = 2 + 2 * n;
        W($"xref\n0 {total + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets)
            W($"{off:D10} 00000 n \n");
        W($"trailer\n<< /Size {total + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
    }

    // ---------- обход геометрии текста -> команды пути PDF ----------
    private static void AppendGeometry(StringBuilder sb, Geometry geometry, double ox, double baseline)
    {
        var pg = geometry as PathGeometry ?? geometry.GetFlattenedPathGeometry();

        foreach (var fig in pg.Figures)
        {
            double cx = fig.StartPoint.X, cy = fig.StartPoint.Y;
            sb.Append(P(ox + cx * Sx)).Append(' ').Append(P(baseline - cy * Sx)).Append(" m ");
            foreach (var seg in fig.Segments)
            {
                switch (seg)
                {
                    case LineSegment l:
                        cx = l.Point.X; cy = l.Point.Y;
                        sb.Append(P(ox + cx * Sx)).Append(' ').Append(P(baseline - cy * Sx)).Append(" l ");
                        break;

                    case PolyLineSegment pl:
                        foreach (var pt in pl.Points)
                        {
                            cx = pt.X; cy = pt.Y;
                            sb.Append(P(ox + cx * Sx)).Append(' ').Append(P(baseline - cy * Sx)).Append(" l ");
                        }
                        break;

                    case BezierSegment b:
                        sb.AppendCurve(ox, baseline, b.Point1, b.Point2, b.Point3);
                        cx = b.Point3.X; cy = b.Point3.Y;
                        break;

                    case QuadraticBezierSegment q:
                        // квадратика -> кубика
                        var c1 = new Point(cx + 2.0 / 3 * (q.Point1.X - cx), cy + 2.0 / 3 * (q.Point1.Y - cy));
                        var c2 = new Point(q.Point2.X + 2.0 / 3 * (q.Point1.X - q.Point2.X), q.Point2.Y + 2.0 / 3 * (q.Point1.Y - q.Point2.Y));
                        sb.AppendCurve(ox, baseline, c1, c2, q.Point2);
                        cx = q.Point2.X; cy = q.Point2.Y;
                        break;

                    case ArcSegment a: // в глифах почти не встречается — упрощаем до отрезка
                        cx = a.Point.X; cy = a.Point.Y;
                        sb.Append(P(ox + cx * Sx)).Append(' ').Append(P(baseline - cy * Sx)).Append(" l ");
                        break;
                }
            }
            if (fig.IsClosed)
                sb.Append("h ");
            sb.Append("f*\n"); // even-odd — корректные «дырки» в буквах о/а/б
        }
    }

    private static void AppendCurve(this StringBuilder sb, double ox, double baseline, Point p1, Point p2, Point p3)
    {
        sb.Append(P(ox + p1.X * Sx)).Append(' ').Append(P(baseline - p1.Y * Sx)).Append(' ')
          .Append(P(ox + p2.X * Sx)).Append(' ').Append(P(baseline - p2.Y * Sx)).Append(' ')
          .Append(P(ox + p3.X * Sx)).Append(' ').Append(P(baseline - p3.Y * Sx)).Append(" c ");
    }

    // ---------- плоские элементы ----------
    private sealed class Item
    {
        public string Text = "";
        public double X, Y, Height, GapBefore, FontSize;
        public bool Bold;
        public bool Line; // горизонтальная линия на всю ширину
    }

    private static List<Item> Flatten(FlowDocument doc)
    {
        var items = new List<Item>();
        double indent = Margin;

        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case Paragraph p:
                {
                    double fsz = p.FontSize > 0 ? p.FontSize : doc.FontSize;
                    string text = TextOf(p.Inlines);
                    items.Add(new Item
                    {
                        Text = text.Length == 0 ? " " : text,
                        X = indent,
                        Height = fsz * 1.4,
                        GapBefore = 4,
                        FontSize = fsz,
                        Bold = p.FontWeight == FontWeights.Bold,
                    });
                    break;
                }

                case Table t:
                    AddTable(t, indent, items);
                    break;
            }
        }
        return items;
    }

    private static void AddTable(Table table, double indent, List<Item> items)
    {
        int colsCount = Math.Max(1, table.Columns.Count);
        double avail = PageW - 2 * indent;

        // тексты всех ячеек
        var rowsText = new List<List<string>>();
        var rowBold = new List<bool>();
        bool firstGroup = true;
        foreach (var group in table.RowGroups)
        {
            bool firstRow = true;
            foreach (var row in group.Rows)
            {
                var texts = new List<string>();
                int c = 0;
                foreach (var cell in row.Cells)
                {
                    if (c >= colsCount) break;
                    texts.Add(string.Join(" ", cell.Blocks.OfType<Paragraph>().Select(p => TextOf(p.Inlines))));
                    c++;
                }
                while (texts.Count < colsCount) texts.Add("");
                rowsText.Add(texts);
                rowBold.Add(firstGroup && firstRow);
                firstRow = false;
            }
            firstGroup = false;
        }

        // ширина колонок пропорциональна реальной ширине содержимого
        var nat = new double[colsCount];
        for (int ri = 0; ri < rowsText.Count; ri++)
            for (int c = 0; c < colsCount; c++)
                nat[c] = Math.Max(nat[c], TextWidthPt(rowsText[ri][c], rowBold[ri], TableFont) + 6);

        double total = nat.Sum();
        var colW = new double[colsCount];
        for (int c = 0; c < colsCount; c++)
            colW[c] = total <= avail ? nat[c] : avail * nat[c] / total;
        double sum = colW.Sum();
        if (sum > avail)
            for (int c = 0; c < colsCount; c++) colW[c] *= avail / sum;

        double rowH = TableFont * 1.6;
        for (int ri = 0; ri < rowsText.Count; ri++)
        {
            bool bold = rowBold[ri];
            items.Add(new Item { GapBefore = bold ? 6 : 2, Height = rowH, Line = true });

            double x = indent;
            for (int c = 0; c < colsCount; c++)
            {
                string text = Fit(rowsText[ri][c], colW[c] - 6, bold);
                items.Add(new Item
                {
                    Text = text.Length == 0 ? " " : text,
                    X = x + 3,
                    Height = 0, // высоту строки уже занимает линия-разделитель
                    GapBefore = 0,
                    FontSize = TableFont,
                    Bold = bold,
                });
                x += colW[c];
            }
        }
    }

    // обрезка текста под ширину колонки (с многоточием)
    private static string Fit(string text, double maxW, bool bold)
    {
        if (text.Length == 0 || maxW <= 0)
            return text;
        bool clipped = false;
        while (text.Length > 1 && TextWidthPt(text, bold, TableFont) > maxW)
        {
            text = text[..(text.Length - 1)];
            clipped = true;
        }
        if (clipped)
        {
            while (text.Length > 1 && TextWidthPt(text + "…", bold, TableFont) > maxW)
                text = text[..(text.Length - 1)];
            text += "…";
        }
        return text;
    }

    private static double TextWidthPt(string text, bool bold, double fontSize)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface(bold), fontSize, Brushes.Black, 1.0);
        return ft.WidthIncludingTrailingWhitespace * Sx;
    }

    private static readonly FontFamily UiFont = new("Segoe UI");

    private static Typeface Typeface(bool bold) => new(UiFont, FontStyles.Normal,
        bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

    private static string TextOf(InlineCollection inlines)
    {
        var sb = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run r: sb.Append(r.Text); break;
                case Span sp: sb.Append(TextOf(sp.Inlines)); break;
                case LineBreak: sb.Append(' '); break;
            }
        }
        return sb.ToString().Trim();
    }
}
