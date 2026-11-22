// ==============================================================================
// Models.cs — модели строк таблиц GUI.
//
// Повторяют колонки трёх CSV консольной версии (summary_fibers /
// sections_detail / events_detail), чтобы перенос результатов был прямым
// «колонка в колонку». Числа — double? (null = нет данных, показывается пусто).
// ==============================================================================
using System.ComponentModel;

namespace ExfoTrcAnalyzerGUI;

// Atn — «пограничное затухание»: PASS, но затухание в жёлтой зоне (±N% от порога)
public enum Verdict { None, Pass, Atn, Fail }

// Вспомогательные флаги для DataTrigger в XAML (триггер по enum ненадёжен)
public interface IVerdictRow
{
    bool IsPass { get; }
    bool IsFail { get; }
}

// Порядок сортировки по колонке «Вердикт»: 1-й клик (по возрастанию) —
// FAIL наверху, 2-й клик (по убыванию) — PASS наверху.
// None/BUFFER — в конец в обоих случаях нельзя, пусть будут после PASS.
public static class VerdictSort
{
    public static int Rank(bool isFail, bool isPass) => isFail ? 0 : isPass ? 1 : 2;
}

// Цвет кружка в списке файлов
public enum DotStatus { Gray, Green, Yellow, Red }

// Элемент списка файлов слева.
// INotifyPropertyChanged — обязателен: вердикт/кружок меняются ПОСЛЕ того,
// как файл уже добавлен в список (по окончании анализа). Без них кружки
// остались бы серыми навсегда.
public class FileEntry : INotifyPropertyChanged
{
    public string Path { get; set; } = "";
    public string Name => System.IO.Path.GetFileName(Path);

    // Модуль кабеля (разбирается из имени папки вида «02579 OBgPo-96A8(8x12)-2,7»)
    public int? ModuleNo { get; set; }
    public string? CableTag { get; set; } // «02579 OBgPo» — префикс при нескольких кабелях

    // Служебная строка «Все» в начале списка: сбрасывает фильтр таблиц
    public bool IsAllEntry { get; init; }
    public string DisplayName => IsAllEntry ? Loc.T("all_entry") : Name;

    private Verdict _verdict;
    public Verdict Verdict
    {
        get => _verdict;
        set { _verdict = value; OnChanged(nameof(Verdict)); }
    }

    private DotStatus _dot;
    public DotStatus Dot
    {
        get => _dot;
        set { _dot = value; OnChanged(nameof(Dot)); }
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        set { _error = value; OnChanged(nameof(Error)); OnChanged(nameof(TooltipText)); }
    }

    private string? _note;
    public string? Note // пояснение к кружку (например «затухание на границе нормы»)
    {
        get => _note;
        set { _note = value; OnChanged(nameof(Note)); OnChanged(nameof(TooltipText)); }
    }

    // Подсказка в списке файлов: заметка + ошибка (или null, если пусто)
    public string? TooltipText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Note)) parts.Add(Note);
            if (!string.IsNullOrWhiteSpace(Error)) parts.Add(Loc.T("tip_error_prefix") + Error);
            return parts.Count == 0 ? null : string.Join("\n", parts);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new(name));

    // Смена языка: перечитать вычисляемые локализованные свойства (список файлов)
    public void RefreshLocalized()
    {
        OnChanged(nameof(DisplayName));
        OnChanged(nameof(TooltipText));
    }
}

// summary_fibers.csv
public class FiberRow : IVerdictRow, INotifyPropertyChanged
{
    public bool Borderline { get; set; } // затухание в «жёлтой зоне» у порога
    public string File { get; set; } = "";
    public string FiberId { get; set; } = "";
    public double? Len13 { get; set; }
    public double? Loss13 { get; set; }
    public double? Atten13 { get; set; }
    public double? Len15 { get; set; }
    public double? Loss15 { get; set; }
    public double? Atten15 { get; set; }
    public double? Qc13 { get; set; }
    public double? Qc15 { get; set; }
    public Verdict Verdict { get; set; }
    public bool IsPass => Verdict == Verdict.Pass;
    public bool IsFail => Verdict == Verdict.Fail;
    // ATTN между FAIL и PASS: FAIL наверху, затем пограничные, затем PASS
    public int VerdictSortRank => Verdict switch { Verdict.Fail => 0, Verdict.Atn => 1, Verdict.Pass => 2, _ => 3 };
    public string Fails { get; set; } = "";
    public string Warnings { get; set; } = "";

    // Ключ сортировки «непустые значения сверху»: пустая строка получает
    // значение \uffff — после любого текста при возрастании
    public string FailsSortKey => string.IsNullOrWhiteSpace(Fails) ? "\uffff" : Fails;
    public string WarningsSortKey => string.IsNullOrWhiteSpace(Warnings) ? "\uffff" : Warnings;

    // Модуль кабеля: группировка строк во вкладке «Волокна» (null — модуль неизвестен)
    public int? ModuleNo { get; set; }
    public string? CableTag { get; set; }

    // Заголовок группы (пересчитывается при смене языка/кабелей). INPC обязателен:
    // ListCollectionView пересобирает группы только по уведомлению о смене свойства
    private string? _module;
    public string? Module
    {
        get => _module;
        set
        {
            if (_module == value)
                return;
            _module = value;
            PropertyChanged?.Invoke(this, new(nameof(Module)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

// sections_detail.csv
public class SectionRow : IVerdictRow
{
    public bool Borderline { get; set; } // затухание участка в «жёлтой зоне» (вердикт ATTN)
    public string File { get; set; } = "";
    public string FiberId { get; set; } = "";
    public int Index { get; set; }
    public string Role { get; set; } = "";
    public double? StartM { get; set; }
    public double? EndM { get; set; }
    public double? Len13 { get; set; }
    public double? Loss13 { get; set; }
    public double? Atten13 { get; set; }
    public double? Len15 { get; set; }
    public double? Loss15 { get; set; }
    public double? Atten15 { get; set; }
    public Verdict Verdict { get; set; }
    public bool IsPass => Verdict == Verdict.Pass;
    public bool IsFail => Verdict == Verdict.Fail;
    // ATTN между FAIL и PASS (см. FiberRow.VerdictSortRank)
    public int VerdictSortRank => Verdict switch { Verdict.Fail => 0, Verdict.Atn => 1, Verdict.Pass => 2, _ => 3 };
    public string Reason { get; set; } = "";

    // Ключ сортировки «непустые значения сверху» (см. FiberRow.FailsSortKey)
    public string ReasonSortKey => string.IsNullOrWhiteSpace(Reason) ? "\uffff" : Reason;
}

// events_detail.csv
public class EventRow : IVerdictRow
{
    public string File { get; set; } = "";
    public string FiberId { get; set; } = "";
    public int Index { get; set; }
    public string Role { get; set; } = "";
    public double? CoordM { get; set; }
    public double? Loss13 { get; set; }
    public double? Refl13 { get; set; }
    public double? Loss15 { get; set; }
    public double? Refl15 { get; set; }
    public Verdict Verdict { get; set; }
    public bool IsPass => Verdict == Verdict.Pass;
    public bool IsFail => Verdict == Verdict.Fail;
    public int VerdictSortRank => VerdictSort.Rank(IsFail, IsPass);
    public string Reason { get; set; } = "";

    // Ключ сортировки «непустые значения сверху» (см. FiberRow.FailsSortKey)
    public string ReasonSortKey => string.IsNullOrWhiteSpace(Reason) ? "\uffff" : Reason;
}