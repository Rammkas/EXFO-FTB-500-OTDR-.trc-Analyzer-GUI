// ==============================================================================
// MainWindow.xaml.cs — логика главного окна GUI-анализатора.
//
// Открытие файла/папки -> анализ движком Engine\Analyzer (через AnalysisBridge),
// результаты в трёх таблицах; повторный анализ при сохранении настроек;
// экспорт трёх CSV (формат консольной версии). Фильтрация — ICollectionView
// (выбранный файл + поиск), сортировка по клику на заголовок колонки.
//
// Анализ выполняется в Task.Run (UI не подвисает на больших пачках), добавление
// строк — в UI-потоке после await.
// ==============================================================================
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExfoTrcAnalyzer;
using Microsoft.Win32;

namespace ExfoTrcAnalyzerGUI.Windows;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<FileEntry> _files = new();
    private readonly ObservableCollection<FiberRow> _fibers = new();
    private readonly ObservableCollection<SectionRow> _sections = new();
    private readonly ObservableCollection<EventRow> _events = new();

    private ICollectionView _fibersView = null!;
    private ICollectionView _sectionsView = null!;
    private ICollectionView _eventsView = null!;

    // Имя выбранного файла (null = показать все)
    private string? _selectedFileName;

    // Кэш результатов и сырых кривых по файлам (для вкладки «Карта»)
    private readonly Dictionary<string, List<FiberAnalysis>> _fileAnalyses = new();
    private readonly Dictionary<string, List<RawTrace.TraceCurve>?> _fileCurves = new();
    private FiberAnalysis? _mapFiber;
    private List<RawTrace.TraceCurve>? _mapCurves;
    private string? _mapFile;

    // Окно просмотра карты (зум/панорамирование по расстоянию); null = весь диапазон
    private double? _mapViewStart;
    private double? _mapViewEnd;

    // Фиксированное окно дБ (автоподстройка оси Y выключена): считается по всей трассе
    private double? _mapDbMin;
    private double? _mapDbMax;
    private bool _mapDragging;
    private double _dragStartX, _dragViewStart, _dragSpan;

    // Курсоры-измерители А/В на карте (позиция в метрах);
    // _placingCursor ('A'/'B') — кнопка нажата, ждём клик по графику
    private double? _cursorA, _cursorB;
    private char? _placingCursor;
    private char? _dragCursor; // Ctrl + ЛКМ по линии курсора — перетаскивание

    // Сворачивание списка файлов по горизонтали (стрелка на границе панели)
    private bool _leftCollapsed;
    private GridLength _leftSavedWidth = new(220);

    // Защита от рекурсии при синхронизации выбора «список файлов <-> Волокна»
    private bool _selectionSync;

    // Служебная первая строка списка «Все»: клик по ней снимает фильтр
    private readonly FileEntry _allEntry = new() { IsAllEntry = true };

    public MainWindow()
    {
        InitializeComponent();

        FileList.ItemsSource = _files;
        FibersGrid.ItemsSource = _fibers;
        SectionsGrid.ItemsSource = _sections;
        EventsGrid.ItemsSource = _events;

        // Представления для фильтрации (файл + поиск)
        _fibersView = CollectionViewSource.GetDefaultView(_fibers);
        _sectionsView = CollectionViewSource.GetDefaultView(_sections);
        _eventsView = CollectionViewSource.GetDefaultView(_events);
        _fibersView.Filter = o => MatchFile(((FiberRow)o).File) && MatchSearch(((FiberRow)o).File, ((FiberRow)o).FiberId, ((FiberRow)o).Fails, ((FiberRow)o).Warnings);
        _sectionsView.Filter = o => MatchFile(((SectionRow)o).File) && MatchSearch(((SectionRow)o).File, ((SectionRow)o).FiberId, ((SectionRow)o).Role, ((SectionRow)o).Reason);
        _eventsView.Filter = o => MatchFile(((EventRow)o).File) && MatchSearch(((EventRow)o).File, ((EventRow)o).FiberId, ((EventRow)o).Role, ((EventRow)o).Reason);

        // «Все» — всегда первая строка списка, выбрана по умолчанию
        _files.Add(_allEntry);
        FileList.SelectedIndex = 0;

        UpdateQcColumnsVisibility();
        UpdateLossColumnsVisibility();
        RefreshTableHeaders(); // заголовки колонок — от текущего языка (см. метод)

        // Стрелка ▲/▼ в тексте заголовка — реакция на изменение SortDirection колонки
        // (работает независимо от того, кто установил направление)
        HookSortArrows();
        UpdateStatus();

        // При старте панель «Файлы» свёрнута влево; раскроется при загрузке файла/папки
        ToggleFileList();
    }

    // Колонки «Втрати … (дБ)» во всех трёх вкладках — по настройке «Показывать Потери»
    // (по x:Name, а не по заголовку: заголовки теперь локализуются)
    private void UpdateLossColumnsVisibility()
    {
        var v = AppSettings.Current.ShowLosses ? Visibility.Visible : Visibility.Collapsed;
        foreach (var col in new[] { FLoss13Column, FLoss15Column, SLoss13Column, SLoss15Column, ELoss13Column, ELoss15Column })
            col.Visibility = v;
    }

    // ---------- Открытие файла ----------
    private const string BaseTitle = "EXFO TRC Analyzer";

    private async void BtnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("dlg_open_file"),
            Filter = Loc.T("filter_trc"),
            Multiselect = true, // можно выбрать несколько файлов (Ctrl/Shift в диалоге)
        };
        if (dlg.ShowDialog(this) != true)
            return;

        // порядок — как при открытии папки: по имени файла (Ordinal)
        foreach (var path in dlg.FileNames.OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            var entry = AddFile(path);
            if (entry != null)
                await AnalyzeEntryAsync(entry);
        }
    }

    // ---------- Открытие папки ----------
    private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = Loc.T("dlg_open_folder"),
            Multiselect = true, // можно выбрать несколько папок (Ctrl/Shift в диалоге)
        };
        if (dlg.ShowDialog(this) != true)
            return;

        foreach (string folder in dlg.FolderNames)
        {
            // Имя загруженной папки — в заголовок (без повторов, порядок загрузки)
            string folderName = new DirectoryInfo(folder).Name;
            if (!_titleFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                _titleFolders.Add(folderName);
            UpdateTitle();

            // Тот же порядок, что у консольной версии: по имени файла (Ordinal)
            var trcFiles = Directory.GetFiles(folder, "*.trc")
                                    .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                                    .ToList();
            // Группировка по модулям кабеля (если имя папки описывает структуру кабеля)
            var moduleMap = BuildFolderModuleMap(folder, trcFiles);
            foreach (string path in trcFiles)
            {
                var entry = AddFile(path);
                if (entry != null)
                {
                    if (moduleMap != null && moduleMap.TryGetValue(path, out var mi))
                    {
                        entry.ModuleNo = mi.no;
                        entry.CableTag = mi.tag;
                    }
                    await AnalyzeEntryAsync(entry);
                }
            }
        }
        // Окно-уведомление не показываем: счётчики файлов и так видны в статус-баре
    }

    // Разбор имени папки вида «02579 OBgPo-96A8(8x12)-2,7»:
    //   (8x12) -> 8 модулей по 12 волокон; без скобок — «96A8»: 96 волокон в 8 модулях.
    // Нумерация волокон может иметь разрывы, поэтому модули определяются по ПОРЯДКУ:
    // все волокна папки сортируются по номеру файла и режутся подряд по `per` штук
    // (первые 12 — модуль 1, следующие 12 — модуль 2, ...). Группировка включается
    // только когда файлов ровно столько, сколько заявлено в имени (mods*per).
    private static Dictionary<string, (int no, string tag)>? BuildFolderModuleMap(
        string folder, List<string> trcFiles)
    {
        if (trcFiles.Count == 0)
            return null;
        string folderName = new DirectoryInfo(folder).Name;

        int mods = 0, per = 0, total = 0;
        var m = Regex.Match(folderName, @"\((\d{1,3})\s*[xх×]\s*(\d{1,3})\)");
        if (m.Success)
        {
            mods = int.Parse(m.Groups[1].Value);
            per = int.Parse(m.Groups[2].Value);
            total = mods * per;
        }
        else
        {
            m = Regex.Match(folderName, @"(\d{2,4})[A-Za-z](\d{1,2})(?!\d)");
            if (m.Success)
            {
                total = int.Parse(m.Groups[1].Value);
                mods = int.Parse(m.Groups[2].Value);
                if (mods > 0 && total % mods == 0)
                    per = total / mods;
            }
        }
        if (mods <= 0 || per <= 0)
            return null;

        var nums = new List<(string path, int num)>();
        foreach (var p in trcFiles)
        {
            var nm = Regex.Match(Path.GetFileName(p), @"^0*(\d{2,5})");
            if (!nm.Success)
                return null; // есть файл без номера — нумерация не подтверждается
            nums.Add((p, int.Parse(nm.Groups[1].Value)));
        }

        // ровно столько волокон, сколько заявлено в имени папки
        if (nums.Count != total)
            return null;

        string tag = Regex.Match(folderName, @"^(.+?)-").Groups[1].Value; // «02579 OBgPo»
        var map = new Dictionary<string, (int, string)>();
        // сортировка по номеру волокна -> режем подряд по per
        var ordered = nums.OrderBy(n => n.num).ToList();
        for (int i = 0; i < ordered.Count; i++)
            map[ordered[i].path] = (i / per + 1, tag);
        return map;
    }

    // Описание попадания затухания в «жёлтую зону» (null — не пограничное);
    // key — ключ локализации note_att_avg / note_att_section
    private static string? BorderlineNote(double? att, double threshold, double percent, string wl, string key)
        => AnalysisBridge.AttBorderline(att, threshold, percent)
            ? Loc.F(key, wl, att, threshold, percent)
            : null;

    private FileEntry? AddFile(string path)
    {
        var existing = _files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return null; // без дублей
        if (_leftCollapsed)
            ToggleFileList(); // загрузка файла/папки — автоматически раскрыть панель «Файлы»
        var entry = new FileEntry { Path = path };
        _files.Add(entry);
        UpdateStatus();
        return entry;
    }

    // Имена папок, загруженных кнопкой «Відкрити папку…» — в заголовок окна:
    // «EXFO TRC Analyzer - <папка1> | <папка2>…». Одиночные файлы («Відкрити
    // файл…») заголовок не меняют. «Очистить» возвращает базовый заголовок.
    private readonly List<string> _titleFolders = [];

    private void UpdateTitle() =>
        Title = _titleFolders.Count == 0
            ? BaseTitle
            : BaseTitle + " - " + string.Join(" | ", _titleFolders);

    // ---------- Анализ одного файла (движок консольной версии) ----------
    private async Task AnalyzeEntryAsync(FileEntry entry)
    {
        string filename = entry.Name;
        try
        {
            AnalysisBridge.ApplySettingsToEngine();
            // Разбор — вне UI-потока; добавление строк ниже — в UI-потоке
            var fibers = await Task.Run(() => Analyzer.AnalyzeFile(entry.Path));

            var settings = AppSettings.Current;

            // Строки этого файла заменяются целиком (переанализ после смены порогов)
            RemoveRowsOfFile(filename);
            bool anyBorderline = false;
            foreach (var f in fibers)
            {
                // «Жёлтая зона»: затухание любой длины волны в ±N% от своего порога
                bool borderline =
                    AnalysisBridge.AttBorderline(f.Att13, settings.Atten1310Max, settings.BorderlinePercent) ||
                    AnalysisBridge.AttBorderline(f.Att15, settings.Atten1550Max, settings.BorderlinePercent);
                anyBorderline |= borderline;

                var fiberRow = AnalysisBridge.ToRow(filename, f);
                fiberRow.Borderline = borderline;
                fiberRow.ModuleNo = entry.ModuleNo; // модуль кабеля (вкладка «Волокна»)
                fiberRow.CableTag = entry.CableTag;
                // метку группы ставим ДО добавления строки: ListCollectionView
                // определяет группу по значению свойства в момент добавления
                fiberRow.Module = ModuleLabel(entry.ModuleNo, entry.CableTag, CurrentModuleMultiTag(),
                    entry.ModuleNo != null || _fibers.Any(r => r.ModuleNo != null));
                // Пограничное затухание: PASS в жёлтой зоне -> вердикт ATTN,
                // в «Причины отбраковки» — что именно попало в жёлтую зону
                if (borderline && fiberRow.Verdict == Verdict.Pass)
                {
                    fiberRow.Verdict = Verdict.Atn;
                    var notes = new List<string?>
                    {
                        BorderlineNote(fiberRow.Atten13, settings.Atten1310Max, settings.BorderlinePercent, "1310", "note_att_avg"),
                        BorderlineNote(fiberRow.Atten15, settings.Atten1550Max, settings.BorderlinePercent, "1550", "note_att_avg"),
                    }.Where(n => n != null).ToList();
                    if (fiberRow.Fails.Length > 0)
                        notes.Insert(0, fiberRow.Fails);
                    fiberRow.Fails = string.Join(" | ", notes);
                }
                _fibers.Add(fiberRow);
                foreach (var s in f.Sections)
                {
                    var sRow = AnalysisBridge.ToRow(filename, f.FiberId, s);
                    // Жёлтая зона проверяется и по каждому участку отдельно:
                    // PASS участка в жёлтой зоне -> ATTN + пояснение в «Причине»
                    if (sRow.Verdict == Verdict.Pass)
                    {
                        var parts = new List<string?>
                        {
                            BorderlineNote(sRow.Atten13, settings.Atten1310Max, settings.BorderlinePercent, "1310", "note_att_section"),
                            BorderlineNote(sRow.Atten15, settings.Atten1550Max, settings.BorderlinePercent, "1550", "note_att_section"),
                        }.Where(n => n != null).ToList();
                        if (parts.Count > 0)
                        {
                            sRow.Verdict = Verdict.Atn;
                            sRow.Borderline = true;
                            sRow.Reason = string.Join("; ", parts);
                        }
                    }
                    _sections.Add(sRow);
                }
                foreach (var ev in f.Events)
                    _events.Add(AnalysisBridge.ToRow(filename, f.FiberId, ev));
            }

            _fileAnalyses[filename] = fibers;

            if (fibers.Count == 0)
            {
                entry.Verdict = Verdict.None;
                entry.Dot = DotStatus.Gray;
                entry.Note = null;
                entry.Error = Loc.T("err_no_fibers");
            }
            else
            {
                bool anyFail = fibers.Any(f => f.Verdict == "FAIL");
                entry.Verdict = anyFail ? Verdict.Fail : Verdict.Pass;
                entry.Error = null;

                // Кружок: жёлтый — затухание у границы нормы (±N% от порога),
                // красный — FAIL без пограничного затухания, зелёный — PASS с запасом
                if (anyBorderline)
                {
                    entry.Dot = DotStatus.Yellow;
                    entry.Note = Loc.F("entry_note_borderline", settings.BorderlinePercent);
                }
                else if (anyFail)
                {
                    entry.Dot = DotStatus.Red;
                    entry.Note = null;
                }
                else
                {
                    entry.Dot = DotStatus.Green;
                    entry.Note = null;
                }
            }
        }
        catch (Exception ex)
        {
            entry.Verdict = Verdict.None;
            entry.Dot = DotStatus.Gray;
            entry.Note = null;
            entry.Error = ex.Message;
        }
        UpdateStatus();
        // строки добавляются напрямую в коллекции (без RefreshViews) —
        // пересчитать сортируемость колонок здесь, иначе она «уснёт» после загрузки
        UpdateColumnSortability();
        UpdateWarningsColumn();
    }

    // Заголовки колонок назначаем кодом: DataGridColumn — не часть визуального
    // дерева, и DynamicResource в его Header при подмене словаря языка не обновляется
    private void RefreshTableHeaders()
    {
        SetHeaders(FibersGrid,
            ("FiberId", "col_fiber_id"), ("Len13", "col_len13"), ("Loss13", "col_loss13"),
            ("Atten13", "col_att13"), ("Len15", "col_len15"), ("Loss15", "col_loss15"),
            ("Atten15", "col_att15"), ("Qc13", "col_qc13"), ("Qc15", "col_qc15"),
            ("Verdict", "col_verdict"), ("Fails", "col_fails"), ("Warnings", "col_warnings"));
        SetHeaders(SectionsGrid,
            ("FiberId", "col_fiber_id"), ("Index", "col_section_no"), ("Role", "col_role"),
            ("StartM", "col_start"), ("EndM", "col_end"),
            ("Len13", "col_len13"), ("Loss13", "col_loss13"), ("Atten13", "col_att13"),
            ("Len15", "col_len15"), ("Loss15", "col_loss15"), ("Atten15", "col_att15"),
            ("Verdict", "col_verdict"), ("Reason", "col_reason"));
        SetHeaders(EventsGrid,
            ("FiberId", "col_fiber_id"), ("Index", "col_event_no"), ("Role", "col_event_role"),
            ("CoordM", "col_coord"), ("Loss13", "col_loss13"), ("Refl13", "col_refl13"),
            ("Loss15", "col_loss15"), ("Refl15", "col_refl15"), ("Reason", "col_reason"));

        // вычисляемые строки списка файлов («Все», подсказки) — перечитать
        foreach (var f in _files)
            f.RefreshLocalized();
    }

    private static void SetHeaders(DataGrid grid, params (string path, string key)[] map)
    {
        foreach (var c in grid.Columns)
            if (c is DataGridBoundColumn bc && bc.Binding is Binding b)
                foreach (var (path, key) in map)
                    if (b.Path.Path == path)
                    {
                        c.Header = Loc.T(key);
                        break;
                    }
    }

    // Колонки «Предупреждения», «Причины отбраковки» и «Причина» (участки/события):
    // скрыты, если ни у одной строки нет значений, и появляются, если есть хотя бы одно
    private void UpdateWarningsColumn()
    {
        if (ColWarnings != null)
            ColWarnings.Visibility = _fibers.Any(f => !string.IsNullOrWhiteSpace(f.Warnings))
                ? Visibility.Visible : Visibility.Collapsed;
        if (ColFails != null)
            ColFails.Visibility = _fibers.Any(f => !string.IsNullOrWhiteSpace(f.Fails))
                ? Visibility.Visible : Visibility.Collapsed;
        if (ColSectionsReason != null)
            ColSectionsReason.Visibility = _sections.Any(s => !string.IsNullOrWhiteSpace(s.Reason))
                ? Visibility.Visible : Visibility.Collapsed;
        if (ColEventsReason != null)
            ColEventsReason.Visibility = _events.Any(ev => !string.IsNullOrWhiteSpace(ev.Reason))
                ? Visibility.Visible : Visibility.Collapsed;

        RecomputeModuleLabels();
    }

    // ---------- Группировка волокон по модулям кабеля (вкладка «Волокна») ----------
    // Включается, когда модули определены для всех строк (папка с именем вида
    // «02579 OBgPo-96A8(8x12)-2,7»); при нескольких кабелях заголовок группы
    // получает префикс с именем кабеля.
    private PropertyGroupDescription? _moduleGroupDesc;

    private void RecomputeModuleLabels()
    {
        bool multiTag = CurrentModuleMultiTag();
        // группы включаем, когда модули есть хотя бы у части строк (папка-кабель +
        // одиночные файлы); строки без модуля попадают в группу «Без модуля»
        bool anyModules = _fibers.Any(r => r.ModuleNo != null);
        bool changed = false;
        foreach (var r in _fibers)
        {
            string? label = ModuleLabel(r.ModuleNo, r.CableTag, multiTag, anyModules);
            if (r.Module != label)
            {
                r.Module = label;
                changed = true;
            }
        }

        bool group = anyModules;
        if (group && _moduleGroupDesc == null)
            _fibersView.GroupDescriptions.Add(_moduleGroupDesc = new PropertyGroupDescription(nameof(FiberRow.Module)));
        else if (!group && _moduleGroupDesc != null)
        {
            _fibersView.GroupDescriptions.Remove(_moduleGroupDesc);
            _moduleGroupDesc = null;
        }

        // INPC сам по себе группы НЕ пересобирает (проверено тестом):
        // после смены меток (появление/исчезновение второго кабеля, смена языка)
        // нужен явный Refresh() — иначе строки остаются в старых группах
        if (changed && _moduleGroupDesc != null)
            _fibersView.Refresh();
    }

    private bool CurrentModuleMultiTag() =>
        _files.Where(f => f.ModuleNo != null).Select(f => f.CableTag).Distinct().Count() > 1;

    private static string? ModuleLabel(int? moduleNo, string? tag, bool multiTag, bool anyModules)
    {
        if (moduleNo == null)
            // при загруженных кабелях строки без модуля — в отдельную группу
            return anyModules ? Loc.T("module_none") : null;
        // «Модуль 1 — Синий»: цвет по стандарту TIA-598 с учётом порядка из настроек
        int colorIdx = AppSettings.Current.ColorForModule(moduleNo.Value);
        string label = $"{Loc.F("module_fmt", moduleNo)} — {Loc.T($"color_{colorIdx}")}";
        return multiTag && !string.IsNullOrEmpty(tag) ? $"{tag} · {label}" : label;
    }

    private void RemoveRowsOfFile(string filename)
    {
        foreach (var r in _fibers.Where(r => r.File == filename).ToList()) _fibers.Remove(r);
        foreach (var r in _sections.Where(r => r.File == filename).ToList()) _sections.Remove(r);
        foreach (var r in _events.Where(r => r.File == filename).ToList()) _events.Remove(r);
        _fileAnalyses.Remove(filename);
        _fileCurves.Remove(filename);
        UpdateWarningsColumn();
    }

    // ---------- Повторный анализ всех файлов (после смены настроек) ----------
    private async Task ReanalyzeAllAsync()
    {
        foreach (var entry in _files.Where(f => !f.IsAllEntry).ToList())
            await AnalyzeEntryAsync(entry);
    }

    // ---------- Выбор файла / «Все»: фильтр таблиц ----------
    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectionSync)
            return;

        var selected = FileList.SelectedItem as FileEntry;
        // «Все» (или снятие выделения) -> показать строки всех файлов
        _selectedFileName = selected == null || selected.IsAllEntry
            ? null
            : selected.Name;
        // выбор пришёл из списка файлов -> фильтруем все три вкладки
        RefreshViews();

        // снять выделение строки в «Волокнах» (строки другого файла скрыты)
        _selectionSync = true;
        FibersGrid.SelectedItem = null;
        SectionsGrid.SelectedItem = null;
        EventsGrid.SelectedItem = null;
        _selectionSync = false;

        // карта: первое волокно выбранного файла (или сброс, если «Все»)
        if (_selectedFileName == null)
            ResetMap();
        else
        {
            var first = _fibers.FirstOrDefault(r => r.File == _selectedFileName);
            if (first != null)
                _ = ShowMapAsync(first.File, first.FiberId);
            else
                ResetMap();
        }
    }

    // Выбор строки на вкладке «Волокна»: её строки остаются как есть,
    // но «Участки»/«События»/«Карта» фильтруются по файлу строки,
    // а список файлов прокручивается к этому файлу и выделяют его
    private void FibersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = FibersGrid.SelectedItem as FiberRow;
        SyncFileSelection(row?.File, row?.FiberId);
    }

    // Выбор строки на вкладке «Участки»: тот же синхронный выбор файла,
    // что и на «Волокнах» (список файлов прокручивается и выделяет файл)
    private void SectionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = SectionsGrid.SelectedItem as SectionRow;
        SyncFileSelection(row?.File, row?.FiberId);
    }

    private void EventsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = EventsGrid.SelectedItem as EventRow;
        SyncFileSelection(row?.File, row?.FiberId);
    }

    // Общая синхронизация: клик по строке любой вкладки выделяет её файл
    // в списке файлов и фильтрует ВСЕ вкладки по этому файлу
    private void SyncFileSelection(string? fileName, string? fiberId)
    {
        if (_selectionSync || fileName == null)
            return;

        _selectedFileName = fileName;

        _selectionSync = true;
        var entry = _files.FirstOrDefault(f => !f.IsAllEntry && f.Name == fileName);
        if (entry != null)
        {
            FileList.SelectedItem = entry;
            FileList.ScrollIntoView(entry); // подвести список к нужному файлу
        }
        // снять выделение строк в двух других таблицах (их строки скрыты фильтром)
        if (FibersGrid.SelectedItem is not FiberRow fr || fr.File != fileName)
            FibersGrid.SelectedItem = null;
        if (SectionsGrid.SelectedItem is not SectionRow sr || sr.File != fileName)
            SectionsGrid.SelectedItem = null;
        if (EventsGrid.SelectedItem is not EventRow er || er.File != fileName)
            EventsGrid.SelectedItem = null;
        _selectionSync = false;

        RefreshViews();

        // карта трассы для выбранного волокна
        if (fiberId != null)
            _ = ShowMapAsync(fileName, fiberId);
    }

    // ---------- Поиск по всем вкладкам ----------
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshViews();
    }

    private bool MatchSearch(params string?[] fields)
    {
        string query = SearchBox.Text.Trim();
        if (query.Length == 0)
            return true;
        return fields.Any(f => f != null && f.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchFile(string fileName)
        => _selectedFileName == null || fileName == _selectedFileName;

    private void RefreshViews()
    {
        _fibersView.Refresh();
        _sectionsView.Refresh();
        _eventsView.Refresh();
        UpdateColumnSortability();
        UpdateWarningsColumn();
    }

    // ---------- Сортировка: пустые колонки не сортируются ----------
    // Клик по заголовку колонки, в которой нет ни одного непустого значения
    // (например «Предупреждения», когда их ни у кого нет), ничего не делает:
    // у сортировки по пустому списку нет смысла. Как только данные появляются
    // (другие файлы/настройки), сортировка снова включается.
    private void UpdateColumnSortability()
    {
        ApplySortable(FibersGrid, _fibers, _fibersView);
        ApplySortable(SectionsGrid, _sections, _sectionsView);
        ApplySortable(EventsGrid, _events, _eventsView);
    }

    private static void ApplySortable(DataGrid grid, IEnumerable<object> rows, ICollectionView view)
    {
        var rowList = rows.ToList();
        foreach (var col in grid.Columns)
        {
            string path = EffectiveSortPath(col);
            if (string.IsNullOrEmpty(path))
                continue;

            bool hasData = false;
            foreach (var r in rowList)
            {
                PropertyInfo? pi = r.GetType().GetProperty(path);
                if (pi == null)
                    continue;
                object? v = pi.GetValue(r);
                if (v is string s)
                {
                    if (!string.IsNullOrWhiteSpace(s)) { hasData = true; break; }
                }
                else if (v != null)
                {
                    hasData = true;
                    break;
                }
            }

            col.CanUserSort = hasData;
            if (!hasData)
            {
                // снять зависшую сортировку по этой колонке, если она была:
                // DataGrid применяет её к ListCollectionView таблицы
                col.SortDirection = null;
                if (view is ListCollectionView lcv)
                    foreach (var d in lcv.SortDescriptions.Where(d => d.PropertyName == path).ToList())
                        lcv.SortDescriptions.Remove(d);
            }
        }
    }
    // Эффективный путь сортировки: SortMemberPath (заполнен у «Вердикта»),
    // иначе — путь Binding колонки (SortMemberPath по умолчанию пустой!)
    // Слушаем SortDirection каждой колонки: стрелка в тексте заголовка ставится
    // автоматически при любом изменении направления сортировки
    private void HookSortArrows()
    {
        var dpd = DependencyPropertyDescriptor.FromProperty(DataGridColumn.SortDirectionProperty, typeof(DataGridColumn));
        foreach (var grid in new[] { FibersGrid, SectionsGrid, EventsGrid })
            foreach (var col in grid.Columns)
                dpd.AddValueChanged(col, (_, _) => SyncHeaderArrow(col));
    }

    private static void SyncHeaderArrow(DataGridColumn col)
    {
        string baseText = StripArrow(col.Header as string);
        col.Header = col.SortDirection switch
        {
            ListSortDirection.Ascending => "▲ " + baseText,
            ListSortDirection.Descending => "▼ " + baseText,
            _ => baseText,
        };
    }

    private static string StripArrow(string? header)
        => header is null ? "" : header.TrimStart('▲', '▼', ' ');

    private static string EffectiveSortPath(DataGridColumn col)
    {
        if (!string.IsNullOrEmpty(col.SortMemberPath))
            return col.SortMemberPath;
        if (col is DataGridBoundColumn bc && bc.Binding is Binding b)
            return b.Path?.Path ?? "";
        return "";
    }

    // ---------- Сортировка по клику на заголовок (событие DataGrid.Sorting) ----------
    // Штатная сортировка отменяется (e.Handled) и применяется вручную, чтобы:
    //   1) колонки «Затухание…» при ПЕРВОМ клике сортировались ПО УБЫВАНИЮ
    //      (худшие сверху), повторный клик — по возрастанию;
    //   2) пустые колонки (CanUserSort=false, см. ApplySortable) оставались «глухими».
    // Направление: null -> (убыв. для затухания / возр. для прочих) -> обратное -> ...
    private static readonly string[] DescFirstColumns = ["Atten13", "Atten15"];

    // Колонки с ключом сортировки «значения сверху»: при первом клике
    // (по возрастанию ключа) строки со значениями идут выше пустых
    private static readonly Dictionary<string, string> SortKeyMap = new()
    {
        ["Fails"] = "FailsSortKey",
        ["Warnings"] = "WarningsSortKey",
        ["Reason"] = "ReasonSortKey",
    };

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true; // штатную сортировку DataGrid отменяем — сортируем сами
        DataGridColumn col = e.Column;
        string path = EffectiveSortPath(col);
        if (!col.CanUserSort || string.IsNullOrEmpty(path))
            return; // пустая колонка — клик ничего не делает

        string sortProp = SortKeyMap.TryGetValue(path, out var key) ? key : path;

        ListSortDirection dir = col.SortDirection switch
        {
            null => DescFirstColumns.Contains(path) ? ListSortDirection.Descending : ListSortDirection.Ascending,
            ListSortDirection.Ascending => ListSortDirection.Descending,
            _ => ListSortDirection.Ascending,
        };

        var grid = (DataGrid)sender;
        foreach (var c in grid.Columns)
            if (c != col)
                c.SortDirection = null; // стрелки только на активной колонке

        if (CollectionViewSource.GetDefaultView(grid.ItemsSource) is ListCollectionView lcv)
        {
            lcv.SortDescriptions.Clear();
            lcv.SortDescriptions.Add(new SortDescription(sortProp, dir));
        }
        col.SortDirection = dir; // HookSortArrows -> SyncHeaderArrow поставит ▲/▼ в заголовок
    }

    // ---------- Колонки «Коннектор ОТК» (вкладка «Волокна») ----------
    // Показываются только при включённой проверке входного разъёма ОТК
    private void UpdateQcColumnsVisibility()
    {
        var v = AppSettings.Current.CheckQcConnector ? Visibility.Visible : Visibility.Collapsed;
        Qc13Column.Visibility = v;
        Qc15Column.Visibility = v;
    }

    // ---------- Настройки: сохранение -> пересчёт всех verdict'ов ----------
    private async void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        if (win.ShowDialog() == true) // настройки сохранены (в т.ч. возможен выбор языка)
        {
            RefreshTableHeaders(); // заголовки колонок переназначаем кодом (DynamicResource
                                   // в Header колонки DataGrid при смене словаря не обновляется)
            UpdateQcColumnsVisibility();
            UpdateLossColumnsVisibility();
            if (_files.Count > 0)
            {
                await ReanalyzeAllAsync();
                // анализ пересоздан — привязываем карту к новому результату того же волокна
                // (роли/причины в нём — на новом языке)
                if (_mapFiber != null && _mapFile != null &&
                    _fileAnalyses.TryGetValue(_mapFile, out var fibers))
                    _mapFiber = fibers.FirstOrDefault(f => f.FiberId == _mapFiber.FiberId) ?? _mapFiber;
                BuildMapInfo();
                RedrawMap();
                UpdateCursorInfo(); // подпись курсоров — на новом языке
            }
            UpdateStatus();
        }
    }

    // ---------- Справка ----------
    private void BtnHelp_Click(object sender, RoutedEventArgs e)
    {
        var win = new HelpWindow { Owner = this };
        win.Show();
    }

    // ---------- Вкладка «Карта» ----------
    private void ResetMap()
    {
        _mapFiber = null;
        _mapCurves = null;
        _mapFile = null;
        _cursorA = _cursorB = null;
        _placingCursor = null;
        UpdateCursorInfo();
        if (ChkMapAutoY.IsChecked == true)
            _mapViewStart = _mapViewEnd = null; // авто включён — окно X сбрасывается
        // При выключенном «Авто Y» окно X и _mapDbMin/_mapDbMax сохраняются:
        // замороженный масштаб и область просмотра действуют и для следующих волокон/файлов
        MapCanvas.Children.Clear();
        MapInfo.Text = "";
        MapZoom.Text = "";
        MapHint.Visibility = Visibility.Visible;
    }

    // Перерисовка карты в текущем окне просмотра + подпись масштаба
    private void RedrawMap()
    {
        if (_mapFiber == null)
            return;
        bool autoY = ChkMapAutoY.IsChecked == true;
        MapPlot.Draw(MapCanvas, _mapFiber, _mapCurves,
            ChkMap1310.IsChecked == true, ChkMap1550.IsChecked == true, _mapViewStart, _mapViewEnd,
            autoY ? null : _mapDbMin, autoY ? null : _mapDbMax,
            _cursorA, _cursorB);

        double fullMax = MapPlot.ComputeMaxDistance(_mapFiber, _mapCurves);
        bool zoomed = _mapViewStart != null && _mapViewEnd != null &&
                      _mapViewStart > 0.5 && _mapViewEnd < fullMax - 0.5;
        MapZoom.Text = zoomed ? Loc.F("map_zoom_fmt", _mapViewStart, _mapViewEnd) : "";
    }

    // Зафиксировать текущее окно дБ: считаем по отсчётам внутри ТЕКУЩЕГО окна X —
    // ровно так, как автоподстройка посчитала бы его сейчас. Вызывается при
    // выключении «Авто Y»: пользователь сохраняет видимый в этот момент масштаб.
    private void FixCurrentDbWindow()
    {
        _mapDbMin = _mapDbMax = null;
        if (_mapFiber == null || _mapCurves == null || _mapCurves.Count == 0)
            return;
        double fullMax = MapPlot.ComputeMaxDistance(_mapFiber, _mapCurves);
        if (fullMax <= 0)
            return;
        double vs = _mapViewStart ?? 0, ve = _mapViewEnd ?? fullMax;

        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var c in MapPlot.VisibleCurves(_mapCurves, ChkMap1310.IsChecked == true, ChkMap1550.IsChecked == true))
            for (int i = 0; i < c.SampleCount; i++)
            {
                double d = c.DistanceM(i);
                if (double.IsNaN(d) || d < vs || d > ve) continue;
                double db = c.LevelDb(i);
                if (db < lo) lo = db;
                if (db > hi) hi = db;
            }
        if (lo <= hi)
        {
            if (hi - lo < 0.5) // плоская трасса: окно нулевой высоты недопустимо
            {
                double mid = (lo + hi) / 2;
                lo = mid - 0.5;
                hi = mid + 0.5;
            }
            // фиксируем окно вместе с тем же запасом 8%, который авто-режим
            // добавлял при рисовании — картинка в момент выключения не «прыгает»
            double pad = (hi - lo) * 0.08;
            _mapDbMin = lo - pad;
            _mapDbMax = hi + pad;
        }
    }

    private async Task ShowMapAsync(string filename, string fiberId)
    {
        if (!_fileAnalyses.TryGetValue(filename, out var fibers))
            return;
        var fiber = fibers.FirstOrDefault(f => f.FiberId == fiberId) ?? fibers.FirstOrDefault();
        if (fiber == null)
        {
            ResetMap();
            return;
        }

        // сырые кривые читаются один раз на файл (вне UI-потока)
        if (!_fileCurves.TryGetValue(filename, out var curves))
        {
            string path = _files.FirstOrDefault(f => !f.IsAllEntry && f.Name == filename)?.Path ?? filename;
            curves = await Task.Run(() =>
            {
                try { return RawTrace.ReadCurves(path); }
                catch { return null; }
            });
            _fileCurves[filename] = curves;
        }

        _mapFiber = fiber;
        _mapCurves = curves;
        _mapFile = filename;
        if (!AppSettings.Current.CompareMode)
            _cursorA = _cursorB = null; // новое волокно — курсоры сбрасываются (в режиме сравнения сохраняются)
        _placingCursor = null;
        UpdateCursorInfo();
        if (ChkMapAutoY.IsChecked == true)
        {
            _mapViewStart = _mapViewEnd = null; // новый объект — полный диапазон
            _mapDbMin = _mapDbMax = null;
        }
        else
        {
            // «Авто Y» выключен: окно X (зум/панорама) и шкала дБ сохраняются
            // при смене волокна/файла — можно навести окно на коннектор и
            // сравнивать одну и ту же область в разных файлах
            if (_mapDbMin == null || _mapDbMax == null)
                FixCurrentDbWindow(); // фиксируем один раз
        }

        // Шапка карты: вердикт + точки длин волн перед 1310/1550 (цвета как у кривых)
        BuildMapInfo();
        RedrawMap();
    }

    // Шапка карты (вынесено: перестраивается и при смене языка/настроек)
    private void BuildMapInfo()
    {
        if (_mapFiber == null)
            return;
        var fiber = _mapFiber;
        var curves = _mapCurves;

        MapInfo.Inlines.Clear();
        void AddInfo(string text, Brush? brush = null)
        {
            var r = new Run(text);
            if (brush != null) r.Foreground = brush; // иначе наследуется от TextBlock (тема)
            MapInfo.Inlines.Add(r);
        }
        // вердикт как во вкладке «Волокна»: PASS в жёлтой зоне -> ATTN
        var st = AppSettings.Current;
        string mapVerdict = fiber.Verdict == "PASS" &&
            (AnalysisBridge.AttBorderline(fiber.Att13, st.Atten1310Max, st.BorderlinePercent) ||
             AnalysisBridge.AttBorderline(fiber.Att15, st.Atten1550Max, st.BorderlinePercent))
            ? "ATTN" : fiber.Verdict;
        string unitM = Loc.T("unit_m"), unitDbKm = Loc.T("unit_dbkm");
        AddInfo($"{Loc.T("map_verdict_lbl")} {mapVerdict} · ");
        AddInfo("● ", Wl1310Brush);
        AddInfo($"1310: {fiber.Len13:F1} {unitM}" + (fiber.Att13 != null ? $" / {fiber.Att13:F3} {unitDbKm}" : "") + " · ");
        AddInfo("● ", Wl1550Brush);
        AddInfo($"1550: {fiber.Len15:F1} {unitM}" + (fiber.Att15 != null ? $" / {fiber.Att15:F3} {unitDbKm}" : ""));
        if (curves == null || curves.Count == 0)
            AddInfo(Loc.T("map_no_raw"));
        MapHint.Visibility = curves == null || curves.Count == 0
            ? Visibility.Visible : Visibility.Hidden;
    }

    private void MapWl_Changed(object sender, RoutedEventArgs e)
    {
        RedrawMap();
        UpdateCursorInfo(); // уровень под курсором зависит от видимой длины волны
    }

    // ---- кнопка «Волокно»: окно по X только по зоне проверяемого волокна ----
    private void BtnMapFiber_Click(object sender, RoutedEventArgs e)
    {
        if (_mapFiber == null)
            return;
        double? lo = null, hi = null;
        foreach (var s in _mapFiber.Sections)
        {
            if (s.Role == Loc.T("role_buffer") || s.StartM == null || s.EndM == null)
                continue;
            lo = lo == null ? s.StartM : Math.Min(lo.Value, s.StartM!.Value);
            hi = hi == null ? s.EndM : Math.Max(hi.Value, s.EndM!.Value);
        }
        if (lo == null || hi == null || hi <= lo)
        {
            ResetMapZoom(); // зон волокна нет — показываем всю трассу
            return;
        }
        double fullMax = MapPlot.ComputeMaxDistance(_mapFiber, _mapCurves);
        double pad = Math.Max(10, (hi.Value - lo.Value) * 0.05); // небольшой запас по краям
        _mapViewStart = Math.Max(0, lo.Value - pad);
        _mapViewEnd = Math.Min(fullMax, hi.Value + pad);
        RedrawMap();
    }

    // ---- курсоры-измерители А/В ----
    private void BtnCursorA_Click(object sender, RoutedEventArgs e) => StartCursorPlacement('A');
    private void BtnCursorB_Click(object sender, RoutedEventArgs e) => StartCursorPlacement('B');

    private void StartCursorPlacement(char which)
    {
        if (_mapFiber == null)
            return;
        _placingCursor = which;
        MapCanvas.Cursor = Cursors.Cross;
        UpdateCursorInfo();
    }

    // Уровень трассы под курсором: по первой видимой кривой (1550, иначе 1310)
    // + длина волны, по которой посчитано (для цветной точки в показаниях)
    private (double? db, int nm) CursorLevel(double dist)
    {
        if (_mapCurves == null)
            return (null, 0);
        bool use1550 = ChkMap1550.IsChecked == true;
        bool use1310 = ChkMap1310.IsChecked == true;
        foreach (var c in _mapCurves)
        {
            bool is1550 = c.WavelengthNm == 1550;
            bool is1310 = c.WavelengthNm == 1310 || c.WavelengthNm == 0;
            if (!((is1550 && use1550) || (is1310 && use1310)))
                continue;
            double bestD = double.MaxValue, bestDb = double.NaN;
            for (int i = 0; i < c.SampleCount; i++)
            {
                double d = c.DistanceM(i);
                if (double.IsNaN(d)) continue;
                double ad = Math.Abs(d - dist);
                if (ad < bestD) { bestD = ad; bestDb = c.LevelDb(i); }
            }
            if (!double.IsNaN(bestDb))
                return (bestDb, is1550 ? 1550 : 1310);
        }
        return (null, 0);
    }

    // точки-маркеры длины волны в показаниях курсоров (цвета как кривые на графике)
    private static readonly Brush Wl1310Brush = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly Brush Wl1550Brush = new SolidColorBrush(Color.FromRgb(0xE6, 0x4A, 0x19));
    private static Brush WlBrush(int nm) => nm switch
    {
        1310 => Wl1310Brush,
        1550 => Wl1550Brush,
        _ => Brushes.Gray,
    };

    // Показания курсоров внизу графика: с цветной точкой длины волны у чисел дБ
    private void UpdateCursorInfo()
    {
        if (CursorInfo == null)
            return; // ещё не создан (события XAML во время InitializeComponent)
        CursorInfo.Inlines.Clear();
        if (_cursorA == null && _cursorB == null && _placingCursor == null)
            return;

        void Add(string text, Brush? brush = null)
        {
            var run = new Run(text);
            if (brush != null) run.Foreground = brush; // иначе наследуется от TextBlock (тема)
            CursorInfo.Inlines.Add(run);
        }

        void NewLine() => CursorInfo.Inlines.Add(new LineBreak());

        bool first = true;
        if (_placingCursor != null)
        {
            Add(Loc.F("cursor_place_hint", Loc.T(_placingCursor == 'A' ? "cursor_a_name" : "cursor_b_name")));
            first = false;
        }

        var (la, nmA) = CursorLevel(_cursorA ?? 0);
        var (lb, nmB) = CursorLevel(_cursorB ?? 0);

        string unitM = Loc.T("unit_m"), unitDb = Loc.T("unit_db");
        if (_cursorA != null)
        {
            if (!first) NewLine();
            Add(Loc.F("cursor_fmt_a", _cursorA.Value));
            if (la != null) { Add(" · "); Add("● ", WlBrush(nmA)); Add($"{la.Value:F2} {unitDb}"); }
            first = false;
        }
        if (_cursorB != null)
        {
            if (!first) NewLine();
            Add(Loc.F("cursor_fmt_b", _cursorB.Value));
            if (lb != null) { Add(" · "); Add("● ", WlBrush(nmB)); Add($"{lb.Value:F2} {unitDb}"); }
            first = false;
        }
        if (_cursorA != null && _cursorB != null)
        {
            if (!first) NewLine();
            Add(Loc.F("cursor_fmt_d", Math.Abs(_cursorA.Value - _cursorB.Value)));
            if (la != null && lb != null)
            {
                Add(" · ");
                Add("● ", WlBrush(nmA != 0 ? nmA : nmB));
                Add($"{la.Value - lb.Value:F2} {unitDb}"); // потери А→В
            }
        }
    }

    private void MapAutoY_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkMapAutoY.IsChecked == true)
        {
            // авто включён — окно X и шкала дБ снова живые
            _mapDbMin = _mapDbMax = null;
            _mapViewStart = _mapViewEnd = null;
        }
        else if (_mapFiber != null)
            FixCurrentDbWindow(); // авто выключен — замораживаем масштаб, видимый сейчас
        RedrawMap();
    }

    private void MapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawMap();

    // ---- зум колесом (вокруг позиции курсора) ----
    // Shift + колесо (только при выключенном «Авто Y») — зум по вертикали (дБ)
    private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_mapFiber == null)
            return;
        double fullMax = MapPlot.ComputeMaxDistance(_mapFiber, _mapCurves);
        if (fullMax <= 0)
            return;

        double factor = e.Delta > 0 ? 1.25 : 1.0 / 1.25; // колесо вверх — приблизить

        // вертикальный зум замороженного окна дБ
        if (ChkMapAutoY.IsChecked != true && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (_mapDbMin == null || _mapDbMax == null)
                FixCurrentDbWindow(); // ещё не заморожено — фиксируем текущее окно
            if (_mapDbMin == null || _mapDbMax == null)
                return;

            double ph = MapCanvas.ActualHeight - MapPlot.PlotTop - MapPlot.PlotBottom;
            double frac = Math.Clamp((e.GetPosition(MapCanvas).Y - MapPlot.PlotTop) / Math.Max(1, ph), 0, 1);
            double span = _mapDbMax.Value - _mapDbMin.Value;
            double anchor = _mapDbMax.Value - frac * span; // верх экрана = максимум дБ
            double newSpan = Math.Clamp(span / factor, 0.5, 400);
            double top = anchor + frac * newSpan; // верхнее значение нового окна
            _mapDbMax = top;
            _mapDbMin = top - newSpan;
            RedrawMap();
            e.Handled = true;
            return;
        }

        double vs = _mapViewStart ?? 0, ve = _mapViewEnd ?? fullMax;
        double pw = MapCanvas.ActualWidth - MapPlot.PlotLeft - MapPlot.PlotRight;
        double fracX = Math.Clamp((e.GetPosition(MapCanvas).X - MapPlot.PlotLeft) / Math.Max(1, pw), 0, 1);

        double spanX = ve - vs;
        double anchorX = vs + fracX * spanX;
        double newSpanX = Math.Clamp(spanX / factor, MapPlot.MinViewSpanM, fullMax);
        double nvs = Math.Clamp(anchorX - fracX * newSpanX, 0, fullMax - newSpanX);
        _mapViewStart = nvs;
        _mapViewEnd = nvs + newSpanX;
        RedrawMap();
        e.Handled = true;
    }

    // ---- панорамирование перетаскиванием (двойной клик = сброс зума) ----
    private void MapCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_mapFiber == null)
            return;
        if (e.ClickCount == 2)
        {
            ResetMapZoom();
            return;
        }
        double fullMax = MapPlot.ComputeMaxDistance(_mapFiber, _mapCurves);
        if (fullMax <= 0)
            return;

        // Режим постановки курсора: один клик ставит курсор в точку клика
        if (_placingCursor != null)
        {
            double pw = MapCanvas.ActualWidth - MapPlot.PlotLeft - MapPlot.PlotRight;
            double vs = _mapViewStart ?? 0, ve = _mapViewEnd ?? fullMax;
            double frac = Math.Clamp((e.GetPosition(MapCanvas).X - MapPlot.PlotLeft) / Math.Max(1, pw), 0, 1);
            double dist = vs + frac * (ve - vs);
            if (_placingCursor == 'A')
                _cursorA = dist;
            else
                _cursorB = dist;
            _placingCursor = null;
            MapCanvas.Cursor = Cursors.SizeWE;
            RedrawMap();
            UpdateCursorInfo();
            e.Handled = true;
            return;
        }

        // Ctrl + ЛКМ вблизи линии курсора -> перетаскиваем курсор по графику
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            double px = e.GetPosition(MapCanvas).X;
            double vs = _mapViewStart ?? 0, ve = _mapViewEnd ?? fullMax;
            double pw = MapCanvas.ActualWidth - MapPlot.PlotLeft - MapPlot.PlotRight;
            char? hit = null;
            double bestPx = 8; // допуск «захвата» линии, px
            foreach (var (ch, dist) in new (char, double?)[] { ('A', _cursorA), ('B', _cursorB) })
            {
                if (dist == null || dist < vs || dist > ve)
                    continue;
                double cx = MapPlot.PlotLeft + (dist.Value - vs) / Math.Max(1e-9, ve - vs) * pw;
                double ad = Math.Abs(cx - px);
                if (ad <= bestPx) { bestPx = ad; hit = ch; }
            }
            if (hit != null)
            {
                _dragCursor = hit;
                MoveCursorToX(px);
                MapCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        _mapDragging = true;
        _dragStartX = e.GetPosition(MapCanvas).X;
        _dragViewStart = _mapViewStart ?? 0;
        _dragSpan = (_mapViewEnd ?? fullMax) - _dragViewStart;
        MapCanvas.CaptureMouse();
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_mapFiber == null)
            return;

        // перетаскивание курсора (Ctrl + ЛКМ)
        if (_dragCursor != null)
        {
            MoveCursorToX(e.GetPosition(MapCanvas).X);
            return;
        }

        if (!_mapDragging)
            return;
        double fullMax = MapPlot.ComputeMaxDistance(_mapFiber, _mapCurves);
        double pw = MapCanvas.ActualWidth - MapPlot.PlotLeft - MapPlot.PlotRight;
        double dx = e.GetPosition(MapCanvas).X - _dragStartX;
        double dm = -dx * _dragSpan / Math.Max(1, pw); // тянем вправо -> окно влево
        double vs = Math.Clamp(_dragViewStart + dm, 0, Math.Max(0, fullMax - _dragSpan));
        _mapViewStart = vs;
        _mapViewEnd = vs + _dragSpan;
        RedrawMap();
    }

    private void MapCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragCursor != null)
        {
            _dragCursor = null;
            MapCanvas.ReleaseMouseCapture();
            return;
        }
        if (!_mapDragging)
            return;
        _mapDragging = false;
        MapCanvas.ReleaseMouseCapture();
    }

    // Поставить перетаскиваемый курсор по позиции X на канвасе (в пределах окна)
    private void MoveCursorToX(double canvasX)
    {
        if (_mapFiber == null || _dragCursor == null)
            return;
        double fullMax = MapPlot.ComputeMaxDistance(_mapFiber, _mapCurves);
        double vs = _mapViewStart ?? 0, ve = _mapViewEnd ?? fullMax;
        double pw = MapCanvas.ActualWidth - MapPlot.PlotLeft - MapPlot.PlotRight;
        double frac = Math.Clamp((canvasX - MapPlot.PlotLeft) / Math.Max(1, pw), 0, 1);
        double dist = Math.Clamp(vs + frac * (ve - vs), vs, ve);
        if (_dragCursor == 'A')
            _cursorA = dist;
        else
            _cursorB = dist;
        RedrawMap();
        UpdateCursorInfo();
    }

    // ---- сброс зума: двойной клик или кнопка ----
    private void BtnMapReset_Click(object sender, RoutedEventArgs e) => ResetMapZoom();

    private void ResetMapZoom()
    {
        if (_mapFiber == null)
            return;
        _mapViewStart = _mapViewEnd = null;
        RedrawMap();
    }

    // ---------- Список файлов: свернуть/развернуть кликом по границе ----------
    // Клик (без смещения мыши) по разделителю = переключение; drag = штатный ресайз
    private System.Windows.Point? _splitterDownPos;

    private void Splitter_Down(object sender, MouseButtonEventArgs e)
        => _splitterDownPos = e.GetPosition(null);

    private void Splitter_Up(object sender, MouseButtonEventArgs e)
    {
        if (_splitterDownPos == null)
            return;
        System.Windows.Point p = e.GetPosition(null);
        double dx = Math.Abs(p.X - _splitterDownPos.Value.X);
        double dy = Math.Abs(p.Y - _splitterDownPos.Value.Y);
        _splitterDownPos = null;
        if (dx <= 3 && dy <= 3) // это был клик, а не перетаскивание
            ToggleFileList();
    }

    private void ToggleFileList()
    {
        _leftCollapsed = !_leftCollapsed;
        if (_leftCollapsed)
        {
            if (LeftCol.ActualWidth > 1)
                _leftSavedWidth = new GridLength(LeftCol.ActualWidth);
            LeftCol.MinWidth = 0;
            LeftCol.Width = new GridLength(0); // панель схлопывается, граница остаётся
            FileListToggleArrow.Text = "";   // → развернуть
        }
        else
        {
            LeftCol.MinWidth = 160;
            LeftCol.Width = _leftSavedWidth; // вернуть ширину до сворачивания
            FileListToggleArrow.Text = "";   // ← свернуть
        }
    }

    // ---------- Тема: цикл Gray -> Dark -> Light -> Gray ----------
    private static readonly string[] Themes = ["Gray", "Dark", "Light"];

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        string current = dicts[0].Source.OriginalString.Split('/')[^1].Split('.')[0];
        int idx = System.Array.IndexOf(Themes, current);
        string next = Themes[(idx + 1) % Themes.Length];
        dicts[0] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{next}.xaml"),
        };
    }

    // ---------- Экспорт CSV (формат консольной версии) ----------
    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_fibers.Count == 0)
        {
            MessageBox.Show(this, Loc.T("msg_no_export"),
                Loc.T("title_export"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFolderDialog { Title = Loc.T("dlg_export_folder") };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            AnalysisBridge.ExportCsv(dlg.FolderName, _fibers, _sections, _events);
            MessageBox.Show(this, Loc.F("msg_export_ok", dlg.FolderName),
                Loc.T("title_export"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.F("msg_export_fail", ex.Message),
                Loc.T("title_export"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- Очистить ----------
    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        _titleFolders.Clear();
        Title = BaseTitle; // папки больше не загружены
        // «Все» остаётся в списке — чистим только файлы
        foreach (var f in _files.Where(f => !f.IsAllEntry).ToList())
            _files.Remove(f);
        _fibers.Clear();
        _sections.Clear();
        _events.Clear();
        _fileAnalyses.Clear();
        _fileCurves.Clear();
        ResetMap();
        _selectedFileName = null;
        SearchBox.Clear();
        FileList.SelectedItem = _allEntry;
        UpdateStatus();
        UpdateColumnSortability(); // таблицы пусты — сортировку по колонкам отключаем
        UpdateWarningsColumn();
    }

    // ---------- Статус-бар ----------
    // Цвета счётчиков вердиктов: PASS — зелёный, FAIL — красный,
    // «Пограничных» — янтарный (как жёлтая зона в списках)
    private static readonly Brush StatusPassBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush StatusFailBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush StatusWarnBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0x8A, 0x00));

    private void UpdateStatus()
    {
        if (StatusText == null)
            return;
        var realFiles = _files.Where(f => !f.IsAllEntry).ToList();
        int pass = realFiles.Count(f => f.Verdict == Verdict.Pass);
        int fail = realFiles.Count(f => f.Verdict == Verdict.Fail);
        int warn = realFiles.Count(f => f.Dot == DotStatus.Yellow);
        int errors = realFiles.Count(f => f.Error != null);

        StatusText.Inlines.Clear();
        // brush == null — НЕ ставим Foreground: наследуется от TextBlock,
        // т.е. подхватывает текущую тему при её переключении
        void Add(string text, Brush? brush = null)
        {
            var run = new Run(text);
            if (brush != null) run.Foreground = brush;
            StatusText.Inlines.Add(run);
        }
        void Sep() => Add(" | ");

        Add(Loc.F("st_files", realFiles.Count)); Sep();
        // PASS зелёный, когда проблем нет вообще; иначе обычным цветом
        Add(Loc.F("st_pass", pass), fail == 0 && warn == 0 ? StatusPassBrush : null); Sep();
        if (fail > 0) Add(Loc.F("st_fail", fail), StatusFailBrush); else Add(Loc.F("st_fail", fail)); Sep();
        if (warn > 0) Add(Loc.F("st_borderline", warn), StatusWarnBrush); else Add(Loc.F("st_borderline", warn)); Sep();
        Add(Loc.F("st_fibers", _fibers.Count));
        if (errors > 0) { Sep(); Add(Loc.F("st_errors", errors), StatusFailBrush); }
    }

    // ---------- Печать отчёта ----------
    // Печатает «Волокна», «Участки» и «События» в том виде, как их видно
    // сейчас: с учётом фильтра по файлу, поиска и сортировки колонок.
    // Есть принтеры — системный диалог печати; нет — сохранение в PDF через
    // безголовый Microsoft Edge/Chrome (настоящий PDF с нормальным текстом),
    // крайний случай — встроенный векторный генератор PrintPdf.
    private async void BtnPrint_Click(object sender, RoutedEventArgs e)
    {
        var opts = new PrintOptionsWindow { Owner = this };
        if (opts.ShowDialog() != true)
            return;

        if (HasPrinters())
        {
            var doc = BuildReportDocument(opts.WantMap, opts.WantFibers, opts.WantSections, opts.WantEvents);
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true)
                return;
            dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "EXFO TRC Analyzer");
            return;
        }

        var sfd = new SaveFileDialog
        {
            Title = Loc.T("dlg_save_pdf"),
            Filter = Loc.T("filter_pdf"),
            FileName = $"otchet_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
        };
        if (sfd.ShowDialog() != true)
            return;

        string html = BuildReportHtml(opts.WantMap, opts.WantFibers, opts.WantSections, opts.WantEvents);
        BtnPrint.IsEnabled = false;
        try
        {
            await Task.Run(() => SavePdfViaBrowser(html, sfd.FileName));
            StatusText.Text = Loc.F("report_saved", sfd.FileName);
        }
        catch
        {
            try
            {
                PrintPdf.Save(BuildReportDocument(false, opts.WantFibers, opts.WantSections, opts.WantEvents), sfd.FileName);
                StatusText.Text = Loc.F("report_saved_builtin", sfd.FileName);
            }
            catch (Exception ex2)
            {
                MessageBox.Show(this, Loc.F("msg_pdf_fail", ex2.Message), Loc.T("title_print"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            BtnPrint.IsEnabled = true;
        }
    }

    private static string? FindBrowser()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? pf;
        foreach (var c in new[]
        {
            Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
        })
            if (File.Exists(c))
                return c;
        return null;
    }

    // HTML-отчёт -> PDF через headless Edge/Chrome (без окна браузера)
    private static void SavePdfViaBrowser(string html, string pdfPath)
    {
        string exe = FindBrowser() ?? throw new InvalidOperationException(Loc.T("err_no_browser"));
        string dir = Path.Combine(Path.GetTempPath(), "exfo_trc_print");
        Directory.CreateDirectory(dir);

        string htmlFile = Path.Combine(dir, "report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
        File.WriteAllText(htmlFile, html, new UTF8Encoding(false));
        if (File.Exists(pdfPath))
            File.Delete(pdfPath);

        string args = "--headless --disable-gpu --no-first-run --no-default-browser-check "
            + $"--user-data-dir=\"{Path.Combine(dir, "profile")}\" --no-pdf-header-footer "
            + "--print-to-pdf-no-header --virtual-time-budget=10000 "
            + $"--print-to-pdf=\"{pdfPath}\" \"file:///{htmlFile.Replace('\\', '/')}\"";
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true };
        var proc = Process.Start(psi) ?? throw new InvalidOperationException(Loc.F("err_browser_start", exe));

        // браузер пишет PDF асинхронно — ждём появления «стабильного» файла, до ~25 с
        long lastLen = -1;
        int stable = 0;
        for (int t = 0; t < 125; t++)
        {
            Thread.Sleep(200);
            long len = File.Exists(pdfPath) ? new FileInfo(pdfPath).Length : 0;
            if (len > 1024)
            {
                if (len == lastLen && ++stable >= 2)
                    break; // размер перестал меняться — файл дописан
                stable = 0;
                lastLen = len;
            }
            if (proc.HasExited && len > 1024)
                break;
        }
        if (!proc.HasExited)
            try { proc.Kill(entireProcessTree: true); } catch { }

        if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length < 1024)
            throw new InvalidOperationException(Loc.T("err_browser_pdf"));
    }

    private string BuildReportHtml(bool withMap, bool withFibers, bool withSections, bool withEvents)
    {
        int nFiles = _files.Count(f => !f.IsAllEntry);
        string scope = _selectedFileName != null ? Loc.F("scope_file", _selectedFileName) : Loc.F("scope_all", nFiles);
        string search = SearchBox.Text.Trim();

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{Esc(Loc.T("report_title"))}</title><style>");
        sb.Append("@page{size:A4 landscape;margin:10mm 12mm;}");
        sb.Append("body{font-family:'Segoe UI',Arial,sans-serif;font-size:10.5px;color:#111;}");
        sb.Append("h1{font-size:16px;margin:0 0 3px;}");
        sb.Append(".meta{color:#111;font-weight:700;font-size:11.5px;margin:0 0 6px;}");
        sb.Append("h2{font-size:12.5px;margin:14px 0 4px;}");
        sb.Append(".map img{width:100%;border:1px solid #999;}");
        sb.Append("table{border-collapse:collapse;}");
        sb.Append("th,td{border:1px solid #999;padding:3px 6px;font-size:9.5px;text-align:left;vertical-align:top;}");
        sb.Append("th{background:#e8ecf1;} tbody tr:nth-child(even) td{background:#f6f8fa;}");
        sb.Append("thead{display:table-header-group;} tr{page-break-inside:avoid;}");
        sb.Append(".fail{color:#c62828;font-weight:600;} .warn{color:#b26a00;} .pass{color:#2e7d32;}");
        sb.Append(".none{color:#777;}");
        sb.Append("</style></head><body>");
        sb.Append($"<h1>{Esc(Loc.T("report_title"))}</h1>");
        sb.Append($"<div class=\"meta\">{DateTime.Now:dd.MM.yyyy HH:mm} · {Esc(scope)} · {Esc(Loc.F("search_lbl", search.Length > 0 ? search : Loc.T("search_none")))}</div>");

        if (withMap)
        {
            var map = RenderMapBitmap();
            if (map != null)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(map));
                using var ms = new MemoryStream();
                enc.Save(ms);
                sb.Append($"<h2>{Esc(Loc.T("tab_map"))}</h2><div class=\"map\"><img src=\"data:image/png;base64,")
                  .Append(Convert.ToBase64String(ms.ToArray()))
                  .Append("\"/></div>");
            }
            else
                sb.Append($"<h2>{Esc(Loc.T("tab_map"))}</h2><div class=\"none\">{Esc(Loc.T("map_missing"))}</div>");
        }

        if (withFibers) AddHtmlTable(sb, Loc.T("tab_fibers"), FibersGrid);
        if (withSections) AddHtmlTable(sb, Loc.T("tab_sections"), SectionsGrid);
        if (withEvents) AddHtmlTable(sb, Loc.T("tab_events"), EventsGrid);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AddHtmlTable(StringBuilder sb, string title, DataGrid grid)
    {
        sb.Append("<h2>").Append(Esc(title)).Append("</h2>");
        var cols = grid.Columns
            .Where(c => c.Visibility == Visibility.Visible && c is DataGridBoundColumn)
            .ToList();

        var rows = new List<object>();
        foreach (var item in CollectionViewSource.GetDefaultView(grid.ItemsSource))
            rows.Add(item);

        if (cols.Count == 0 || rows.Count == 0)
        {
            sb.Append($"<div class=\"none\">{Esc(Loc.T("no_rows"))}</div>");
            return;
        }

        sb.Append("<table><thead><tr>");
        foreach (var col in cols)
            sb.Append("<th>").Append(Esc(StripArrow(col.Header as string))).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var item in rows)
        {
            sb.Append("<tr>");
            foreach (var col in cols)
            {
                string val = CellValue(col, item);
                string cls = val.Contains("FAIL") ? " class=\"fail\""
                    : val == "ATTN" || val.Contains("зон") || val.Contains("zone") ? " class=\"warn\""
                    : val == "PASS" ? " class=\"pass\"" : "";
                sb.Append("<td").Append(cls).Append('>').Append(Esc(val)).Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static string Esc(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static bool HasPrinters()
    {
        try
        {
            using var srv = new System.Printing.LocalPrintServer();
            return srv.GetPrintQueues().Any();
        }
        catch
        {
            return false;
        }
    }

    private FlowDocument BuildReportDocument(bool withMap, bool withFibers, bool withSections, bool withEvents)
    {
        var doc = new FlowDocument
        {
            PageWidth = 794, PageHeight = 1123, // A4, px @96dpi
            PagePadding = new Thickness(36),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
        };

        doc.Blocks.Add(new Paragraph(new Run(Loc.T("report_title")))
        { FontSize = 16, FontWeight = FontWeights.Bold });
        int nFiles = _files.Count(f => !f.IsAllEntry);
        string scope = _selectedFileName != null ? Loc.F("scope_file", _selectedFileName) : Loc.F("scope_all", nFiles);
        string search = SearchBox.Text.Trim();
        doc.Blocks.Add(new Paragraph(new Run($"{DateTime.Now:dd.MM.yyyy HH:mm} · {scope} · {Loc.F("search_lbl", search.Length > 0 ? search : Loc.T("search_none"))}"))
        { FontWeight = FontWeights.Bold });

        if (withMap)
        {
            var map = RenderMapBitmap();
            if (map != null)
            {
                doc.Blocks.Add(new BlockUIContainer(new Image { Source = map, Stretch = Stretch.Uniform })
                { Margin = new Thickness(0, 8, 0, 8) });
            }
            else
                doc.Blocks.Add(new Paragraph(new Run(Loc.T("map_missing")))
                { Foreground = Brushes.Gray });
        }

        if (withFibers) AddPrintTable(doc, Loc.T("tab_fibers"), FibersGrid);
        if (withSections) AddPrintTable(doc, Loc.T("tab_sections"), SectionsGrid);
        if (withEvents) AddPrintTable(doc, Loc.T("tab_events"), EventsGrid);
        return doc;
    }

    // Снимок текущей карты (Canvas) в битмап с белой подложкой; null, если карты нет.
    // Как в приложении: шапка (вердикт/длины/затухания) на белой полосе над графиком,
    // показания курсоров и масштаб — текстом внизу графика (цвета как на экране).
    private BitmapSource? RenderMapBitmap()
    {
        if (MapCanvas == null || MapCanvas.Children.Count == 0 ||
            MapCanvas.ActualWidth < 20 || MapCanvas.ActualHeight < 20)
            return null;
        int w = (int)Math.Round(MapCanvas.ActualWidth), h = (int)Math.Round(MapCanvas.ActualHeight);

        var canvasRt = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
        canvasRt.Render(MapCanvas);

        var mapFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // шапка карты — отдельной полосой над графиком (цветные точки длин волн,
        // перенос по ширине графика)
        var headerSegs = new List<FormattedText>();
        if (MapInfo != null)
            foreach (var inl in MapInfo.Inlines)
                if (inl is System.Windows.Documents.Run r && r.Text.Length > 0)
                    headerSegs.Add(new FormattedText(r.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        mapFace, 12, r.Foreground ?? MapInfo.Foreground, 1.0));

        var headerLayout = new List<(FormattedText Ft, double X, double Y)>();
        double hx = 10, hy = 4;
        foreach (var ft in headerSegs)
        {
            if (hx > 10 && hx + ft.WidthIncludingTrailingWhitespace > w - 10)
            {
                hx = 10;
                hy += 16;
            }
            headerLayout.Add((ft, hx, hy));
            hx += ft.WidthIncludingTrailingWhitespace;
        }
        double strip = headerLayout.Count > 0 ? hy + 20 : 0;

        // строки курсоров (с цветами длин волн) + масштаб окна
        var lines = new List<List<(string Text, Brush Brush, FontStyle Style)>>();
        var cur = new List<(string, Brush, FontStyle)>();
        if (CursorInfo != null)
            foreach (var inline in CursorInfo.Inlines)
            {
                if (inline is System.Windows.Documents.LineBreak)
                {
                    if (cur.Count > 0) { lines.Add(cur); cur = new List<(string, Brush, FontStyle)>(); }
                }
                else if (inline is System.Windows.Documents.Run r && r.Text.Length > 0)
                    cur.Add((r.Text, r.Foreground ?? Brushes.Black, FontStyles.Normal));
            }
        if (cur.Count > 0)
            lines.Add(cur);
        string zoomText = MapZoom?.Text?.Trim() ?? "";
        if (zoomText.Length > 0)
            lines.Add(new List<(string, Brush, FontStyle)> { (zoomText, MapZoom!.Foreground ?? Brushes.Gray, FontStyles.Italic) });
        double overlayH = lines.Count * 17.0;

        int totalH = (int)Math.Round(h + strip);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, totalH));
            foreach (var (ft, x, yy) in headerLayout)
                dc.DrawText(ft, new Point(x, yy));
            dc.DrawImage(canvasRt, new Rect(0, strip, w, h));

            double y = strip + h - 30 - overlayH; // как в приложении: внизу слева
            foreach (var ln in lines)
            {
                double x = 60;
                foreach (var (text, brush, style) in ln)
                {
                    var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), style, FontWeights.Normal, FontStretches.Normal),
                        style == FontStyles.Italic ? 11 : 12, brush, 1.0);
                    dc.DrawText(ft, new Point(x, y));
                    x += ft.WidthIncludingTrailingWhitespace;
                }
                y += 17;
            }
        }
        var rt = new RenderTargetBitmap(w * 2, totalH * 2, 192, 192, PixelFormats.Pbgra32);
        rt.Render(dv);
        return rt;
    }

    private static void AddPrintTable(FlowDocument doc, string title, DataGrid grid)
    {
        var cols = grid.Columns
            .Where(c => c.Visibility == Visibility.Visible && c is DataGridBoundColumn)
            .ToList();

        doc.Blocks.Add(new Paragraph(new Run(title))
        { FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 4) });

        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        int rows = 0;
        foreach (var _ in view) rows++;
        if (cols.Count == 0 || rows == 0)
        {
            doc.Blocks.Add(new Paragraph(new Run(Loc.T("no_rows"))) { Foreground = Brushes.Gray });
            return;
        }

        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
        for (int i = 0; i < cols.Count; i++)
            table.Columns.Add(new TableColumn { Width = GridLength.Auto });

        var headerGroup = new TableRowGroup();
        var hr = new TableRow();
        foreach (var col in cols)
            hr.Cells.Add(PrintCell(StripArrow(col.Header as string), bold: true));
        headerGroup.Rows.Add(hr);
        table.RowGroups.Add(headerGroup);

        var body = new TableRowGroup();
        foreach (var item in view) // перечисление представления = отфильтрованные + отсортированные строки
        {
            var tr = new TableRow();
            foreach (var col in cols)
                tr.Cells.Add(PrintCell(CellValue(col, item)));
            body.Rows.Add(tr);
        }
        table.RowGroups.Add(body);
        doc.Blocks.Add(table);
    }

    private static TableCell PrintCell(string text, bool bold = false)
    {
        var p = new Paragraph(new Run(text ?? "")) { Margin = new Thickness(0) };
        if (bold) p.FontWeight = FontWeights.Bold;
        return new TableCell(p) { Padding = new Thickness(4, 2, 4, 2) };
    }

    // Значение ячейки по привязке колонки (path + StringFormat из DataGrid)
    private static string CellValue(DataGridColumn col, object item)
    {
        if (col is not DataGridBoundColumn bc || bc.Binding is not Binding b || b.Path?.Path is not string path || path.Length == 0)
            return "";
        var pi = item.GetType().GetProperty(path);
        object? v = pi?.GetValue(item);
        CultureInfo loc = Loc.Culture;
        return v switch
        {
            null => "",
            double d => (b.StringFormat?.Contains("0.000") == true ? d.ToString("0.000", loc) : d.ToString("0.0", loc)),
            // enum Verdict -> текстовый вердикт как в таблице (PASS/ATTN/FAIL)
            Verdict vp => vp switch { Verdict.Pass => "PASS", Verdict.Atn => "ATTN", Verdict.Fail => "FAIL", _ => "" },
            _ => v.ToString() ?? "",
        };
    }
}
