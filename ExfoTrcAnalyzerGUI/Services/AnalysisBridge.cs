// ==============================================================================
// AnalysisBridge.cs — прослойка между GUI и скопированным движком
// (Engine\TrcParser|Analyzer|Csv|Config — код 1-в-1 как в консольной версии).
//
//   ApplySettingsToEngine — переносит порог из AppSettings в Config движка
//     перед КАЖДЫМ анализом (пороги можно менять окном «Настройки» на лету);
//   ToRow* — превращают FiberAnalysis/SectionAnalysis/EventAnalysis в модели
//     таблиц GUI (те же поля, что в CSV консольной версии);
//   ExportCsv — выгрузка трёх таблиц в CSV точь-в-точь как консольная
//     summary_fibers / sections_detail / events_detail (';', запятая, UTF-8 BOM).
// ==============================================================================
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ExfoTrcAnalyzer;

namespace ExfoTrcAnalyzerGUI;

internal static class AnalysisBridge
{
    public static void ApplySettingsToEngine()
    {
        var s = AppSettings.Current;
        Config.HasBufferSpool = s.HasBufferSpool;
        Config.BufferMinM = s.BufferMinM;
        Config.BufferMaxM = s.BufferMaxM;
        Config.Atten1310Max = s.Atten1310Max;
        Config.Atten1550Max = s.Atten1550Max;
        Config.SpliceLossMax = s.SpliceLossMax;
        Config.CheckQcConnector = s.CheckQcConnector;
        Config.QcConnectorLossMax = s.QcConnectorLossMax;
        Config.MatchToleranceM = s.MatchToleranceM;
    }

    public static Verdict ToVerdict(string verdict)
        => verdict switch
        {
            "PASS" => Verdict.Pass,
            "FAIL" => Verdict.Fail,
            _ => Verdict.None,
        };

    // «Жёлтая зона»: значение затухания в пределах ±percent% от порога
    // (например порог 0.36 дБ/км, 5% -> жёлтый при 0.342..0.378)
    public static bool AttBorderline(double? attenuation, double thresholdMax, double percent)
        => attenuation.HasValue
        && percent > 0
        && Math.Abs(attenuation.Value - thresholdMax) <= thresholdMax * percent / 100.0;

    public static FiberRow ToRow(string filename, FiberAnalysis f) => new()
    {
        File = filename,
        FiberId = f.FiberId,
        Len13 = f.Len13, Loss13 = f.Loss13, Atten13 = f.Att13,
        Len15 = f.Len15, Loss15 = f.Loss15, Atten15 = f.Att15,
        Qc13 = f.QcLoss13, Qc15 = f.QcLoss15,
        Verdict = ToVerdict(f.Verdict),
        Fails = string.Join(" | ", f.Fails),
        Warnings = string.Join(" | ", f.Warnings),
    };

    public static SectionRow ToRow(string filename, string fiberId, SectionAnalysis s) => new()
    {
        File = filename,
        FiberId = fiberId,
        Index = s.Index,
        Role = s.Role,
        StartM = s.StartM,
        EndM = s.EndM,
        Len13 = s.S13.LengthM, Loss13 = s.S13.LossDb, Atten13 = s.S13.AttenDbKm,
        Len15 = s.S15.LengthM, Loss15 = s.S15.LossDb, Atten15 = s.S15.AttenDbKm,
        Verdict = ToVerdict(s.Verdict),
        Reason = string.Join("; ", s.Reasons),
    };

    public static EventRow ToRow(string filename, string fiberId, EventAnalysis e) => new()
    {
        File = filename,
        FiberId = fiberId,
        Index = e.Index,
        Role = e.Role,
        CoordM = e.PosM,
        Loss13 = e.L13, Refl13 = e.R13,
        Loss15 = e.L15, Refl15 = e.R15,
        Verdict = ToVerdict(e.Verdict),
        Reason = string.Join("; ", e.Reasons),
    };

    // ---------- Экспорт CSV (формат идентичен консольной версии) ----------
    public static void ExportCsv(string outputDir,
        ObservableCollection<FiberRow> fibers,
        ObservableCollection<SectionRow> sections,
        ObservableCollection<EventRow> events)
    {
        // колонка «Модуль» — только если модули кабеля определены (группировка)
        bool withModule = fibers.Any(f => f.Module != null);
        var fHeaders = new List<string>();
        if (withModule) fHeaders.Add(Loc.T("col_module"));
        fHeaders.AddRange(new[]
        {
            Loc.T("col_file"), Loc.T("col_fiber_id"),
            Loc.T("col_len13"), Loc.T("col_loss13"), Loc.T("col_att13"),
            Loc.T("col_len15"), Loc.T("col_loss15"), Loc.T("col_att15"),
            Loc.T("col_qc13"), Loc.T("col_qc15"),
            Loc.T("col_verdict"), Loc.T("col_fails"), Loc.T("col_warnings"),
        });
        Csv.SaveCsv(Path.Combine(outputDir, "summary_fibers.csv"),
            fHeaders.ToArray(),
            fibers.Select(f =>
            {
                var cells = new List<string>();
                if (withModule) cells.Add(f.Module ?? "");
                cells.Add(f.File); cells.Add(f.FiberId);
                cells.Add(Csv.Fmt(f.Len13, 1)); cells.Add(Csv.Fmt(f.Loss13, 3)); cells.Add(Csv.Fmt(f.Atten13, 3));
                cells.Add(Csv.Fmt(f.Len15, 1)); cells.Add(Csv.Fmt(f.Loss15, 3)); cells.Add(Csv.Fmt(f.Atten15, 3));
                cells.Add(Csv.Fmt(f.Qc13, 3)); cells.Add(Csv.Fmt(f.Qc15, 3));
                cells.Add(f.Verdict switch { Verdict.Pass => "PASS", Verdict.Atn => "ATTN", Verdict.Fail => "FAIL", _ => "" });
                cells.Add(f.Fails); cells.Add(f.Warnings);
                return cells.ToArray();
            }).ToList());

        Csv.SaveCsv(Path.Combine(outputDir, "sections_detail.csv"),
            new[]
            {
                Loc.T("col_file"), Loc.T("col_fiber_id"), Loc.T("col_section_no"), Loc.T("col_role"), Loc.T("col_start"), Loc.T("col_end"),
                Loc.T("col_len13"), Loc.T("col_loss13"), Loc.T("col_att13"),
                Loc.T("col_len15"), Loc.T("col_loss15"), Loc.T("col_att15"),
                Loc.T("col_verdict"), Loc.T("col_reason"),
            },
            sections.Select(s => new[]
            {
                s.File, s.FiberId, s.Index.ToString(), s.Role,
                Csv.Fmt(s.StartM, 1), Csv.Fmt(s.EndM, 1),
                Csv.Fmt(s.Len13, 1), Csv.Fmt(s.Loss13, 3), Csv.Fmt(s.Atten13, 3),
                Csv.Fmt(s.Len15, 1), Csv.Fmt(s.Loss15, 3), Csv.Fmt(s.Atten15, 3),
                s.Verdict switch { Verdict.Pass => "PASS", Verdict.Atn => "ATTN", Verdict.Fail => "FAIL", _ => "" },
                s.Reason,
            }).ToList());

        Csv.SaveCsv(Path.Combine(outputDir, "events_detail.csv"),
            new[]
            {
                Loc.T("col_file"), Loc.T("col_fiber_id"), Loc.T("col_event_no"), Loc.T("col_event_role"), Loc.T("col_coord"),
                Loc.T("col_loss13"), Loc.T("col_refl13"), Loc.T("col_loss15"),
                Loc.T("col_refl15"), Loc.T("col_verdict"), Loc.T("col_reason"),
            },
            events.Select(e => new[]
            {
                e.File, e.FiberId, e.Index.ToString(), e.Role, Csv.Fmt(e.CoordM, 1),
                Csv.Fmt(e.Loss13, 3), Csv.Fmt(e.Refl13, 1), Csv.Fmt(e.Loss15, 3), Csv.Fmt(e.Refl15, 1),
                e.Verdict switch { Verdict.Pass => "PASS", Verdict.Fail => "FAIL", _ => "" },
                e.Reason,
            }).ToList());
    }
}
