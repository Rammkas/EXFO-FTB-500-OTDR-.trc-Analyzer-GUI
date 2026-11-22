// ==============================================================================
// Localization.cs — локализация интерфейса: украинская (по умолчанию),
// английская и русская.
//
// Единый источник строк — таблица Table (ключ -> uk/en/ru).
//   Loc.T("key")            — строка текущего языка;
//   Loc.F("key", args)      — форматирование (culture текущего языка);
//   Loc.Culture             — культура текущей локали (разделители чисел);
//   Loc.SetLanguage(code)   — смена языка: сохраняет выбор и подменяет
//                             ResourceDictionary в Application.Resources,
//                             XAML-строки {DynamicResource L.key} обновляются сами.
// XAML обращается к строкам как {DynamicResource L.key}; движок анализа
// (Engine\Analyzer.cs) формирует вердикты/причины через Loc.T/Loc.F, поэтому
// после смены языка и пересчета файлов тексты согласованы.
// ==============================================================================
using System.Globalization;
using System.IO;
using System.Windows;

namespace ExfoTrcAnalyzerGUI;

public static class Loc
{
    public const string Uk = "uk", En = "en", Ru = "ru";
    public static readonly string[] Languages = [Uk, En, Ru];

    private static volatile string _lang = LoadSaved();
    public static string Language => _lang;

    public static CultureInfo Culture { get; private set; } =
        MakeCulture(_lang);

    private static CultureInfo MakeCulture(string lang) => lang switch
    {
        Uk => TryCulture("uk-UA"),
        En => TryCulture("en-US"),
        _ => TryCulture("ru-RU"),
    };
    private static CultureInfo TryCulture(string name)
    {
        try { return CultureInfo.GetCultureInfo(name); }
        catch { return CultureInfo.InvariantCulture; }
    }

    public static string T(string key)
    {
        if (!Table.TryGetValue(key, out var row))
            return key;
        return _lang switch { En => row.En, Ru => row.Ru, _ => row.Uk };
    }

    public static string F(string key, params object?[] args)
        => string.Format(Culture, T(key), args);

    public static void SetLanguage(string lang)
    {
        if (!Languages.Contains(lang))
            lang = Uk;
        _lang = lang;
        Culture = MakeCulture(lang);
        Save(lang);
        ApplyToApplication();
    }

    // ---------- Persisted choice ----------
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExfoTrcAnalyzerGUI", "language.txt");

    private static string LoadSaved()
    {
        try
        {
            string s = File.ReadAllText(SettingsPath).Trim();
            if (Languages.Contains(s))
                return s;
        }
        catch { }
        return Uk; // по умолчанию — украинская
    }

    private static void Save(string lang)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, lang);
        }
        catch { }
    }

    // ---------- XAML resource dictionary ----------
    private static ResourceDictionary? _rd;

    public static void ApplyToApplication()
    {
        var app = Application.Current;
        if (app == null)
            return;
        var rd = new ResourceDictionary();
        foreach (var (key, row) in Table)
            rd["L." + key] = _lang switch { En => row.En, Ru => row.Ru, _ => row.Uk };
        int i = _rd != null ? app.Resources.MergedDictionaries.IndexOf(_rd) : -1;
        if (i >= 0)
            app.Resources.MergedDictionaries[i] = rd;
        else
            app.Resources.MergedDictionaries.Add(rd);
        _rd = rd;
    }

    private record Row(string Uk, string En, string Ru);

    // ============================ СТРОКИ ============================
    private static readonly Dictionary<string, Row> Table = new()
    {
        // ---- Тулбар ----
        ["btn_open_file"] = new("Відкрити файл", "Open file", "Открыть файл"),
        ["tip_open_file"] = new("Відкрити рефлектограми .trc (можна обрати кілька: Ctrl/Shift)",
                                "Open .trc traces (multiple selection: Ctrl/Shift)",
                                "Открыть рефлектограммы .trc (можно выбрать несколько: Ctrl/Shift)"),
        ["btn_open_folder"] = new("Відкрити теку", "Open folder", "Открыть папку"),
        ["tip_open_folder"] = new("Відкрити теки з рефлектограмами .trc (можна обрати кілька: Ctrl/Shift)",
                                  "Open folders with .trc traces (multiple selection: Ctrl/Shift)",
                                  "Открыть папки с рефлектограммами .trc (можно выбрать несколько: Ctrl/Shift)"),
        ["btn_clear"] = new("Очистити", "Clear", "Очистить"),
        ["tip_clear"] = new("Закрити всі файли та очистити таблиці",
                            "Close all files and clear the tables",
                            "Закрыть все файлы и очистить таблицы"),
        ["btn_export_csv"] = new("Експорт CSV", "Export CSV", "Экспорт CSV"),
        ["tip_export_csv"] = new("Вивантаження трьох таблиць у CSV",
                                 "Export the three tables to CSV",
                                 "Выгрузка трёх таблиц в CSV"),
        ["btn_print"] = new("Друк", "Print", "Печать"),
        ["tip_print"] = new("Друк звіту (якщо принтерів немає — збереження у PDF)",
                            "Print report (if there are no printers — save as PDF)",
                            "Печать отчёта (если принтеров нет — сохранение в PDF)"),
        ["btn_help"] = new("Довідка", "Help", "Справка"),
        ["tip_help"] = new("Довідка: читання вердиктів, затухання волокна vs ділянки, налаштування",
                           "Help: reading verdicts, fiber vs section attenuation, settings",
                           "Справка: чтение вердиктов, затухание волокна vs участка, настройки"),
        ["btn_settings"] = new("Налаштування", "Settings", "Настройки"),
        ["tip_settings"] = new("Пороги та параметри аналізу",
                               "Thresholds and analysis parameters",
                               "Пороги и параметры анализа"),
        ["btn_theme"] = new("Тема", "Theme", "Тема"),
        ["tip_theme"] = new("Перемкнути тему: сіра → темна → світла",
                            "Cycle theme: gray → dark → light",
                            "Переключить тему: серая → тёмная → светлая"),
        ["lbl_search"] = new("Пошук  ", "Search  ", "Поиск  "),
        ["tip_search"] = new("Фільтр по всіх вкладках: ім'я файла, ID волокна, роль, причини",
                             "Filter across all tabs: file name, fiber ID, role, reasons",
                             "Фильтр по всем вкладкам: имя файла, ID волокна, роль, причины"),

        // ---- Статус-бар ----
        ["st_files"] = new("Файлів: {0}", "Files: {0}", "Файлов: {0}"),
        ["st_pass"] = new("PASS: {0}", "PASS: {0}", "PASS: {0}"),
        ["st_fail"] = new("FAIL: {0}", "FAIL: {0}", "FAIL: {0}"),
        ["st_borderline"] = new("Пограничних: {0}", "Borderline: {0}", "Пограничных: {0}"),
        ["st_fibers"] = new("Волокон: {0}", "Fibers: {0}", "Волокон: {0}"),
        ["st_errors"] = new("Помилок: {0}", "Errors: {0}", "Ошибок: {0}"),

        // ---- Список файлов ----
        ["grp_files"] = new("Файли", "Files", "Файлы"),
        ["all_entry"] = new("Усі", "All", "Все"),
        ["tip_splitter"] = new("Клік — згорнути/розгорнути список файлів; перетягування — змінити ширину",
                               "Click — collapse/expand the file list; drag — resize the panel",
                               "Клик — свернуть/развернуть список файлов; перетаскивание — изменить ширину"),
        ["tip_error_prefix"] = new("Помилка: ", "Error: ", "Ошибка: "),

        // ---- Вкладки ----
        ["tab_fibers"] = new("Волокна", "Fibers", "Волокна"),
        ["tab_sections"] = new("Ділянки", "Sections", "Участки"),
        ["tab_events"] = new("Події", "Events", "События"),
        ["tab_map"] = new("Карта", "Map", "Карта"),

        // ---- Колонки ----
        ["col_file"] = new("Файл", "File", "Файл"),
        ["col_fiber_id"] = new("Волокно (ID)", "Fiber (ID)", "Волокно (ID)"),
        ["col_len13"] = new("Довжина 1310 (м)", "Length 1310 (m)", "Длина 1310 (м)"),
        ["col_loss13"] = new("Втрати 1310 (дБ)", "Loss 1310 (dB)", "Потери 1310 (дБ)"),
        ["col_att13"] = new("Затухання 1310 (дБ/км)", "Attenuation 1310 (dB/km)", "Затухание 1310 (дБ/км)"),
        ["col_len15"] = new("Довжина 1550 (м)", "Length 1550 (m)", "Длина 1550 (м)"),
        ["col_loss15"] = new("Втрати 1550 (дБ)", "Loss 1550 (dB)", "Потери 1550 (дБ)"),
        ["col_att15"] = new("Затухання 1550 (дБ/км)", "Attenuation 1550 (dB/km)", "Затухание 1550 (дБ/км)"),
        ["col_qc13"] = new("Коннектор ВТК 1310 (дБ)", "QC connector 1310 (dB)", "Коннектор ОТК 1310 (дБ)"),
        ["col_qc15"] = new("Коннектор ВТК 1550 (дБ)", "QC connector 1550 (dB)", "Коннектор ОТК 1550 (дБ)"),
        ["col_verdict"] = new("Вердикт", "Verdict", "Вердикт"),
        ["col_fails"] = new("Причини бракування", "Fail reasons", "Причины отбраковки"),
        ["col_warnings"] = new("Попередження", "Warnings", "Предупреждения"),
        ["col_module"] = new("Модуль", "Module", "Модуль"),
        ["module_fmt"] = new("Модуль {0}", "Module {0}", "Модуль {0}"),
        // цвета модулей ВОК по TIA-598 (1..12)
        ["color_1"] = new("Синій", "Blue", "Синий"),
        ["color_2"] = new("Помаранчевий", "Orange", "Оранжевый"),
        ["color_3"] = new("Зелений", "Green", "Зелёный"),
        ["color_4"] = new("Коричневий", "Brown", "Коричневый"),
        ["color_5"] = new("Сіро-блакитний", "Slate", "Серо-голубой"),
        ["color_6"] = new("Білий", "White", "Белый"),
        ["color_7"] = new("Червоний", "Red", "Красный"),
        ["color_8"] = new("Чорний", "Black", "Чёрный"),
        ["color_9"] = new("Жовтий", "Yellow", "Жёлтый"),
        ["color_10"] = new("Фіолетовий", "Violet", "Фиолетовый"),
        ["color_11"] = new("Індиго", "Indigo", "Индиго"),
        ["color_12"] = new("Рожевий", "Rose", "Розовый"),
        ["col_section_no"] = new("Ділянка №", "Section #", "Участок №"),
        ["col_role"] = new("Призначення", "Role", "Назначение"),
        ["col_start"] = new("Початок (м)", "Start (m)", "Начало (м)"),
        ["col_end"] = new("Кінець (м)", "End (m)", "Конец (м)"),
        ["col_reason"] = new("Причина", "Reason", "Причина"),
        ["col_event_no"] = new("Подія №", "Event #", "Событие №"),
        ["col_event_role"] = new("Роль на стенді", "Bench role", "Роль на стенде"),
        ["col_coord"] = new("Координата (м)", "Position (m)", "Координата (м)"),
        ["col_refl13"] = new("Відбиття 1310 (дБ)", "Reflection 1310 (dB)", "Отражение 1310 (дБ)"),
        ["col_refl15"] = new("Відбиття 1550 (дБ)", "Reflection 1550 (dB)", "Отражение 1550 (дБ)"),

        // ---- Карта ----
        ["btn_map_reset"] = new("Весь діапазон", "Full range", "Весь диапазон"),
        ["tip_map_reset"] = new("Показати всю трасу (скинути зум)",
                                "Show the whole trace (reset zoom)",
                                "Показать всю трассу (сбросить зум)"),
        ["btn_cursor_a"] = new("Курсор А", "Cursor A", "Курсор А"),
        ["btn_cursor_b"] = new("Курсор В", "Cursor B", "Курсор В"),
        ["tip_cursor_a"] = new("Потім клацніть по графіку — курсор А стане в точку кліку (жовта лінія). Перетягнути курсор: Ctrl + ЛКМ по його лінії",
                               "Then click on the plot — cursor A lands at the clicked point (yellow line). Drag a cursor: Ctrl + left-click on its line",
                               "Затем кликните по графику — курсор А встанет в точку клика (жёлтая линия). Перетащить курсор: Ctrl + ЛКМ по его линии"),
        ["tip_cursor_b"] = new("Потім клацніть по графіку — курсор В стане в точку кліку (блакитна лінія). Перетягнути курсор: Ctrl + ЛКМ по його лінії",
                               "Then click on the plot — cursor B lands at the clicked point (blue line). Drag a cursor: Ctrl + left-click on its line",
                               "Затем кликните по графику — курсор В встанет в точку клика (голубая линия). Перетащить курсор: Ctrl + ЛКМ по его линии"),
        ["btn_map_fiber"] = new("Волокно", "Fiber", "Волокно"),
        ["tip_map_fiber"] = new("Наблизити вікно до зони перевірюваного волокна (без буферної котушки)",
                                "Zoom the window to the fiber-under-test zone (excluding the buffer spool)",
                                "Приблизить окно к зоне проверяемого волокна (исключая буферную катушку)"),
        ["chk_auto_y"] = new("Авто Y", "Auto Y", "Авто Y"),
        ["tip_auto_y"] = new("Автопідстройка осі дБ під видиме вікно. Вимкнено: вікно по X і шкала дБ заморожені та зберігаються при зміні волокна/файла; Shift + колесо миші — зум по вертикалі (дБ)",
                             "Auto-fit the dB axis to the visible window. Off: the X window and dB scale are frozen and kept when switching fiber/file; Shift + mouse wheel — vertical (dB) zoom",
                             "Автоподстройка оси дБ под видимое окно. Выключено: окно по X и шкала дБ заморожены и сохраняются при смене волокна/файла; Shift + колесо мыши — зум по вертикали (дБ)"),
        ["lbl_1310nm"] = new("1310 нм", "1310 nm", "1310 нм"),
        ["lbl_1550nm"] = new("1550 нм", "1550 nm", "1550 нм"),
        ["map_hint"] = new("Виберіть рядок волокна або файл — тут з'явиться карта траси",
                           "Select a fiber or file row — the trace map will appear here",
                           "Выберите строку волокна или файл — здесь появится карта трассы"),

        // ---- Карта, динаміка ----
        ["map_verdict_lbl"] = new("вердикт", "verdict", "вердикт"),
        ["map_no_raw"] = new(" · немає RawSamples", " · no RawSamples", " · нет RawSamples"),
        ["map_zoom_fmt"] = new("масштаб: {0:F0}–{1:F0} м", "zoom: {0:F0}–{1:F0} m", "масштаб: {0:F0}–{1:F0} м"),
        ["cursor_fmt_a"] = new("А: {0:F1} м", "A: {0:F1} m", "А: {0:F1} м"),
        ["cursor_fmt_b"] = new("В: {0:F1} м", "B: {0:F1} m", "В: {0:F1} м"),
        ["cursor_fmt_d"] = new("Δ: {0:F1} м", "Δ: {0:F1} m", "Δ: {0:F1} м"),
        ["unit_db"] = new("дБ", "dB", "дБ"),
        ["unit_dbkm"] = new("дБ/км", "dB/km", "дБ/км"),
        ["unit_m"] = new("м", "m", "м"),
        ["unit_km"] = new("км", "km", "км"),
        ["cursor_place_hint"] = new("Курсор {0}: клацніть по графіку", "Cursor {0}: click on the plot", "Курсор {0}: кликните по графику"),
        ["cursor_a_name"] = new("А", "A", "А"),
        ["cursor_b_name"] = new("В", "B", "В"),
        ["map_no_raw_data"] = new("Немає даних RawSamples — немає що малювати",
                                  "No RawSamples data — nothing to draw",
                                  "Нет данных RawSamples — рисовать нечего"),
        ["map_no_coords"] = new("Координат траси немає — немає що малювати",
                                "No trace coordinates — nothing to draw",
                                "Координат трассы нет — рисовать нечего"),
        ["map_window_empty"] = new("У цьому вікні немає відліків траси — зсуньте або віддаліть",
                                   "No trace samples in this window — pan or zoom out",
                                   "В этом окне нет отсчётов трассы — сдвиньте или отдалите"),
        ["band_buffer"] = new("Буфер", "Buffer", "Буфер"),
        ["band_fiber"] = new("Волокно", "Fiber", "Волокно"),
        ["mark_port"] = new("Порт", "Port", "Порт"),
        ["mark_qc"] = new("ВТК", "QC", "ОТК"),
        ["mark_end"] = new("Кінець", "End", "Конец"),
        ["mark_splice"] = new("Зварювання", "Splice", "Сварка"),
        ["cursor_marker_fmt"] = new("{0} {1:F1} м", "{0} {1:F1} m", "{0} {1:F1} м"),

        // ---- Движок: ролі ----
        ["role_buffer"] = new("Буферна котушка", "Buffer spool", "Буферная катушка"),
        ["role_fiber"] = new("Перевірюване волокно", "Fiber under test", "Проверяемое волокно"),
        ["role_port"] = new("Порт рефлектометра", "Reflectometer port", "Порт рефлектометра"),
        ["role_end"] = new("Кінець волокна", "Fiber end", "Конец волокна"),
        ["role_qc"] = new("Коннектор ВТК (вхід)", "QC connector (input)", "Коннектор ОТК (вход)"),
        ["role_splice"] = new("Зварювання / Подія всередині", "Splice / internal event", "Сварка / Событие внутри"),

        // ---- Движок: причини/попередження ----
        ["reason_att13"] = new("Затух. 1310 > {0} ({1:F3})", "Attenuation 1310 > {0} ({1:F3})", "Затух. 1310 > {0} ({1:F3})"),
        ["reason_att15"] = new("Затух. 1550 > {0} ({1:F3})", "Attenuation 1550 > {0} ({1:F3})", "Затух. 1550 > {0} ({1:F3})"),
        ["reason_no13"] = new("Немає даних 1310 (довжина/затухання ділянки відсутні)",
                              "No 1310 data (section length/attenuation missing)",
                              "Нет данных 1310 (длина/затухание участка отсутствуют)"),
        ["reason_no15"] = new("Немає даних 1550 (довжина/затухання ділянки відсутні)",
                              "No 1550 data (section length/attenuation missing)",
                              "Нет данных 1550 (длина/затухание участка отсутствуют)"),
        ["reason_section"] = new("Ділянка {0}: {1}", "Section {0}: {1}", "Участок {0}: {1}"),
        ["reason_qc13"] = new("Втрати коннектора ВТК 1310 ({0:F3} > {1} дБ)",
                              "QC connector loss 1310 ({0:F3} > {1} dB)",
                              "Потери коннектора ОТК 1310 ({0:F3} > {1} дБ)"),
        ["reason_qc15"] = new("Втрати коннектора ВТК 1550 ({0:F3} > {1} дБ)",
                              "QC connector loss 1550 ({0:F3} > {1} dB)",
                              "Потери коннектора ОТК 1550 ({0:F3} > {1} дБ)"),
        ["reason_splice13"] = new("Втрати 1310 ({0:F3} > {1} дБ)", "Loss 1310 ({0:F3} > {1} dB)", "Потери 1310 ({0:F3} > {1} дБ)"),
        ["reason_splice15"] = new("Втрати 1550 ({0:F3} > {1} дБ)", "Loss 1550 ({0:F3} > {1} dB)", "Потери 1550 ({0:F3} > {1} дБ)"),
        ["reason_event_fail"] = new("{0} {1:F1} м: {2}", "{0} {1:F1} m: {2}", "{0} {1:F1} м: {2}"),
        ["warn_buffer_missing"] = new(
            "has_buffer_spool=True, але ділянку-буфер не знайдено — можливо, вимірювання без буферної " +
            "котушки: вимкніть has_buffer_spool (інакше буфер, якщо він є, увійде до перевірюваного волокна).",
            "has_buffer_spool=True but no buffer section was found — the measurement may be without a " +
            "buffer spool: set has_buffer_spool=False (otherwise the buffer, if any, becomes part of the fiber under test).",
            "has_buffer_spool=True, но участок-буфер не найден — возможно, замер без буферной " +
            "катушки: поставьте has_buffer_spool=False (иначе буфер, если он есть, войдет в проверяемое волокно)."),
        ["warn_all_buffer"] = new(
            "усі ділянки позначені як буфер — довжина перевірюваного волокна 0 " +
            "(можливо, перевірюване волокно прийнято за буфер): перевірте has_buffer_spool.",
            "all sections are marked as buffer — the fiber-under-test length is 0 " +
            "(the fiber under test may be mistaken for the buffer): check has_buffer_spool.",
            "все участки помечены как буфер — длина проверяемого волокна 0 " +
            "(возможно, проверяемое волокно принято за буфер): проверьте has_buffer_spool."),

        // ---- GUI: жовта зона ----
        ["note_att_avg"] = new("Середнє затухання {0} у жовтій зоні: {1:F3} дБ/км (порог {2:F3}, ±{3:0.#}%)",
                               "Average {0} attenuation in the yellow zone: {1:F3} dB/km (threshold {2:F3}, ±{3:0.#}%)",
                               "Среднее затухание {0} в жёлтой зоне: {1:F3} дБ/км (порог {2:F3}, ±{3:0.#}%)"),
        ["note_att_section"] = new("Затухання ділянки {0} у жовтій зоні: {1:F3} дБ/км (порог {2:F3}, ±{3:0.#}%)",
                                   "Section {0} attenuation in the yellow zone: {1:F3} dB/km (threshold {2:F3}, ±{3:0.#}%)",
                                   "Затухание участка {0} в жёлтой зоне: {1:F3} дБ/км (порог {2:F3}, ±{3:0.#}%)"),
        ["entry_note_borderline"] = new("Затухання в жовтій зоні: у межах ±{0:0.#}% від порога",
                                        "Attenuation in the yellow zone: within ±{0:0.#}% of the threshold",
                                        "Затухание в жёлтой зоне: в пределах ±{0:0.#}% от порога"),
        ["err_no_fibers"] = new("У файлі не знайдено волокон (OtdrData/Fibers)",
                                "No fibers found in the file (OtdrData/Fibers)",
                                "В файле не найдено волокон (OtdrData/Fibers)"),
        ["err_trc_root"] = new("У файлі не знайдено підтверджений кореневий вузол OtdrFile",
                               "Confirmed OtdrFile root node not found in the file",
                               "В файле не найден подтверждённый корневой узел OtdrFile"),
        ["tip_fails_prefix"] = new("Причини бракування:", "Fail reasons:", "Причины отбраковки:"),
        ["tip_warnings_prefix"] = new("Попередження:", "Warnings:", "Предупреждения:"),

        // ---- Діалоги/повідомлення ----
        ["dlg_open_file"] = new("Відкрити рефлектограми .trc", "Open .trc traces", "Открыть рефлектограммы .trc"),
        ["filter_trc"] = new("Рефлектограми EXFO (*.trc)|*.trc|Всі файли (*.*)|*.*",
                             "EXFO traces (*.trc)|*.trc|All files (*.*)|*.*",
                             "Рефлектограммы EXFO (*.trc)|*.trc|Все файлы (*.*)|*.*"),
        ["dlg_open_folder"] = new("Відкрити теки з рефлектограмами .trc", "Open folders with .trc traces", "Открыть папки с рефлектограммами .trc"),
        ["dlg_export_folder"] = new("Тека для вивантаження CSV", "Folder for CSV export", "Папка для выгрузки CSV"),
        ["msg_no_export"] = new("Немає даних для експорту — спершу відкрийте файл або теку.",
                                "No data to export — open a file or folder first.",
                                "Нет данных для экспорта — сначала откройте файл или папку."),
        ["title_export"] = new("Експорт CSV", "Export CSV", "Экспорт CSV"),
        ["msg_export_ok"] = new("Збережено у '{0}': summary_fibers.csv, sections_detail.csv, events_detail.csv",
                                "Saved to '{0}': summary_fibers.csv, sections_detail.csv, events_detail.csv",
                                "Сохранено в '{0}': summary_fibers.csv, sections_detail.csv, events_detail.csv"),
        ["msg_export_fail"] = new("Не вдалося записати CSV: {0}", "Failed to write CSV: {0}", "Не удалось записать CSV: {0}"),
        ["dlg_save_pdf"] = new("Принтери не знайдені — збережіть звіт у PDF",
                               "No printers found — save the report as PDF",
                               "Принтеры не найдены — сохраните отчёт в PDF"),
        ["filter_pdf"] = new("PDF-файл|*.pdf", "PDF file|*.pdf", "PDF-файл|*.pdf"),
        ["report_saved"] = new("Звіт збережено: {0}", "Report saved: {0}", "Отчёт сохранён: {0}"),
        ["report_saved_builtin"] = new("Звіт збережено (вбудований генератор): {0}",
                                       "Report saved (built-in generator): {0}",
                                       "Отчёт сохранён (встроенный генератор): {0}"),
        ["msg_pdf_fail"] = new("Не вдалося зберегти PDF: {0}", "Failed to save PDF: {0}", "Не удалось сохранить PDF: {0}"),
        ["title_print"] = new("Друк", "Print", "Печать"),
        ["err_no_browser"] = new("Microsoft Edge або Google Chrome не знайдено",
                                 "Microsoft Edge or Google Chrome not found",
                                 "Microsoft Edge или Google Chrome не найдены"),
        ["err_browser_start"] = new("Не вдалося запустити {0}", "Failed to start {0}", "Не удалось запустить {0}"),
        ["err_browser_pdf"] = new("браузер не створив PDF-файл", "the browser did not create the PDF file", "браузер не создал PDF-файл"),

        // ---- Звіт (печать) ----
        ["report_title"] = new("EXFO TRC Analyzer — звіт", "EXFO TRC Analyzer — report", "EXFO TRC Analyzer — отчёт"),
        ["scope_file"] = new("файл: {0}", "file: {0}", "файл: {0}"),
        ["scope_all"] = new("всі файли ({0})", "all files ({0})", "все файлы ({0})"),
        ["search_lbl"] = new("пошук: {0}", "search: {0}", "поиск: {0}"),
        ["search_none"] = new("немає", "none", "нет"),
        ["map_missing"] = new("карта не відображається — виберіть рядок волокна або файла",
                              "map not shown — select a fiber or file row",
                              "карта не отображается — выберите строку волокна или файла"),
        ["no_rows"] = new("немає рядків", "no rows", "нет строк"),

        // ---- Вікно налаштувань ----
        ["set_title"] = new("Налаштування аналізу", "Analysis settings", "Настройки анализа"),
        ["set_buffer_group"] = new("Буферна котушка стенда", "Bench buffer spool", "Буферная катушка стенда"),
        ["set_has_buffer"] = new("Перша ділянка (~1 км) — буферна котушка (has_buffer_spool)",
                                 "First section (~1 km) is the buffer spool (has_buffer_spool)",
                                 "Первый участок (~1 км) — буферная катушка (has_buffer_spool)"),
        ["set_buffer_min"] = new("Довжина буфера: мін (м)", "Buffer length: min (m)", "Длина буфера: мин (м)"),
        ["set_buffer_max"] = new("Довжина буфера: макс (м)", "Buffer length: max (m)", "Длина буфера: макс (м)"),
        ["set_norms_group"] = new("Норми (пороги бракування)", "Limits (fail thresholds)", "Нормы (пороги отбраковки)"),
        ["set_att13"] = new("Затухання 1310 макс (дБ/км)", "Max attenuation 1310 (dB/km)", "Затухание 1310 макс (дБ/км)"),
        ["set_att15"] = new("Затухання 1550 макс (дБ/км)", "Max attenuation 1550 (dB/km)", "Затухание 1550 макс (дБ/км)"),
        ["set_splice"] = new("Втрати зварювання макс (дБ)", "Max splice loss (dB)", "Потери сварки макс (дБ)"),
        ["set_match"] = new("Допуск зіставлення 1310/1550 (м)", "1310/1550 match tolerance (m)", "Допуск сопоставления 1310/1550 (м)"),
        ["set_qc"] = new("Втрати QC-коннектора макс (дБ)", "Max QC connector loss (dB)", "Потери QC-коннектора макс (дБ)"),
        ["set_yellow"] = new("Жовта зона: допуск від порога затухання (%)",
                             "Yellow zone: tolerance from the attenuation threshold (%)",
                             "Жёлтая зона: допуск от порога затухания (%)"),
        ["set_qc_check"] = new("Перевіряти вхідний роз'єм ВТК на брак (check_qc_connector)",
                               "Reject-check the input QC connector (check_qc_connector)",
                               "Проверять входной разъём ОТК на брак (check_qc_connector)"),
        ["set_show_losses"] = new("Показувати стовпці «Втрати» (дБ) у вкладках",
                                  "Show “Loss” (dB) columns in the tabs",
                                  "Показывать столбцы «Потери» (дБ) во вкладках"),
        ["set_compare"] = new("Режим порівняння: курсори А/В зберігаються при перемиканні волокна/файла",
                              "Compare mode: A/B cursors are kept when switching fiber/file",
                              "Режим сравнения: курсоры А/В сохраняются при переключении волокна/файла"),
        ["set_language"] = new("Мова інтерфейсу", "Interface language", "Мова интерфейса"),
        ["set_colors_group"] = new("Кольори модулів", "Module colors", "Цвета модулей"),
        ["set_colors_btn"] = new("Кольори модулів…", "Module colors…", "Цвета модулей…"),
        ["set_colors_note"] = new("Стандартний порядок маркування ВОК (TIA-598): синій, помаранчевий, зелений, " +
                                  "коричневий, сіро-блакитний, білий, червоний, чорний, жовтий, фіолетовий, індиго, " +
                                  "рожевий. Тут колір можна призначити кожному модулю інакше (наприклад, якщо " +
                                  "трубки кабелю пофарбовані в іншому порядку). Вибір зберігається.",
                                  "Standard fiber marking order (TIA-598): blue, orange, green, brown, slate, white, " +
                                  "red, black, yellow, violet, indigo, rose. Here you can assign a different color to " +
                                  "each module (e.g. if the cable tubes are colored in another order). The choice is saved.",
                                  "Стандартный порядок маркировки ВОК (TIA-598): синий, оранжевый, зелёный, коричневый, " +
                                  "серо-голубой, белый, чёрный, жёлтый, фиолетовый, индиго, розовый. Здесь цвет можно " +
                                  "назначить каждому модулю иначе (например, если трубки кабеля окрашены в другом " +
                                  "порядке). Выбор сохраняется."),
        ["btn_save"] = new("Зберегти", "Save", "Сохранить"),
        ["btn_cancel"] = new("Скасувати", "Cancel", "Отмена"),
        ["btn_reset"] = new("Скинути до типових", "Reset to defaults", "Сбросить по умолчанию"),
        ["val_buf_min"] = new("Буфер мін", "Buffer min", "Буфер мин"),
        ["val_buf_max"] = new("Буфер макс", "Buffer max", "Буфер макс"),
        ["val_att13"] = new("Затухання 1310", "Attenuation 1310", "Затухание 1310"),
        ["val_att15"] = new("Затухання 1550", "Attenuation 1550", "Затухание 1550"),
        ["val_splice"] = new("Втрати зварювання", "Splice loss", "Потери сварки"),
        ["val_match"] = new("Допуск зіставлення", "Match tolerance", "Допуск сопоставления"),
        ["val_qc"] = new("QC-коннектор", "QC connector", "QC-коннектор"),
        ["val_yellow"] = new("Жовта зона (%)", "Yellow zone (%)", "Жёлтая зона (%)"),
        ["val_not_number"] = new("{0}: не число — «{1}»", "{0}: not a number — “{1}”", "{0}: не число — «{1}»"),
        ["val_positive"] = new("{0}: має бути більше 0", "{0}: must be greater than 0", "{0}: должно быть больше 0"),
        ["val_yellow_100"] = new("Жовта зона: допуск має бути менше 100%",
                                 "Yellow zone: tolerance must be below 100%",
                                 "Жёлтая зона: допуск должен быть меньше 100%"),
        ["val_buffer_order"] = new("Буфер: мінімальна довжина має бути менше максимальної",
                                   "Buffer: minimum length must be below the maximum",
                                   "Буфер: минимальная длина должна быть меньше максимальной"),

        // ---- Діалог друку ----
        ["po_title"] = new("Друк — що включити у звіт", "Print — what to include in the report", "Печать — что включить в отчёт"),
        ["po_map"] = new("Карта (графік поточної траси)", "Map (current trace plot)", "Карта (график текущей трассы)"),
        ["po_fibers"] = new("Таблиця «Волокна»", "“Fibers” table", "Таблица «Волокна»"),
        ["po_sections"] = new("Таблиця «Ділянки»", "“Sections” table", "Таблица «Участки»"),
        ["po_events"] = new("Таблиця «Події»", "“Events” table", "Таблица «События»"),
        ["po_note"] = new("У таблиці потрапляють рядки у тому вигляді, як вони видно зараз: фільтр за файлом, пошук і сортування.",
                          "Tables include rows exactly as currently visible: file filter, search and sorting.",
                          "В таблицы попадают строки в том виде, как они видны сейчас: фильтр по файлу, поиск и сортировка."),

        // ---- Довідка ----
        ["help_title"] = new("Довідка — EXFO TRC Analyzer", "Help — EXFO TRC Analyzer", "Справка — EXFO TRC Analyzer"),
        ["help_head"] = new("EXFO TRC Analyzer (GUI) — довідка", "EXFO TRC Analyzer (GUI) — help", "EXFO TRC Analyzer (GUI) — справка"),
        ["help_verdicts_head"] = new("Читання вердиктів (кружки в списку файлів)",
                                     "Reading verdicts (dots in the file list)",
                                     "Чтение вердиктов (кружки в списке файлов)"),
        ["help_green"] = new("зелений — PASS: волокно пройшло всі перевірки, затухання із запасом нижче порога",
                             "green — PASS: the fiber passed all checks, attenuation comfortably below the threshold",
                             "зелёный — PASS: волокно прошло все проверки, затухание с запасом ниже порога"),
        ["help_yellow"] = new("жовтий — «жовта зона»: виміряне затухання в межах ±N% від порога (N налаштовується, типово 5%). " +
                              "Сигнал «придивитися», перекриває зелений і червоний. Таке волокно отримує вердикт ATTN замість PASS",
                              "yellow — «yellow zone»: measured attenuation within ±N% of the threshold (N configurable, default 5%). " +
                              "A “take a closer look” signal, overrides green and red. Such a fiber gets an ATTN verdict instead of PASS",
                              "жёлтый — «жёлтая зона»: измеренное затухание в пределах ±N% от порога (N настраивается, по умолчанию 5%). " +
                              "Сигнал «присмотреться», перекрывает зелёный и красный. Такое волокно получает вердикт ATTN вместо PASS"),
        ["help_red"] = new("червоний — FAIL: є порушення (ділянки/події вище норм), затухання при цьому не пограничне",
                           "red — FAIL: violations exist (sections/events above limits), attenuation is not borderline",
                           "красный — FAIL: есть нарушения (участки/события выше норм), затухание при этом не пограничное"),
        ["help_gray"] = new("сірий — файл не розібрано (помилка) або в ньому немає волокон; текст помилки — у підказці при наведенні",
                            "gray — file not parsed (error) or contains no fibers; the error text is shown in the hover tooltip",
                            "серый — файл не разобран (ошибка) или в нём нет волокон; текст ошибки — в подсказке при наведении"),
        ["help_att_head"] = new("Важливо: два різні «затухання»", "Important: two different “attenuations”", "Важно: два разных «затухания»"),
        ["help_att1"] = new("У колонці «Затухання 1310/1550 (дБ/км)» на вкладці «Волокна» — СЕРЕДНЄ затухання по всьому " +
                            "перевірюваному волокну: сумарні втрати, поділені на сумарну довжину.",
                            "The “Attenuation 1310/1550 (dB/km)” column on the Fibers tab is the AVERAGE attenuation of the " +
                            "whole fiber under test: total loss divided by total length.",
                            "В колонке «Затухание 1310/1550 (дБ/км)» на вкладке «Волокна» — СРЕДНЕЕ затухание по всему " +
                            "проверяемому волокну: суммарные потери делённые на суммарную длину."),
        ["help_att2"] = new("У підказках «Причини бракування» і на вкладці «Ділянки» — затухання КОНКРЕТНОЇ ДІЛЯНКИ " +
                            "(перевірка йде по кожній ділянці окремо).",
                            "“Fail reasons” tooltips and the Sections tab show the attenuation of a SPECIFIC SECTION " +
                            "(each section is checked separately).",
                            "В подсказках «Причины отбраковки» и на вкладке «Участки» — затухание КОНКРЕТНОГО УЧАСТКА " +
                            "(проверка идёт по каждому участку отдельно)."),
        ["help_att3"] = new("Дані перевірюваної ділянки мають бути на обох довжинах хвиль: якщо ділянка є на 1550, " +
                            "а на 1310 немає ні довжини, ні затухання (або навпаки) — це FAIL «Немає даних 1310/1550 " +
                            "(довжина/затухання ділянки відсутні)».",
                            "A section under test must have data at both wavelengths: if it exists at 1550 but has " +
                            "neither length nor attenuation at 1310 (or vice versa) it is a FAIL " +
                            "“No 1310/1550 data (section length/attenuation missing)”.",
                            "Данные проверяемого участка должны быть на обеих длинах волн: если участок есть на 1550, " +
                            "а на 1310 нет ни длины, ни затухания (или наоборот) — это FAIL «Нет данных 1310/1550 " +
                            "(длина/затухание участка отсутствуют)»."),
        ["help_att4"] = new("Вердикт ATTN (пограничне затухання): «жовта зона» перевіряється окремо для волокна " +
                            "(за СЕРЕДНІМ затуханням) і для кожної ділянки (за затуханням ділянки). Волокно ATTN " +
                            "отримує запис у «Причинах бракування» — яке затухання і на якій довжині хвилі потрапило " +
                            "у жовту зону; ділянка PASS у жовтій зоні також стає ATTN з поясненням у колонці «Причина». " +
                            "Оскільки середнє затухання волокна і затухання ділянок — різні числа, можливе волокно ATTN " +
                            "за всіх PASS-ділянок (і навпаки).",
                            "ATTN verdict (borderline attenuation): the “yellow zone” is checked separately for the fiber " +
                            "(by AVERAGE attenuation) and for each section (by its own attenuation). An ATTN fiber gets an " +
                            "entry in “Fail reasons” — which attenuation at which wavelength fell into the yellow zone; " +
                            "a PASS section in the yellow zone also becomes ATTN with an explanation in the “Reason” column. " +
                            "Since the fiber's average attenuation and section attenuations are different numbers, a fiber " +
                            "can be ATTN while all its sections are PASS (and vice versa).",
                            "Вердикт ATTN (пограничное затухание): «жёлтая зона» проверяется отдельно для волокна " +
                            "(по СРЕДНЕМУ затуханию) и для каждого участка (по затуханию участка). Волокно ATTN " +
                            "получает запись в «Причинах отбраковки» — какое затухание и на какой длине волны попало " +
                            "в жёлтую зону; участок PASS в жёлтой зоне тоже становится ATTN с пояснением в колонке «Причина». " +
                            "Так как среднее затухание волокна и затухание участков — разные числа, возможно волокно ATTN " +
                            "при всех PASS-участках (и наоборот)."),
        ["help_att5"] = new("Тому в підказці може стояти число більше, ніж у колонці волокна: наприклад, волокно " +
                            "загалом 0,608 дБ/км, а окрема ділянка всередині нього — 1,500 дБ/км, і саме вона викликала " +
                            "FAIL. Середнє «розмиває» погану ділянку по довжині, тому нормується і перевіряється " +
                            "затухання ділянки — коротка погана бухта не повинна ховатися за довгою хорошою.",
                            "So a tooltip may show a number larger than the fiber's column: e.g. the fiber as a whole is " +
                            "0.608 dB/km while one section inside it is 1.500 dB/km, and that section caused the FAIL. " +
                            "The average “smears” a bad section over the length, so the per-section attenuation is the " +
                            "normalized, checked quantity — a short bad fiber span must not hide behind a long good one.",
                            "Поэтому в подсказке может стоять число больше, чем в колонке волокна: например, волокно " +
                            "в целом 0,608 дБ/км, а отдельный участок внутри него — 1,500 дБ/км, и именно он вызвал " +
                            "FAIL. Среднее «размазывает» плохой участок по длине, поэтому нормируется и проверяется " +
                            "затухание участка — короткая плохая бухта не должна прятаться за длинной хорошей."),
        ["help_nav_head"] = new("Навігація та таблиці", "Navigation and tables", "Навигация и таблицы"),
        ["help_nav1"] = new("• Рядок «Усі» на початку списку файлів — показати рядки всіх волокон; вибір файла " +
                            "у списку — фільтрує всі вкладки, включно з «Волокна».",
                            "• The “All” row at the top of the file list shows all fibers; selecting a file filters " +
                            "every tab, including “Fibers”.",
                            "• Строка «Все» в начале списка файлов — показать строки всех волокон; выбор файла в " +
                            "списке — фильтрует все вкладки, включая «Волокна»."),
        ["help_nav2"] = new("• Вибір рядка на вкладці «Волокна» — рядки цієї вкладки залишаються як є; список " +
                            "файлів прокручується до файла рядка та виділяє його, а «Ділянки», «Події» і «Карта» " +
                            "показують лише цей файл.",
                            "• Selecting a row on the Fibers tab leaves that tab as is; the file list scrolls to the " +
                            "row's file and highlights it, while “Sections”, “Events” and “Map” show only that file.",
                            "• Выбор строки на вкладке «Волокна» — строки этой вкладки остаются как есть; список " +
                            "файлов прокручивается к файлу строки и выделяет его, а «Участки», «События» и «Карта» " +
                            "показывают только этот файл."),
        ["help_nav3"] = new("• Пошук у тулбарі — живий фільтр по всіх вкладках (ім'я файла, ID волокна, роль, " +
                            "причини), комбінується з вибором файла.",
                            "• The toolbar search is a live filter across all tabs (file name, fiber ID, role, reasons), " +
                            "combined with the selected file.",
                            "• Поиск в тулбаре — живой фильтр по всем вкладкам (имя файла, ID волокна, роль, причины), " +
                            "комбинируется с выбором файла."),
        ["help_nav4"] = new("• Сортування — клік по заголовку колонки, повторний клік змінює напрямок. Колонки " +
                            "«Затухання 1310/1550» при першому кліці сортуються за спаданням (найгірші вгорі). " +
                            "Порожні колонки (наприклад «Попередження», якщо попереджень немає) не сортуються — " +
                            "клік по такому заголовку нічого не робить.",
                            "• Sorting — click a column header, click again to reverse. “Attenuation 1310/1550” columns " +
                            "sort descending on the first click (worst on top). Empty columns (e.g. “Warnings” when " +
                            "there are none) are not sortable — clicking such a header does nothing.",
                            "• Сортировка — клик по заголовку колонки, повторный клик меняет направление. Колонки " +
                            "«Затухание 1310/1550» при первом клике сортируются по убыванию (худшие сверху). Пустые " +
                            "колонки (например «Предупреждения», если предупреждений нет) не сортируются — клик по " +
                            "такому заголовку ничего не делает."),
        ["help_nav5"] = new("• Підказки при наведенні: на файлі — помилки/помітки, на рядку таблиці — причини та " +
                            "попередження.",
                            "• Hover tooltips: on a file — errors/notes; on a table row — reasons and warnings.",
                            "• Подсказки при наведении: на файле — ошибки/заметки, на строке таблицы — причины и предупреждения."),
        ["help_nav6"] = new("• Вкладки повторюють CSV консольної версії: «Волокна» = summary_fibers.csv, " +
                            "«Ділянки» = sections_detail.csv, «Події» = events_detail.csv.",
                            "• The tabs mirror the console CSVs: Fibers = summary_fibers.csv, Sections = " +
                            "sections_detail.csv, Events = events_detail.csv.",
                            "• Вкладки повторяют CSV консольной версии: «Волокна» = summary_fibers.csv, " +
                            "«Участки» = sections_detail.csv, «События» = events_detail.csv."),
        ["help_map_head"] = new("Вкладка «Карта»", "The “Map” tab", "Вкладка «Карта»"),
        ["help_map1"] = new("Показується траса вибраного волокна: сира крива RawSamples (дБ/метри), 1310 нм — " +
                            "синя, 1550 нм — оранжева; смуги ділянок (Буфер/Волокно), пунктирні маркери подій " +
                            "(Порт, ВТК, Зварювання, Кінець).",
                            "The selected fiber's trace is shown: the raw RawSamples curve (dB/meters), 1310 nm in blue, " +
                            "1550 nm in orange; section bands (Buffer/Fiber), dashed event markers (Port, QC, Splice, End).",
                            "Показывается трасса выбранного волокна: сырая кривая RawSamples (дБ/метры), 1310 нм — синя, " +
                            "1550 нм — оранжевая; полосы участков (Буфер/Волокно), пунктирные маркеры событий (Порт, ОТК, Сварка, Конец)."),
        ["help_map2"] = new("Зум — колесо миші (наближення до точки під курсором), панорамування — перетягування " +
                            "лівою кнопкою, скидання зуму — подвійний клік або кнопка «Весь діапазон». При вимкненому " +
                            "«Авто Y»: Shift + колесо миші — зум по вертикалі (шкала дБ) навколо точки під курсором.",
                            "Zoom — mouse wheel (zooming toward the point under the cursor), panning — left-drag, " +
                            "reset — double click or the “Full range” button. With “Auto Y” off: Shift + wheel — " +
                            "vertical (dB scale) zoom around the point under the cursor.",
                            "Зум — колесо мыши (приближение к точке под курсором), панорамирование — перетаскивание левой " +
                            "кнопкой, сброс зума — двойной клик или кнопка «Весь диапазон». При выключенном «Авто Y»: " +
                            "Shift + колесо мыши — зум по вертикали (шкала дБ) вокруг точки под курсором."),
        ["help_map3"] = new("Кнопка «Волокно» — наблизити вікно до зони перевірюваного волокна (без буферної " +
                            "котушки). «Курсор А» / «Курсор В» — вимірювальні курсори: натисніть кнопку, потім " +
                            "клацніть по графіку; вертикальні лінії (А — жовта, В — блакитна) показують позицію (м) " +
                            "і рівень (дБ) кожного курсора та Δ між ними (відстань і втрати дБ) у рядку під кнопками. " +
                            "Повторне натискання кнопки дозволяє переставити курсор; курсор також можна взяти мишею " +
                            "із затиснутим Ctrl (Ctrl + ЛКМ по його лінії) і перетягувати по графіку, показання " +
                            "оновлюються на льоту; при виборі іншого волокна курсори скидаються (крім режиму " +
                            "порівняння — див. «Налаштування»).",
                            "The “Fiber” button zooms the window to the fiber-under-test zone (without the buffer " +
                            "spool). “Cursor A” / “Cursor B” — measuring cursors: press the button, then click the plot; " +
                            "vertical lines (A yellow, B blue) show each cursor's position (m) and level (dB) plus the Δ " +
                            "between them (distance and loss in dB) in the line under the buttons. Pressing the button " +
                            "again lets you move the cursor; a cursor can also be grabbed with Ctrl held (Ctrl + " +
                            "left-click on its line) and dragged along the plot, readings update live; cursors reset " +
                            "when another fiber is selected (except in compare mode — see “Settings”).",
                            "Кнопка «Волокно» — приблизить окно к зоне проверяемого волокна (без буферной катушки). " +
                            "«Курсор А» / «Курсор В» — измерительные курсоры: нажмите кнопку, затем кликните по графику; " +
                            "вертикальные линии (А — жёлтая, В — голубая) показывают позицию (м) и уровень (дБ) каждого " +
                            "курсора и Δ между ними (расстояние и потери дБ) в строке под кнопками. Повторное нажатие " +
                            "кнопки позволяет переставить курсор; курсор можно взять мышью с зажатым Ctrl (Ctrl + ЛКМ по " +
                            "его линии) и перетаскивать по графику, показания обновляются на лету; при выборе другого " +
                            "волокна курсоры сбрасываются (кроме режима сравнения — см. «Настройки»)."),
        ["help_map4"] = new("Вісь дБ підлаштовується автоматично під відліки всередині поточного вікна; вікно дБ " +
                            "не звужується менше 12 дБ, вікно відстані — менше 10 м.",
                            "The dB axis auto-fits the samples inside the current window; the dB window never narrows " +
                            "below 12 dB, the distance window below 10 m.",
                            "Ось дБ подстраивается автоматически под отсчёты внутри текущего окна; окно дБ не сужается " +
                            "меньше 12 дБ, окно расстояния — меньше 10 м."),
        ["help_map5"] = new("Перемикач «Авто Y»: увімкнено (типово) — масштаб по вертикалі перераховується під " +
                            "видиме вікно при кожному зумі, при виборі нового волокна/файла вікно X скидається на " +
                            "повний діапазон; вимкнено — заморожуються і шкала дБ, і вікно по X (зум/панорама в " +
                            "метрах): вони зберігаються при зміні волокна чи файла. можна навести вікно на коннектор " +
                            "(~1010 м), наблизити та дивитися ту саму область у різних файлах; піки за краї вікна " +
                            "обрізаються. Shift + колесо — ручне наближення/віддалення по вертикалі (дБ) у замороженому " +
                            "вікні. Кнопка «Весь діапазон»/подвійний клік скидають вікно X в будь-якому режимі; " +
                            "увімкнення «Авто Y» повертає живу підстройку.",
                            "The “Auto Y” switch: on (default) — the vertical scale is recomputed for the visible window " +
                            "at every zoom, and the X window resets to the full range when a new fiber/file is selected; " +
                            "off — both the dB scale and the X window (zoom/pan in meters) are frozen and kept when " +
                            "switching fiber or file: you can place the window over the connector (~1010 m), zoom in and " +
                            "watch the same region in different files; peaks outside the window are clipped. " +
                            "Shift + wheel — manual vertical (dB) zoom in the frozen window. “Full range”/double click " +
                            "resets the X window in any mode; turning “Auto Y” back on restores live fitting.",
                            "Переключатель «Авто Y»: включён (по умолчанию) — масштаб по вертикали пересчитывается под " +
                            "видимое окно при каждом зуме, при выборе нового волокна/файла окно X сбрасывается на полный " +
                            "диапазон; выключен — замораживаются и шкала дБ, и окно по X (зум/панорама в метрах): они " +
                            "сохраняются при смене волокна или файла. Можно навести окно на коннектор (~1010 м), " +
                            "приблизить и смотреть одну и ту же область в разных файлах; пики за края окна обрезаются. " +
                            "Shift + колесо — ручное приближение/отдаление по вертикали (дБ) в замороженном окне. Кнопка " +
                            "«Весь диапазон»/двойной клик сбрасывают окно X в любом режиме; включение «Авто Y» " +
                            "возвращает живую подстройку."),
        ["help_mod_head"] = new("Модулі кабелю (вкладка «Волокна»)", "Cable modules (the “Fibers” tab)", "Модули кабеля (вкладка «Волокна»)")
        ,
        ["help_mod1"] = new("Якщо ім'я папки описує структуру кабеля (наприклад «02579 OBgPo-96A8(8x12)-2,7»: " +
                            "96 волокон, (8x12) — 8 модулів по 12 волокон), волокна у вкладці «Волокна» групуються " +
                            "за модулями: сортування за номером файла та нарізання підряд по 12 (перші 12 — модуль 1, " +
                            "далі модуль 2...). Заголовки груп «Модуль N — колір (кількість)» можна згортати. " +
                            "Групування вмикається лише якщо файлов рівно стільки,скільки заявлено в імені папки, і всі " +
                            "імена файлів числові; інакше список залишається плоским.",
                            "If the folder name describes the cable structure (e.g. “02579 OBgPo-96A8(8x12)-2,7”: " +
                            "96 fibers, (8x12) — 8 modules of 12 fibers), fibers in the “Fibers” tab are grouped by " +
                            "modules: sorted by file number and cut in consecutive blocks of 12 (first 12 — module 1, " +
                            "next — module 2...). The group headers “Module N — color (count)” are collapsible. " +
                            "Grouping turns on only when the folder holds exactly as many files as the name states and " +
                            "all file names are numeric; otherwise the list stays flat.",
                            "Если имя папки описывает структуру кабеля (например «02579 OBgPo-96A8(8x12)-2,7»: " +
                            "96 волокон, (8x12) — 8 модулей по 12 волокон), волокна во вкладке «Волокна» группируются " +
                            "по модулям: сортировка по номеру файла и нарезка подряд по 12 (первые 12 — модуль 1, " +
                            "далее модуль 2…). Заголовки групп «Модуль N — цвет (количество)» сворачиваются. " +
                            "Группировка включается только если файлов ровно столько, сколько заявлено в имени папки, " +
                            "и все имена файлов числовые; иначе список остаётся плоским."),
        ["help_mod2"] = new("Кожний модуль має стандартний колір маркировки ВОК (порядок TIA-598, той самий, " +
                            "що для волокон та буферних трубок): 1 синій, 2 помаранчевий, 3 зелений, 4 коричневий, " +
                            "5 сіро-блакитний (slate), 6 білий, 7 червоний, 8 чорний, 9 жовтий, 10 фіолетовий, " +
                            "11 індиго, 12 рожевий; після 12-го цикл повторюється. Колір показано плашкою біля " +
                            "назви групи та кольоровою лівою межею рядків групи.",
                            "Each module gets the standard fiber-optic marking color (TIA-598 order, the same as for " +
                            "fibers and buffer tubes): 1 blue, 2 orange, 3 green, 4 brown, 5 slate, 6 white, 7 red, " +
                            "8 black, 9 yellow, 10 violet, 11 indigo, 12 rose; after the 12th the cycle repeats. " +
                            "The color is shown as a swatch next to the group name and as a colored left border of " +
                            "the group's rows.",
                            "Каждый модуль имеет стандартный цвет маркировки ВОК (порядок TIA-598, тот же, что для " +
                            "волокон и буферных трубок): 1 синий, 2 оранжевый, 3 зелёный, 4 коричневый, " +
                            "5 серо-голубой (slate), 6 белый, 7 красный, 8 чёрный, 9 жёлтый, 10 фиолетовый, " +
                            "11 индиго, 12 розовый; после 12-го цикл повторяется. Цвет показан плашкой возле названия " +
                            "группы и цветной левой границей строк группы."),
        ["help_mod3"] = new("Завантаження кількох кабелів — назва групи отримує префікс кабеля " +
                            "(«02578 OBgPo · Модуль 3 — зелений»). У файлі summary_fibers.csv колонка «Модуль» " +
                            "з'являється лише коли модулі визначені.",
                            "When several cables are loaded, the group name gets a cable prefix " +
                            "(“02578 OBgPo · Module 3 — green”). In summary_fibers.csv the “Module” column appears " +
                            "only when modules are defined.",
                            "При загрузке нескольких кабелей название группы получает префикс кабеля " +
                            "(«02578 OBgPo · Модуль 3 — зелёный»). В файле summary_fibers.csv колонка «Модуль» " +
                            "появляется только когда модули определены."),
        ["help_set_head"] = new("Налаштування (пороги)", "Settings (thresholds)", "Настройки (пороги)"),
        ["help_set1"] = new("Пороги збігаються з otdr_config.txt консольної версії (has_buffer_spool, " +
                            "buffer_min_m/max_m, atten_1310_max/1550_max, splice_loss_max, check_qc_connector, " +
                            "qc_connector_loss_max, match_tolerance_m) плюс GUI-параметри: «Жовта зона» (допуск % " +
                            "від порога затухання), «Показувати стовпці Втрати», «Режим порівняння» (курсори А/В " +
                            "зберігаються при перемиканні волокна/файла — показання дБ перераховуються для нового " +
                            "волокна, зручно порівнювати ту саму точку траси в різних файлах) і «Мова інтерфейсу». " +
                            "Числа приймаються і з крапкою, і з комою. Після збереження всі завантажені файли " +
                            "перераховуються автоматично.",
                            "Thresholds match the console version's otdr_config.txt (has_buffer_spool, buffer_min_m/max_m, " +
                            "atten_1310_max/1550_max, splice_loss_max, check_qc_connector, qc_connector_loss_max, " +
                            "match_tolerance_m) plus GUI options: “Yellow zone” (% tolerance from the attenuation " +
                            "threshold), “Show Loss columns”, “Compare mode” (A/B cursors are kept when switching " +
                            "fiber/file — dB readings are recomputed for the new fiber, convenient for comparing the " +
                            "same trace point across files) and “Interface language”. Numbers are accepted with a dot " +
                            "or a comma. After saving, all loaded files are recomputed automatically.",
                            "Пороги совпадают с otdr_config.txt консольной версии (has_buffer_spool, buffer_min_m/max_m, " +
                            "atten_1310_max/1550_max, splice_loss_max, check_qc_connector, qc_connector_loss_max, " +
                            "match_tolerance_m) плюс GUI-параметры: «Жёлтая зона» (допуск % от порога затухания), " +
                            "«Показывать столбцы Потери», «Режим сравнения» (курсоры А/В сохраняются при переключении " +
                            "волокна/файла — показания дБ пересчитываются для нового волокна, удобно сравнивать одну и " +
                            "ту же точку трассы в разных файлах) и «Мова интерфейса». Числа принимаются и с точкой, и " +
                            "с запятой. После сохранения все загруженные файлы пересчитываются автоматически."),
        ["help_set2"] = new("ВАЖЛИВО (як у консолі): вимір БЕЗ буферної котушки вимагає has_buffer_spool = False, " +
                            "інакше волокно 800..1200 м буде прийнято за буфер і виключене з перевірки.",
                            "IMPORTANT (as in the console version): a measurement WITHOUT a buffer spool requires " +
                            "has_buffer_spool = False, otherwise an 800..1200 m fiber will be taken for the buffer and " +
                            "excluded from the check.",
                            "ВАЖНО (как в консоли): замер БЕЗ буферной катушки требует has_buffer_spool = False, " +
                            "иначе волокно 800..1200 м будет принято за буфер и исключено из проверки."),
        ["help_print_head"] = new("Друк", "Printing", "Печать"),
        ["help_print1"] = new("Кнопка «Друк» спершу питає, що включити у звіт: карту (поточний графік траси) та " +
                              "таблиці «Волокна», «Ділянки», «Події». Звіт A4 містить заголовок, дату й область " +
                              "(поточний файл або всі файли); таблиці друкуються в тому вигляді, як вони видно " +
                              "зараз — з урахуванням фільтра за файлом, пошуку та сортування колонок. Видимість " +
                              "колонок (наприклад «Втрати») враховується. Якщо в системі є принтер — відкривається " +
                              "системний діалог друку; якщо принтерів немає — звіт зберігається у PDF через системний " +
                              "Microsoft Edge або Chrome (без вікна браузера; альбомна орієнтація, справжній текст — " +
                              "його можна виділяти й шукати, вердикти забарвлені). Якщо браузер не знайдено, " +
                              "використовується вбудований генератор PDF (текст кривими, без карти).",
                              "The “Print” button first asks what to include in the report: the map (current trace plot) " +
                              "and the Fibers/Sections/Events tables. The A4 report contains the title, date and scope " +
                              "(current file or all files); tables print exactly as currently visible — with the file " +
                              "filter, search and column sorting. Column visibility (e.g. “Loss”) is respected. If the " +
                              "system has a printer, the system print dialog opens; if there are no printers, the report " +
                              "is saved as PDF via the system Microsoft Edge or Chrome (headless; landscape orientation, " +
                              "real selectable/searchable text, verdicts colored). If no browser is found, a built-in " +
                              "PDF generator is used (text as curves, no map).",
                              "Кнопка «Печать» сначала спрашивает, что включить в отчёт: карту (текущий график трассы) " +
                              "и таблицы «Волокна», «Участки», «События». Отчёт A4 содержит заголовок, дату и область " +
                              "(текущий файл или все файлы); таблицы печатаются в том виде, как они видны сейчас — с " +
                              "учётом фильтра по файлу, поиска и сортировки колонок. Видимость колонок (например " +
                              "«Потери») учитывается. Если в системе есть принтер — открывается системный диалог печати; " +
                              "если принтеров нет — отчёт сохраняется в PDF через системный Microsoft Edge или Chrome " +
                              "(без окна браузера; альбомная ориентация, настоящий текст — его можно выделять и искать, " +
                              "вердикты окрашены). Если браузер не найден, используется встроенный генератор PDF (текст " +
                              "кривыми, без карты)."),
        ["help_csv_head"] = new("Експорт CSV", "CSV export", "Экспорт CSV"),
        ["help_csv1"] = new("«Експорт CSV» записує три файли (summary_fibers.csv, sections_detail.csv, " +
                            "events_detail.csv) у вибрану теку у форматі консольної версії: роздільник «;», " +
                            "десяткова кома, UTF-8 з BOM.",
                            "“Export CSV” writes the three files (summary_fibers.csv, sections_detail.csv, " +
                            "events_detail.csv) into the chosen folder in the console format: “;” separator, decimal " +
                            "comma, UTF-8 with BOM.",
                            "«Экспорт CSV» пишет три файла (summary_fibers.csv, sections_detail.csv, events_detail.csv) " +
                            "в выбранную папку в формате консольной версии: разделитель «;», десятичная запятая, UTF-8 с BOM."),
        ["help_theme_head"] = new("Тема оформлення", "Theme", "Тема оформления"),
        ["help_theme1"] = new("Кнопка «Тема» перемик палітри додатка по колу: сіра (типова, приглушена) → темна → " +
                              "світла. Системні діалоги (вибір файла/теки) підпорядковуються темі Windows і " +
                              "не перекрашуються.",
                              "The “Theme” button cycles the app palettes: gray (default, muted) → dark → light. " +
                              "System dialogs (file/folder pickers) follow the Windows theme and are not recolored.",
                              "Кнопка «Тема» переключает палитры приложения по кругу: серая (по умолчанию, " +
                              "приглушённая) → тёмная → светлая. Системные диалоги (выбор файла/папки) подчиняются " +
                              "теме Windows и не перекрашиваются."),
    };
}
