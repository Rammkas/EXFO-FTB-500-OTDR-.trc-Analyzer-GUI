// ==============================================================================
// MapPlot.cs — отрисовка карты трассы на Canvas (замена ASCII-карты консоли).
//
// Показывает: сырые кривые RawSamples (дБ против метров) для 1310/1550,
// полосы участков (буфер / проверяемое волокно), маркеры событий с ролями,
// сетку координат. Прореживание — максимум дБ на пиксельную колонку
// (пики отражений не теряются), как в MapRenderer консоли.
//
// Зум/панорамирование: Draw принимает окно просмотра по расстоянию
// (viewStartM..viewEndM); окно дБ автоматически подстраивается под отсчёты
// внутри окна X.
// ==============================================================================
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ExfoTrcAnalyzer;

namespace ExfoTrcAnalyzerGUI;

internal static class MapPlot
{
    // Поля отступа области графика (нужны MainWindow для зума по позиции курсора)
    public const double PlotLeft = 54, PlotRight = 12, PlotTop = 42, PlotBottom = 28;
    public const double EndMargin = 1.15;   // показывать трассу чуть дальше последнего события
    public const double MinDbSpan = 12.0;   // минимальное окно дБ (плоская трасса нечитаема)
    public const double MinViewSpanM = 10;  // минимальный масштаб по расстоянию

    // Цвета кривых длин волн (читаемы на светлой и тёмной темах)
    private static readonly Color Color1310 = Color.FromRgb(0x1E, 0x88, 0xE5);
    private static readonly Color Color1550 = Color.FromRgb(0xE6, 0x4A, 0x19);

    // Полосы участков
    private static readonly Color BandBuffer = Color.FromRgb(0x9E, 0x9E, 0x9E);
    private static readonly Color BandFiber = Color.FromArgb(0x66, 0x00, 0x78, 0xD4);

    // Маркеры событий по роли
    private static readonly Color MarkPort = Color.FromRgb(0x61, 0x61, 0x61);
    private static readonly Color MarkConnector = Color.FromRgb(0xEF, 0x6C, 0x00);
    private static readonly Color MarkSplice = Color.FromRgb(0x9E, 0x9E, 0x9E);
    private static readonly Color MarkEnd = Color.FromRgb(0xC6, 0x28, 0x28);
    private static readonly Color MarkDefault = Color.FromRgb(0x75, 0x75, 0x75);

    // курсоры-измерители: А — янтарный, В — синий (отличаются от маркеров событий)
    private static readonly Color ColorCursorA = Color.FromRgb(0xE6, 0x9F, 0x00);
    private static readonly Color ColorCursorB = Color.FromRgb(0x00, 0xAC, 0xC8);

    // Полный диапазон трассы (метры, уже с EndMargin); 0 = рисовать нечего
    public static double ComputeMaxDistance(FiberAnalysis? fiber, List<RawTrace.TraceCurve>? curves)
    {
        if (fiber == null)
            return 0;
        double maxDist = 0;
        foreach (var s in fiber.Sections)
        {
            maxDist = Math.Max(maxDist, s.StartM ?? 0);
            maxDist = Math.Max(maxDist, s.EndM ?? 0);
        }
        foreach (var e in fiber.Events)
            maxDist = Math.Max(maxDist, e.PosM);
        if (curves != null)
            foreach (var c in curves)
                for (int i = c.SampleCount - 1; i >= 0; i--)
                {
                    double d = c.DistanceM(i);
                    if (!double.IsNaN(d)) { maxDist = Math.Max(maxDist, d); break; }
                }
        return maxDist * EndMargin;
    }

    // Кривые, показываемые при текущих чекбоксах длин волн (тот же фильтр,
    // что и внутри Draw — используется MainWindow для расчёта фиксированного окна дБ)
    public static List<RawTrace.TraceCurve> VisibleCurves(List<RawTrace.TraceCurve> curves, bool show1310, bool show1550)
    {
        var visible = curves.Where(c =>
            c.WavelengthNm == 1310 && show1310 ||
            c.WavelengthNm == 1550 && show1550 ||
            c.WavelengthNm != 1310 && c.WavelengthNm != 1550 && (show1310 || show1550)).ToList();
        return visible.Count == 0 ? curves : visible;
    }

    // fixedDbMin/fixedDbMax != null — ось Y зафиксирована (автоподстройка выключена):
    // окно дБ заданное, зум по X его не пересчитывает
    public static void Draw(Canvas canvas, FiberAnalysis? fiber, List<RawTrace.TraceCurve>? curves,
                            bool show1310, bool show1550, double? viewStartM, double? viewEndM,
                            double? fixedDbMin, double? fixedDbMax,
                            double? cursorAM = null, double? cursorBM = null)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 80 || h < 80)
            return;

        var fg = Theme("ForegroundBrush", Brushes.Black);
        var grid = Theme("GridLineBrush", Brushes.LightGray);
        var border = Theme("ChromeBorderBrush", Brushes.Gray);

        if (fiber == null || curves == null || curves.Count == 0)
        {
            AddText(canvas, 12, 12, Loc.T("map_no_raw_data"), fg, 13);
            return;
        }

        double fullMax = ComputeMaxDistance(fiber, curves);
        if (fullMax <= 0)
        {
            AddText(canvas, 12, 12, Loc.T("map_no_coords"), fg, 13);
            return;
        }

        // ---- окно просмотра по расстоянию ----
        double viewStart = Math.Clamp(viewStartM ?? 0, 0, fullMax);
        double viewEnd = Math.Clamp(viewEndM ?? fullMax, 0, fullMax);
        if (viewEnd - viewStart < MinViewSpanM)
        {
            double mid = (viewStart + viewEnd) / 2;
            viewStart = Math.Max(0, mid - MinViewSpanM / 2);
            viewEnd = Math.Min(fullMax, viewStart + MinViewSpanM);
            viewStart = Math.Max(0, viewEnd - MinViewSpanM);
        }
        double span = viewEnd - viewStart;

        double left = PlotLeft, right = PlotRight, top = PlotTop, bottom = PlotBottom;
        double pw = w - left - right, ph = h - top - bottom;
        if (pw < 40 || ph < 40)
            return;

        // ---- окно дБ: фиксированное (авто выкл.) или по отсчётам ВНУТРИ окна X ----
        var visible = VisibleCurves(curves, show1310, show1550);

        double dbMin = double.MaxValue, dbMax = double.MinValue;
        bool fixedDb = fixedDbMin != null && fixedDbMax != null;
        if (fixedDb)
        {
            dbMin = fixedDbMin!.Value;
            dbMax = fixedDbMax!.Value;
            if (dbMax - dbMin < 0.5) // защита от вырожденного окна
            {
                double mid = (dbMax + dbMin) / 2;
                dbMin = mid - 0.5;
                dbMax = mid + 0.5;
            }
        }
        else
        {
            foreach (var c in visible)
                for (int i = 0; i < c.SampleCount; i++)
                {
                    double d = c.DistanceM(i);
                    if (double.IsNaN(d) || d < viewStart || d > viewEnd) continue;
                    double db = c.LevelDb(i);
                    if (db < dbMin) dbMin = db;
                    if (db > dbMax) dbMax = db;
                }
            if (dbMin > dbMax)
            {
                AddText(canvas, 12, 12, Loc.T("map_window_empty"), fg, 13);
                return;
            }
        }
        if (!fixedDb)
        {
            if (dbMax - dbMin < MinDbSpan)
            {
                double mid = (dbMax + dbMin) / 2;
                dbMin = mid - MinDbSpan / 2;
                dbMax = mid + MinDbSpan / 2;
            }
            // авто: небольшой запас по краям; фиксированное окно (авто выкл.,
            // зум Shift+колесо) рисуется ровно таким, каким сохранено
            double pad = (dbMax - dbMin) * 0.08;
            dbMin -= pad; dbMax += pad;
        }

        double X(double dist) => left + (dist - viewStart) / span * pw;
        double Y(double db) => top + (dbMax - db) / (dbMax - dbMin) * ph;

        // ---- затенённая зона ниже 0 дБ (затухание) ----
        if (dbMin < 0)
        {
            double y0 = Math.Clamp(Y(0.0), top, h - bottom); // 0 выше окна -> затемняется весь график
            if (h - bottom > y0)
            {
                var shade = new Rectangle
                {
                    Width = pw, Height = h - bottom - y0,
                    Fill = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0)),
                };
                Canvas.SetLeft(shade, left);
                Canvas.SetTop(shade, y0);
                canvas.Children.Add(shade);
            }
        }

        // ---- сетка: горизонтальные линии дБ ----
        double dbStep = NiceDbStep((dbMax - dbMin) / 6);
        for (double db = Math.Ceiling(dbMin / dbStep) * dbStep; db <= dbMax; db += dbStep)
        {
            double y = Y(db);
            bool isZero = Math.Abs(db) < dbStep / 2; // линия 0 дБ — выделена толщиной
            canvas.Children.Add(new Line { X1 = left, Y1 = y, X2 = w - right, Y2 = y, Stroke = grid, StrokeThickness = isZero ? 2.2 : 1 });
            AddText(canvas, 6, y - 8, db.ToString("F1", CultureInfo.InvariantCulture), fg, 10);
        }

        // ---- сетка: вертикальные линии расстояния ----
        double tickM = NiceDistStep(span / 6);
        for (double d = Math.Ceiling(viewStart / tickM) * tickM; d <= viewEnd; d += tickM)
        {
            double x = X(d);
            canvas.Children.Add(new Line { X1 = x, Y1 = top, X2 = x, Y2 = h - bottom, Stroke = grid, StrokeThickness = 1 });
            // подписи: км при шаге >= 1000 м, иначе метры
            string label = tickM >= 1000
                ? (d / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)
                : d.ToString("0", CultureInfo.InvariantCulture);
            AddText(canvas, x + 2, h - bottom + 4, label, fg, 10);
        }
        AddText(canvas, w - right - 22, h - bottom + 4, tickM >= 1000 ? Loc.T("unit_km") : Loc.T("unit_m"), fg, 10);

        // ---- рамка области графика ----
        canvas.Children.Add(new Line { X1 = left, Y1 = top, X2 = left, Y2 = h - bottom, Stroke = border, StrokeThickness = 1 });
        canvas.Children.Add(new Line { X1 = left, Y1 = h - bottom, X2 = w - right, Y2 = h - bottom, Stroke = border, StrokeThickness = 1 });

        // ---- полосы участков (буфер / волокно) над графиком, обрезаются по окну ----
        double bandTop = 8, bandH = 12;
        foreach (var s in fiber.Sections)
        {
            if (s.StartM == null || s.EndM == null || s.EndM <= s.StartM)
                continue;
            double x0 = Math.Max(left, X(s.StartM.Value)), x1 = Math.Min(w - right, X(s.EndM.Value));
            if (x1 <= x0)
                continue;
            bool isBuffer = s.Role == Loc.T("role_buffer");
            var band = new Rectangle
            {
                Width = x1 - x0,
                Height = bandH,
                Fill = new SolidColorBrush(isBuffer ? BandBuffer : BandFiber),
            };
            Canvas.SetLeft(band, x0);
            Canvas.SetTop(band, bandTop);
            canvas.Children.Add(band);
            if (x1 - x0 > 48)
                AddText(canvas, x0 + 4, bandTop - 1, isBuffer ? Loc.T("band_buffer") : Loc.T("band_fiber"), fg, 10);
        }

        // ---- маркеры событий ----
        foreach (var e in fiber.Events)
        {
            if (e.PosM < viewStart || e.PosM > viewEnd)
                continue;
            double x = X(e.PosM);
            var markBrush = new SolidColorBrush(MarkForRole(e.Role));
            canvas.Children.Add(new Line
            {
                X1 = x, Y1 = bandTop + bandH + 2, X2 = x, Y2 = h - bottom,
                Stroke = markBrush, StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
            });
            string label = ShortRole(e.Role);
            var tb = AddText(canvas, x + 2, bandTop + bandH + 2, label, markBrush, 10);
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (x + 2 + tb.DesiredSize.Width > w - right) // не влезает справа — ставим слева
                Canvas.SetLeft(tb, Math.Max(left, x - 2 - tb.DesiredSize.Width));
        }

        // ---- кривые: максимум дБ на пиксельную колонку (пики не теряются) ----
        foreach (var c in visible)
        {
            Color color = c.WavelengthNm switch
            {
                1310 => Color1310,
                1550 => Color1550,
                _ => Color1310,
            };

            var best = new double[(int)Math.Max(1, Math.Floor(pw))];
            Array.Fill(best, double.NegativeInfinity);
            for (int i = 0; i < c.SampleCount; i++)
            {
                double d = c.DistanceM(i);
                if (double.IsNaN(d) || d < viewStart || d > viewEnd) continue;
                int px = Math.Clamp((int)((d - viewStart) / span * pw), 0, best.Length - 1);
                double db = c.LevelDb(i);
                if (db > best[px]) best[px] = db;
            }

            var pts = new PointCollection();
            for (int px = 0; px < best.Length; px++)
                if (!double.IsNegativeInfinity(best[px]))
                    pts.Add(new Point(left + px, Y(best[px])));
            if (pts.Count < 2)
                continue;

            canvas.Children.Add(new Polyline
            {
                Points = pts,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.3,
            });
        }

        // ---- курсоры-измерители А / В ----
        DrawCursor(Loc.T("cursor_a_name"), ColorCursorA, cursorAM);
        DrawCursor(Loc.T("cursor_b_name"), ColorCursorB, cursorBM);
        void DrawCursor(string name, Color color, double? cursorPos)
        {
            double? dist = cursorPos;
            if (dist == null)
                return;
            double d = dist.Value;
            if (d < viewStart || d > viewEnd)
                return; // курсор вне текущего окна
            double x = X(d);
            var brush = new SolidColorBrush(color);
            canvas.Children.Add(new Line
            {
                X1 = x, Y1 = bandTop, X2 = x, Y2 = h - bottom,
                Stroke = brush, StrokeThickness = 1.6,
            });
            var tb = AddText(canvas, x + 3, bandTop + bandH + 2, Loc.F("cursor_marker_fmt", name, d), brush, 11);
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (x + 3 + tb.DesiredSize.Width > w - right)
                Canvas.SetLeft(tb, Math.Max(left, x - 3 - tb.DesiredSize.Width));
        }

        // ---- легенда ----
        AddLegend(canvas, w - right - 150, top + 6, Color1310, Loc.T("lbl_1310nm"), show1310, fg);
        AddLegend(canvas, w - right - 150, top + 22, Color1550, Loc.T("lbl_1550nm"), show1550, fg);
    }

    // --------------------------------------------------------------------------
    private static Brush Theme(string key, Brush fallback)
        => Application.Current.TryFindResource(key) as Brush ?? fallback;

    // Роли/метки сравниваются со строками ТЕКУЩЕГО языка: роли генерирует
    // Analyzer через Loc.T при анализе, а смена языка всегда сопровождается
    // пересчетом файлов (сохранение настроек).
    private static Color MarkForRole(string role) => role switch
    {
        var r when r == Loc.T("role_port") => MarkPort,
        var r when r == Loc.T("role_qc") => MarkConnector,
        var r when r == Loc.T("role_end") => MarkEnd,
        var r when r == Loc.T("role_splice") => MarkSplice,
        _ => MarkDefault,
    };

    private static string ShortRole(string role) => role switch
    {
        var r when r == Loc.T("role_port") => Loc.T("mark_port"),
        var r when r == Loc.T("role_qc") => Loc.T("mark_qc"),
        var r when r == Loc.T("role_end") => Loc.T("mark_end"),
        var r when r == Loc.T("role_splice") => Loc.T("mark_splice"),
        _ => role,
    };

    private static double NiceDbStep(double raw)
    {
        foreach (double s in new[] { 0.5, 1, 2, 5, 10, 20 })
            if (raw <= s) return s;
        return 50;
    }

    private static double NiceDistStep(double rawMeters)
    {
        foreach (double s in new[] { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 })
            if (rawMeters <= s) return s;
        return 100000;
    }

    private static TextBlock AddText(Canvas canvas, double x, double y, string text, Brush fg, double size)
    {
        var tb = new TextBlock { Text = text, Foreground = fg, FontSize = size };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        canvas.Children.Add(tb);
        return tb;
    }

    private static void AddLegend(Canvas canvas, double x, double y, Color color, string label, bool visible, Brush fg)
    {
        var swatch = new Rectangle { Width = 14, Height = 3, Fill = new SolidColorBrush(color), Opacity = visible ? 1.0 : 0.3 };
        Canvas.SetLeft(swatch, x);
        Canvas.SetTop(swatch, y + 5);
        canvas.Children.Add(swatch);
        var tb = AddText(canvas, x + 20, y, label, fg, 11);
        tb.Opacity = visible ? 1.0 : 0.4;
    }
}
