// ==============================================================================
// Analyzer.cs — смысловой анализ рефлектограмм: из дерева .trc делает вердикт ОТК.
//
// СТЕНД ИЗМЕРЕНИЙ (схема, под которую написана логика):
//   порт рефлектометра -> постоянный коннектор -> буферная катушка (~1 км)
//   -> коннектор ОТК (сюда подключается проверяемое волокно) -> проверяемое волокно
// Каждый замер делается на двух длинах волн: 1310 нм и 1550 нм.
//
// КАК ВЫГЛЯДЯТ ДАННЫЕ EXFO (подтверждено на эталонных файлах, см. --dump-keys):
// - Trace содержит EventTable с ключами Event0, Event1, ... (НУМЕРАЦИЯ С НУЛЯ);
//   записи чередуются: точечные события (есть поля Type/Reflectance) и линейные
//   участки LMA (Position/Length/Loss/CursorBPosition, без Type).
// - Пример реальной трассы (9076.trc, 1310 нм):
//     Event0 = коннектор порта (Position=0, Loss=NaN->null)
//     Event1 = LMA буферной катушки (0..1010.93 м, Loss=0.328 дБ)
//     Event2 = коннектор ОТК (Loss=0.541 дБ)  <- его проверяет check_qc_connector
//     Event3 = LMA проверяемого волокна (1010.93..8349.28 м, Loss=2.364 дБ)
//     Event4 = конец волокна
// - ВАЖНАЯ СЕМАНТИКА ЗАТУХАНИЯ: EXFO НЕ хранит измеренный коэффициент дБ/км в
//   EventTable. Поле FiberSectionCharacteristics — это НАСТРОЙКИ шаблона
//   (TypicalFiberAttenuation), SpansLoss/SpansLength — итог по всем спанам вместе
//   с коннекторами. При этом потери коннекторов EXFO относит к точечным событиям,
//   а LMA лежат МЕЖДУ событиями, поэтому затухание участка = Loss/Length —
//   корректная оценка. Если в других прошивках появится явное поле затухания
//   (AttenuationKeys ниже), оно будет использовано вместо расчётного.
//
// ЛОГИКА ВЕРДИКТА:
// 1) Участки 1310 и 1550 сопоставляются ПО КООРДИНАТЕ НАЧАЛА (не по индексу) —
//    при лишнем/пропущенном участке на одной длине волне пары не съезжают.
// 2) Первый участок длиной в окне buffer_min_m..buffer_max_m помечается BUFFER
//    и исключается из длины/потерь проверяемого волокна (DUT).
// 3) DUT-участки: затухание каждой длины волн сравнивается с aten_1310_max /
//    aten_1550_max.
// 4) Точечные события получают роль: порт (позиция < FrontPortMaxPosM), конец
//    волокна (последнее событие), коннектор ОТК (позиция == конец буфера +- допуск;
//    при check_qc_connector=True проверяется его Loss), иначе — сварка (порог
//    splice_loss_max).
// 5) Волокно FAIL, если хоть одна проверка не пройдена; причины пишутся в CSV.
// 6) Замер БЕЗ буферной катушки (волокно напрямую к постоянному коннектору) —
//    штатный режим: поставьте has_buffer_spool=False, тогда все участки считаются
//    проверяемым волокном, колонки QC-коннектора пустые. Сам трасса «безбуферность»
//    не определяет — по этому случаю Analyzer добавляет Warnings (в CSV отдельной
//    колонкой), если настройка противоречит трассе:
//      - флаг True, но буфероподобного участка нет  -> «поставьте has_buffer_spool=False»;
//      - флаг True и вся трасса ушла в буфер (длина DUT 0) -> опасный случай:
//        проверяемое волокно 800..1200 м принято за буфер, вердикт PASS при пустых
//        данных — предупреждение ловит это.
//    Обратный неоднозначный случай (замер С буфером при has_buffer_spool=False)
//    предупреждением не покрыт: буфер войдёт в DUT и, как правило, сам не пройдёт
//    норму затухания (SMF ~0.33 дБ/км против порога 0.36).
// ==============================================================================
using System.Globalization;
using System.IO; // в WPF-проекте (net10.0-windows) implicit usings не включает System.IO
using System.Text;
using ExfoTrcAnalyzerGUI; // Loc — локализация строк движка (роли/причины)

namespace ExfoTrcAnalyzer;

// ==============================================================================
// АНАЛИЗ РЕФЛЕКТОГРАММ: трассы -> участки/события -> вердикт PASS/FAIL
// ==============================================================================
public sealed class Section
{
    public double? StartM;
    public double? EndM;
    public double? LengthM;
    public double? LossDb;
    public double? AttenDbKm;
    public bool AttenFromExplicitField; // true = взято из явного поля дерева, false = рассчитано Loss/Length
}

public sealed class PointEvent
{
    public double? PosM;
    public double? LossDb;
    public double? ReflDb;
    public string Comment = "";
}

public sealed class TraceMetrics
{
    public int WavelengthNm;
    public List<Section> Sections = new();
    public List<PointEvent> Events = new();
}

public sealed class SectionPair
{
    public Section S13 = new();
    public Section S15 = new();
}

public sealed class EvPair
{
    public PointEvent? E13;
    public PointEvent? E15;
}

public sealed class SectionAnalysis
{
    public int Index;
    public string Role = "";
    public string Verdict = "PASS";
    public double? StartM;
    public double? EndM;
    public Section S13 = new();
    public Section S15 = new();
    public List<string> Reasons = new();
}

public sealed class EventAnalysis
{
    public int Index;
    public string Role = "";
    public string Verdict = "PASS";
    public double PosM;
    public double? L13, R13, L15, R15;
    public List<string> Reasons = new();
}

public sealed class FiberAnalysis
{
    public string FiberId = "";
    public List<SectionAnalysis> Sections = new();
    public List<EventAnalysis> Events = new();
    public double Len13, Loss13, Len15, Loss15;
    public double? Att13, Att15;
    public double? QcLoss13, QcLoss15;
    public string Verdict = "PASS";
    public List<string> Fails = new();
    public List<string> Warnings = new(); // предупреждения, не влияющие на вердикт
}

public static class Analyzer
{
    // Событие на координате < FrontPortMaxPosM считается портом рефлектометра —
    // это постоянный коннектор стенда перед буферной катушкой.
    public const double FrontPortMaxPosM = 5.0;

    // Стандартные длины волн измерений на стенде ОТК
    public const int Wavelength1310Nm = 1310;
    public const int Wavelength1550Nm = 1550;

    // Префикс ключей таблицы событий EXFO: "Event0", "Event1", ... (нумерация с нуля)
    public const string EventKeyPrefix = "Event";
    private static readonly System.Text.RegularExpressions.Regex EventKeyRegex =
        new($"^{EventKeyPrefix}\\d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private const double MetersPerKm = 1000.0;     // метров в километре — для расчёта затухания в дБ/км
    private const double NanometersPerMeter = 1e9; // EXFO хранит длину волны в метрах (1.31E-06)

    // Ограничения вывода --dump-keys
    private const int TraceValuePreviewMaxLength = 400; // макс. длина предпросмотра одного значения ключа
    private const int StringPreviewMaxLength = 40;      // макс. длина строки в предпросмотре
    private const int PreviewMaxDepth = 2;              // макс. глубина вложенности словарей в предпросмотре

    // Явные поля затухания в дереве EXFO (проверяются раньше, чем Loss/Length).
    // Точные имена подтверждаются режимом: exfo_trc_analyzer --dump-keys <файл.trc>
    private static readonly string[] AttenuationKeys =
    {
        "Attenuation",
        "AttenuationCoefficient",
        "AttenuationDbPerKm",
        "AttenuationPerKm",
        "LMAAttenuation",
    };

    // --------------------------------------------------------------------------
    // Полный разбор файла: .trc -> список волокон с вердиктами
    // --------------------------------------------------------------------------
    public static List<FiberAnalysis> AnalyzeFile(string filepath)
    {
        byte[] stream = TrcParser.DecompressTrc(filepath);
        var tree = TrcParser.FindRootTree(stream);

        var fibers = TrcParser.AsDict(
            TrcParser.Get(tree, "OtdrData") is Dictionary<string, object?> otdr
                ? TrcParser.Get(otdr, "Fibers")
                : null);

        string baseName = Path.GetFileNameWithoutExtension(filepath);
        var result = new List<FiberAnalysis>();
        foreach (var fEntry in fibers)
        {
            if (fEntry.Value is Dictionary<string, object?> fObj)
                result.Add(AnalyzeFiber(fObj, baseName));
        }
        return result;
    }

    public static FiberAnalysis AnalyzeFiber(Dictionary<string, object?> fObj, string baseName)
    {
        var res = new FiberAnalysis();

        res.FiberId = TrcParser.ValueToString(TrcParser.Get(fObj, "Color"));
        if (string.IsNullOrEmpty(res.FiberId))
            res.FiberId = TrcParser.ValueToString(TrcParser.Get(fObj, "Identifier"));
        if (string.IsNullOrEmpty(res.FiberId))
            res.FiberId = baseName;

        var byWl = new Dictionary<int, TraceMetrics>();
        var traces = TrcParser.AsDict(TrcParser.Get(fObj, "Traces"));
        foreach (var tEntry in traces)
        {
            if (tEntry.Value is Dictionary<string, object?> tObj && tObj.ContainsKey("Wavelength"))
            {
                var m = ExtractTraceMetrics(tObj);
                byWl[m.WavelengthNm] = m;
            }
        }

        var t13 = byWl.TryGetValue(Wavelength1310Nm, out var m13) ? m13 : null;
        var t15 = byWl.TryGetValue(Wavelength1550Nm, out var m15) ? m15 : null;

        // Участки 1310/1550 сопоставляются по координате, а не по индексу:
        // при лишнем/пропущенном участке на одной длине волне пары не съезжают.
        var pairs = MatchSectionsByPosition(
            t13?.Sections ?? new List<Section>(),
            t15?.Sections ?? new List<Section>(),
            Config.MatchToleranceM);

        double bufferEndM = 0.0;

        for (int idx = 0; idx < pairs.Count; idx++)
        {
            var p = pairs[idx];

            double len13 = p.S13.LengthM ?? 0.0;
            double len15 = p.S15.LengthM ?? 0.0;
            // для окна буфера берём длину 1550; 0 означает «нет данных на этой длине волны»
            double checkLen = len15 > 0 ? len15 : len13;

            double? startM = p.S15.StartM ?? p.S13.StartM;
            // 0 в CursorBPosition = EXFO не зафиксировал конец участка — берем с другой длины волны
            double? endM = (p.S15.EndM == null || p.S15.EndM == 0) ? p.S13.EndM : p.S15.EndM;
            double? att13 = p.S13.AttenDbKm;
            double? att15 = p.S15.AttenDbKm;

            // буферной катушкой может быть только первый участок трассы (idx == 0)
            bool isBuffer =
                idx == 0 &&
                Config.HasBufferSpool &&
                Config.BufferMinM <= checkLen &&
                checkLen <= Config.BufferMaxM;

            var sa = new SectionAnalysis
            {
                Index = idx + 1,
                S13 = p.S13,
                S15 = p.S15,
                StartM = startM,
                EndM = endM,
            };
            var reasons = new List<string>();

            if (isBuffer)
            {
                sa.Role = Loc.T("role_buffer");
                // 0 в endM = «нет данных о конце» — считаем буфер равным длине участка
                bufferEndM = (endM == null || endM == 0) ? checkLen : endM.Value;
                sa.Verdict = "BUFFER";
            }
            else
            {
                sa.Role = Loc.T("role_fiber");
                res.Len13 += len13;
                res.Len15 += len15;
                if (p.S13.LossDb != null)
                    res.Loss13 += p.S13.LossDb.Value;
                if (p.S15.LossDb != null)
                    res.Loss15 += p.S15.LossDb.Value;

                if (att13 != null && att13 > Config.Atten1310Max)
                    reasons.Add(Loc.F("reason_att13", Config.Atten1310Max, att13));
                if (att15 != null && att15 > Config.Atten1550Max)
                    reasons.Add(Loc.F("reason_att15", Config.Atten1550Max, att15));

                // Данные должны быть на обеих длинах волн: если участок есть на
                // одной длине волны, а на другой нет ни длины, ни затухания — FAIL
                bool no13 = att13 == null && len13 <= 0;
                bool no15 = att15 == null && len15 <= 0;
                if (no13 && !no15)
                    reasons.Add(Loc.T("reason_no13"));
                if (no15 && !no13)
                    reasons.Add(Loc.T("reason_no15"));

                sa.Verdict = reasons.Count > 0 ? "FAIL" : "PASS";
                if (reasons.Count > 0)
                    res.Fails.Add(Loc.F("reason_section", idx + 1, string.Join(", ", reasons)));
            }

            sa.Reasons = reasons;
            res.Sections.Add(sa);
        }

        var matchedEv = MatchEventsByPosition(
            t13?.Events ?? new List<PointEvent>(),
            t15?.Events ?? new List<PointEvent>(),
            Config.MatchToleranceM);

        for (int evIdx = 0; evIdx < matchedEv.Count; evIdx++)
        {
            var (e13, e15) = (matchedEv[evIdx].E13, matchedEv[evIdx].E15);
            double posM = e15?.PosM ?? e13?.PosM ?? 0.0;
            double? l13 = e13?.LossDb, l15 = e15?.LossDb;
            double? r13 = e13?.ReflDb, r15 = e15?.ReflDb;

            var ea = new EventAnalysis
            {
                Index = evIdx + 1,
                PosM = posM,
                L13 = l13,
                R13 = r13,
                L15 = l15,
                R15 = r15,
            };
            var reasons = new List<string>();
            string role;

            if (evIdx == 0 && posM < FrontPortMaxPosM)
            {
                role = Loc.T("role_port");
            }
            else if (evIdx == matchedEv.Count - 1)
            {
                role = Loc.T("role_end");
            }
            // bufferEndM == 0 означает «буферная катушка не обнаружена» — искать QC-коннектор негде
            else if (bufferEndM > 0 && Math.Abs(posM - bufferEndM) <= Config.MatchToleranceM)
            {
                role = Loc.T("role_qc");
                res.QcLoss13 = l13;
                res.QcLoss15 = l15;

                // П.1: реальная проверка входного разъема ОТК (была настройка без реализации)
                if (Config.CheckQcConnector)
                {
                    if (l13 != null && l13 > Config.QcConnectorLossMax)
                        reasons.Add(Loc.F("reason_qc13", l13, Config.QcConnectorLossMax));
                    if (l15 != null && l15 > Config.QcConnectorLossMax)
                        reasons.Add(Loc.F("reason_qc15", l15, Config.QcConnectorLossMax));
                }
            }
            else
            {
                role = Loc.T("role_splice");
                if (l13 != null && l13 > Config.SpliceLossMax)
                    reasons.Add(Loc.F("reason_splice13", l13, Config.SpliceLossMax));
                if (l15 != null && l15 > Config.SpliceLossMax)
                    reasons.Add(Loc.F("reason_splice15", l15, Config.SpliceLossMax));
            }

            ea.Role = role;
            ea.Verdict = reasons.Count > 0 ? "FAIL" : "PASS";
            if (reasons.Count > 0)
                res.Fails.Add(Loc.F("reason_event_fail", role, posM, string.Join(", ", reasons)));
            ea.Reasons = reasons;
            res.Events.Add(ea);
        }

        // Итоговое удельное затухание DUT = суммарные потери / суммарная длина в км
        res.Att13 = res.Len13 > 0 ? res.Loss13 / (res.Len13 / MetersPerKm) : null;
        res.Att15 = res.Len15 > 0 ? res.Loss15 / (res.Len15 / MetersPerKm) : null;

        // Страховка от неверной настройки has_buffer_spool (замер с буфером и без него
        // из данных трассы неразличимы — это определяется только настройкой стенда):
        if (Config.HasBufferSpool)
        {
            bool bufferFound = res.Sections.Any(s => s.Verdict == "BUFFER");
            if (!bufferFound)
                res.Warnings.Add(Loc.T("warn_buffer_missing"));
            else if (res.Len13 <= 0 && res.Len15 <= 0)
                res.Warnings.Add(Loc.T("warn_all_buffer"));
        }

        res.Verdict = res.Fails.Count > 0 ? "FAIL" : "PASS";
        return res;
    }

    // --------------------------------------------------------------------------
    // Разделение линейных участков волокна и точечных событий
    // --------------------------------------------------------------------------
    public static TraceMetrics ExtractTraceMetrics(Dictionary<string, object?> traceObj)
    {
        // EXFO хранит длину волны в метрах (1.31E-06); 0 = длина волна не указана
        double wlM = TrcParser.AsDouble(TrcParser.Get(traceObj, "Wavelength")) ?? 0.0;
        int wlNm = wlM != 0 ? (int)Math.Round(wlM * NanometersPerMeter) : 0;

        var evTable = TrcParser.AsDict(TrcParser.Get(traceObj, "EventTable"));
        var eventKeys = evTable.Keys
            .Where(k => EventKeyRegex.IsMatch(k))
            .OrderBy(k => long.Parse(k.AsSpan(EventKeyPrefix.Length))) // «Event» -> число в конце ключа
            .ToList();

        var sections = new List<Section>();
        var pointEvents = new List<PointEvent>();

        foreach (string key in eventKeys)
        {
            var evObj = evTable[key] as Dictionary<string, object?>;
            if (evObj == null)
                continue;

            bool isPointEvent =
                evObj.ContainsKey("Reflectance") ||
                evObj.ContainsKey("PeakReflectionToRbs") ||
                evObj.ContainsKey("Type");

            double lengthM = TrcParser.AsDouble(TrcParser.Get(evObj, "Length")) ?? 0.0;

            // линейный участок (LMA): не точечное событие и имеет ненулевую длину
            if (!isPointEvent && lengthM > 0)
            {
                double? lossDb = TrcParser.AsDouble(TrcParser.Get(evObj, "Loss"));
                double posStart = TrcParser.AsDouble(TrcParser.Get(evObj, "Position")) ?? 0.0;
                double? posEnd = evObj.ContainsKey("CursorBPosition")
                    ? TrcParser.AsDouble(TrcParser.Get(evObj, "CursorBPosition"))
                    : posStart + lengthM;

                // П.5: затухание берется из явного поля дерева, если оно есть;
                // иначе рассчитывается как Loss/Length (оценка, включает потери
                // на границах участка — аккуратнее для коротких участков).
                double? attenDbKm = null;
                bool explicitAtten = false;
                foreach (string k in AttenuationKeys)
                {
                    var a = TrcParser.AsDouble(TrcParser.Get(evObj, k));
                    if (a != null)
                    {
                        attenDbKm = a;
                        explicitAtten = true;
                        break;
                    }
                }
                if (!explicitAtten && lossDb != null)
                    attenDbKm = lossDb.Value / (lengthM / MetersPerKm);

                sections.Add(new Section
                {
                    StartM = posStart,
                    EndM = posEnd,
                    LengthM = lengthM,
                    LossDb = lossDb,
                    AttenDbKm = attenDbKm,
                    AttenFromExplicitField = explicitAtten,
                });
            }
            else
            {
                pointEvents.Add(new PointEvent
                {
                    PosM = TrcParser.AsDouble(TrcParser.Get(evObj, "Position")) ?? 0.0,
                    LossDb = TrcParser.AsDouble(TrcParser.Get(evObj, "Loss")),
                    ReflDb = TrcParser.AsDouble(TrcParser.Get(evObj, "Reflectance")),
                    Comment = TrcParser.Get(evObj, "Comment") as string ?? "",
                });
            }
        }

        return new TraceMetrics
        {
            WavelengthNm = wlNm,
            Sections = sections,
            Events = pointEvents,
        };
    }

    // --------------------------------------------------------------------------
    // П.3: сопоставление участков 1310/1550 по координате начала
    // --------------------------------------------------------------------------
    public static List<SectionPair> MatchSectionsByPosition(
        List<Section> sec1310, List<Section> sec1550, double toleranceM)
    {
        var pairs = new List<SectionPair>();
        var used1310 = new HashSet<int>();

        foreach (var s15 in sec1550)
        {
            double start15 = s15.StartM ?? 0.0;
            int bestIdx = -1; // -1 = подходящий участок не найден
            double bestDiff = toleranceM;

            for (int idx = 0; idx < sec1310.Count; idx++)
            {
                if (used1310.Contains(idx))
                    continue;
                double diff = Math.Abs((sec1310[idx].StartM ?? 0.0) - start15);
                if (diff <= bestDiff)
                {
                    bestDiff = diff;
                    bestIdx = idx;
                }
            }

            if (bestIdx >= 0)
            {
                used1310.Add(bestIdx);
                pairs.Add(new SectionPair { S13 = sec1310[bestIdx], S15 = s15 });
            }
            else
            {
                pairs.Add(new SectionPair { S13 = new Section(), S15 = s15 });
            }
        }

        for (int idx = 0; idx < sec1310.Count; idx++)
        {
            if (!used1310.Contains(idx))
                pairs.Add(new SectionPair { S13 = sec1310[idx], S15 = new Section() });
        }

        pairs = pairs.OrderBy(p => PairStart(p)).ToList();
        return pairs;

        static double PairStart(SectionPair p)
            => Math.Min(p.S13.StartM ?? double.MaxValue, p.S15.StartM ?? double.MaxValue);
    }

    // --------------------------------------------------------------------------
    // Сопоставление событий на 1310 и 1550 нм по их координате
    // --------------------------------------------------------------------------
    public static List<EvPair> MatchEventsByPosition(
        List<PointEvent> evList1310, List<PointEvent> evList1550, double toleranceM)
    {
        var matched = new List<EvPair>();
        var used1310 = new HashSet<int>();

        foreach (var e15 in evList1550)
        {
            double pos15 = e15.PosM ?? 0.0;
            int bestIdx = -1;
            double bestDiff = toleranceM;

            for (int idx = 0; idx < evList1310.Count; idx++)
            {
                if (used1310.Contains(idx))
                    continue;
                double diff = Math.Abs((evList1310[idx].PosM ?? 0.0) - pos15);
                if (diff <= bestDiff)
                {
                    bestDiff = diff;
                    bestIdx = idx;
                }
            }

            if (bestIdx >= 0)
            {
                used1310.Add(bestIdx);
                matched.Add(new EvPair { E13 = evList1310[bestIdx], E15 = e15 });
            }
            else
            {
                matched.Add(new EvPair { E13 = null, E15 = e15 });
            }
        }

        for (int idx = 0; idx < evList1310.Count; idx++)
        {
            if (!used1310.Contains(idx))
                matched.Add(new EvPair { E13 = evList1310[idx], E15 = null });
        }

        matched = matched
            .OrderBy(p => p.E15?.PosM ?? p.E13?.PosM ?? 0.0)
            .ToList();

        return matched;
    }

    // --------------------------------------------------------------------------
    // Диагностика формата: список ключей EventTable с значениями (--dump-keys).
    // Позволяет подтвердить реальные имена полей затухания в EXFO .trc.
    // --------------------------------------------------------------------------
    public static string DumpTrc(string filepath)
    {
        byte[] stream = TrcParser.DecompressTrc(filepath);
        var tree = TrcParser.FindRootTree(stream);

        var sb = new StringBuilder();
        sb.AppendLine($"Файл: {Path.GetFileName(filepath)} (распаковано {stream.Length} байт)");
        sb.AppendLine($"Корень OtdrFile: {string.Join(", ", tree.Keys)}");

        var fibers = TrcParser.AsDict(
            TrcParser.Get(tree, "OtdrData") is Dictionary<string, object?> otdr
                ? TrcParser.Get(otdr, "Fibers")
                : null);

        foreach (var fEntry in fibers)
        {
            if (fEntry.Value is not Dictionary<string, object?> fObj)
                continue;

            sb.AppendLine();
            sb.AppendLine($"=== Волокно '{fEntry.Key}' Color='{TrcParser.ValueToString(TrcParser.Get(fObj, "Color"))}' ===");
            sb.AppendLine($"  Ключи волокна: {string.Join(", ", fObj.Keys)}");

            var traces = TrcParser.AsDict(TrcParser.Get(fObj, "Traces"));
            foreach (var tEntry in traces)
            {
                if (tEntry.Value is not Dictionary<string, object?> tObj)
                    continue;

                double wlM = TrcParser.AsDouble(TrcParser.Get(tObj, "Wavelength")) ?? 0.0;
                sb.AppendLine($"  --- Trace '{tEntry.Key}' (Wavelength={wlM:G6} -> {(int)Math.Round(wlM * NanometersPerMeter)} нм) ---");
                sb.AppendLine($"      Ключи трассы: {string.Join(", ", tObj.Keys)}");

                var evTable = TrcParser.AsDict(TrcParser.Get(tObj, "EventTable"));
                var eventKeys = evTable.Keys
                    .Where(k => EventKeyRegex.IsMatch(k))
                    .OrderBy(k => long.Parse(k.AsSpan(EventKeyPrefix.Length)))
                    .ToList();

                foreach (string key in eventKeys)
                {
                    if (evTable[key] is not Dictionary<string, object?> eObj)
                        continue;
                    sb.Append($"      {key}: ");
                    foreach (var kv in eObj)
                        sb.Append($"{kv.Key}={PreviewValue(kv.Value)}; ");
                    sb.AppendLine();
                }

                // Скалярные/служебные ключи трассы (без огромных массивов сэмплов)
                foreach (var kv in tObj)
                {
                    if (kv.Key is "EventTable" or "RawSamples" or "AcquisitionDatas" or "ModuleInformation" or "SoftwareContext")
                        continue;
                    sb.AppendLine($"      [{kv.Key}] = {Truncate(PreviewValue(kv.Value), TraceValuePreviewMaxLength)}");
                }
            }
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private static string PreviewValue(object? v, int depth = 0)
    {
        if (v is Dictionary<string, object?> d)
        {
            if (depth >= PreviewMaxDepth)
                return "{{...}}";
            return "{" + string.Join("; ", d.Select(kv => $"{kv.Key}={PreviewValue(kv.Value, depth + 1)}")) + "}";
        }
        return v switch
        {
            double dv => dv.ToString("G6", CultureInfo.InvariantCulture),
            int iv => iv.ToString(CultureInfo.InvariantCulture),
            bool bv => bv ? "True" : "False",
            string sv => "\"" + (sv.Length > StringPreviewMaxLength ? sv[..StringPreviewMaxLength] + "…" : sv) + "\"",
            null => "null",
            _ => v.ToString() ?? "",
        };
    }
}
