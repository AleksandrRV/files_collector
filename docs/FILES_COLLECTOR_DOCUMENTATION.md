# Files Collector — итоговая документация

**Версия документации:** 2.0  
**Целевая платформа:** Windows 10/11 x64  
**Назначение:** локальное создание структурированных снимков файлов проекта для чтения человеком, LLM и агентами автоматизации.

---

## 1. Назначение приложения

Files Collector сканирует выбранное дерево каталогов, применяет сохранённые правила к файлам и создаёт два взаимосвязанных результата:

```text
outputs/YYYY.MM.dd_HH.mm.ss_PresetName.md
outputs/YYYY.MM.dd_HH.mm.ss_PresetName.manifest.json
```

Markdown предназначен для передачи в LLM и ручного просмотра. JSON manifest содержит полный машиночитаемый результат, включая исключённые файлы, режимы, причины исключения, диагностику, хеши и сведения об extractor-ах.

Приложение не запускает код выбранного проекта, не отправляет файлы в сеть и не требует установки Python, Node.js или .NET Runtime в целевой portable-сборке.

---

## 2. Структура поставки

Готовый ZIP распаковывается в папку следующего вида:

```text
files-collector/
├── FilesCollector.exe
├── docs/
│   └── FILES_COLLECTOR_DOCUMENTATION.md
├── src/
│   ├── FilesCollector.sln
│   ├── FilesCollector.App/
│   ├── FilesCollector.Core/
│   ├── FilesCollector.Infrastructure/
│   ├── FilesCollector.Extractors/
│   ├── FilesCollector.Tests/
│   └── scripts/
└── outputs/
```

`FilesCollector.exe` — единственный исполняемый файл. `outputs` предназначен только для результатов и portable-данных. В обычном режиме пользовательские настройки хранятся вне папки программы.

---

## 3. Запуск, Scan Root и интерфейс

### Быстрый старт

1. Поместите папку `files-collector` внутрь анализируемого проекта.
2. Запустите `FilesCollector.exe` двойным кликом.
3. Перетащите папку проекта прямо на окно (или нажмите `Change…` в строке корня).
4. При необходимости настройте форматы, фильтры, префикс и правила в левой панели.
5. Нажмите `Create report`.

Каталог, в котором расположен `FilesCollector.exe`, автоматически исключается из дерева, плана и отчёта. Это системное исключение нельзя переопределить правилом пользователя.

### Компоновка окна

Одно окно состоит из пяти зон:

| Зона | Содержимое |
|---|---|
| Командная панель | название приложения, badge `PORTABLE`, чип активного пресета (с точкой несохранённых изменений), поле поиска/команд, переключатель темы, меню «⋯» и кнопка `Create report`. |
| Строка корня | breadcrumbs текущего Scan Root (сегменты кликабельны — выбирают корень), кнопки `Change…`, `Refresh tree`, `Refresh inventory` и живой статус inventory. |
| Левый rail + панель настроек | разделы: Explorer (скрывает панель), Расширения, Фильтры, Префикс, Пресеты, История. Rail показывает индикаторы (активные паттерны, отключённые расширения). Панель resizable и скрывается по `Ctrl+B`. |
| Центральная область | дерево файлов: строка поиска + фильтры-чипы (Files, Folders, Local rules, Excluded, >1 MB, Binary, No ext), заголовки колонок SRC/MODE/SIZE, цветные бейджи режимов, quick-actions при наведении, контекстное меню, множественный выбор с панелью массовых действий. |
| Правый инспектор | вкладки **Details** (свойства узла, segmented-переключатель режима, цепочка разрешения правила, причина, быстрые действия), **Plan** (статистика по режимам, распределение, оценка размера и токенов, топ причин исключений, проблемные файлы, диагностика последней генерации) и **Preview** (живой предпросмотр структуры отчёта). Сворачивается по `Ctrl+J`, раскрывается на весь экран кнопкой «⤢». |

Нижняя план-полоса постоянно видима: статус, счётчики и размеры по режимам, итоговый размер с оценкой токенов, предупреждение о большом отчёте, прогресс генерации с `Cancel` и последний созданный отчёт.

### Ключевые действия и клавиши

| Действие | Как |
|---|---|
| Смена Scan Root | drag & drop папки на окно, `Change…`, клик по сегменту breadcrumbs, палитра |
| Перечитать дерево | `Refresh tree` или `F5` (при фокусе в дереве) |
| Полный обход inventory | `Refresh inventory` или `F5` (при фокусе вне дерева) |
| Поиск/команды | `Ctrl+K` — палитра: команды + поиск по всему inventory с переходом «показать в дереве» |
| Режимы для узла | quick-actions `F/S/L/E/↺` при наведении, контекстное меню, клавиши `1–4`, segmented-контрол в инспекторе |
| Быстрое исключение | `Delete` на выбранном узле (для папки — с подтверждением), тост с `Undo` |
| Паттерн из пути | контекстное меню: `Include/Exclude pattern from path` |
| Сохранить пресет | `Ctrl+S` / `Ctrl+Shift+S` |
| Открыть последний отчёт / outputs | `Ctrl+O` / `Ctrl+Shift+O`, нижняя план-полоса, раздел История |
| Темы и плотность | переключатель в шапке (Light/Dark), `Toggle density` в меню «⋯» (Comfortable/Compact) |

### Служебные механизмы

Дерево загружается лениво: содержимое папки читается при её раскрытии. Недоступные каталоги и reparse points помечаются иконкой-предупреждением; Excluded-элементы приглушены, но остаются видимыми («слепые зоны» отчёта). Reparse points не обходятся рекурсивно, если соответствующая опция не включена.

Файловый inventory создаётся отдельно в фоне и сохраняется в локальном cache. Правила, форматы, glob-паттерны и Preview пересчитывают план только по inventory в памяти, без повторного обхода диска (live-пересчёт с debounce). `Refresh inventory` запускает полный фоновый обход и обновляет cache; `Refresh tree` перечитывает отображаемый корень. `FileSystemWatcher` ускоряет обновление после изменений.

---

## 4. Режимы обработки

Каждому файлу назначается один эффективный режим.

| Режим | Результат в Markdown |
|---|---|
| `Full` | Метаданные файла и полный текст. |
| `Signatures` | Сокращённая структура при наличии поддерживаемого extractor-а. |
| `Listed` | Отдельная запись без текста с причиной пропуска. |
| `Excluded` | Не включается в Markdown index и раздел `Files`; остаётся в manifest. |

### Наследование правил

Правило каталога применяется к самому каталогу и всем потомкам на любой глубине. Точечное правило файла имеет более высокий приоритет.

Порядок разрешения:

1. системное исключение папки приложения;
2. точечное правило файла или каталога;
3. наиболее глубокое правило предкового каталога;
4. правило расширения;
5. глобальный режим `Full`.

В дереве отображаются эффективный режим и источник:

- `System`;
- `Local`;
- `Inherited`;
- `Global`.

`Reset local rule` удаляет только правило выбранного узла и возвращает наследуемое значение.

---

## 5. Пресеты

Пресет является именованной конфигурацией. Он хранит:

- правила файлов и каталогов;
- Scan Root;
- выбранный prefix preset;
- правила расширений;
- include/exclude glob-паттерны;
- настройки скрытых и системных файлов;
- лимит размера;
- режим бинарных файлов;
- настройку маскирования Scan Root;
- настройку per-file YAML metadata blocks.

Доступные команды:

| Команда | Действие |
|---|---|
| `New` | Создаёт новый пустой пресет. |
| `Save` / `Ctrl+S` | Перезаписывает активный пресет. |
| `Save as...` / `Ctrl+Shift+S` | Сохраняет текущее состояние как новый пресет. |
| `Rename` | Переименовывает пользовательский пресет. |
| `Delete` | Удаляет пользовательский пресет после подтверждения. |
| `Discard` | Восстанавливает сохранённое состояние активного пресета. |

`Default` нельзя удалить или переименовать. Несохранённые изменения отмечаются текстом `Unsaved changes`.

При переключении пресета с изменениями приложение предлагает сохранить их, отбросить или отменить переключение.

### Хранение

Обычный режим:

```text
%LOCALAPPDATA%\FilesCollector\
├── settings.json
├── presets/
├── prefixes/
└── logs/
```

Каждый пресет сохраняется отдельным JSON-файлом по GUID, а `presets/index.json` хранит отображаемое имя и порядок. Запись выполняется через временный файл и атомарную замену.

Последний активный preset ID сохраняется в `settings.json`. При следующем запуске он загружается автоматически. Если пресет отсутствует, используется `Default`.

---

## 6. Prefix presets

Префикс — независимая текстовая заготовка, не являющаяся частью текста правила обхода. Он добавляется в начало Markdown до системных metadata.

Вкладка `Prefix` позволяет создать, выбрать, сохранить, переименовать, удалить или отбросить prefix preset. `No prefix` отключает выбранный префикс для текущей конфигурации, не удаляя его файл.

Выбранный prefix preset ID сохраняется в основном preset. Поэтому при переключении preset автоматически восстанавливается связанный префикс, если он существует.

---

## 7. Форматы и фильтры

### Formats

Вкладка `Formats` показывает обнаруженные расширения, их количество, включение и режим по умолчанию.

- `Include all extensions` включён по умолчанию.
- При выключении общего режима включаются только явно разрешённые расширения.
- Точечное правило файла может включить файл даже при выключенном расширении.

### Filters

| Настройка | Действие |
|---|---|
| `Include hidden files` | Включает скрытые файлы. |
| `Include system files` | Включает системные файлы. |
| `Follow reparse points` | Разрешает обход reparse points; планировщик отслеживает посещённые каталоги для предотвращения циклов. |
| `Maximum file size, KiB` | Превышающие лимит файлы становятся `Listed`. |
| `Binary file mode` | Режим для известных бинарных расширений. |
| `Include patterns` | Разрешающие glob-паттерны. |
| `Exclude patterns` | Исключающие glob-паттерны. |
| `Redact root path in output` | Заменяет абсолютный root на `<redacted>` в Markdown и manifest. |
| `Include per-file YAML metadata blocks` | Включает/выключает YAML-блок перед каждым файлом Markdown. |
| `Inventory refresh interval, minutes` | Период фонового обновления inventory; `0` отключает автоматическое обновление. |

Поддерживаются glob-паттерны `*`, `?`, `**`.

Примеры:

```text
**/node_modules/**
**/.git/**
**/bin/**
**/obj/**
**/*.cs
```

Планировщик отображает число файлов по режимам и приблизительный размер `Full`/`Signatures`. При большой оценке отображается предупреждение.

---

## 8. Inventory cache и производительность

Inventory — это снимок метаданных файлов Scan Root: относительный путь, размер, расширение, атрибуты, доступность и статистика расширений. Содержимое файлов в cache не сохраняется.

- При открытии Scan Root приложение пытается загрузить ранее сохранённый inventory cache.
- После этого автоматически запускается фоновое обновление inventory.
- Полный обход выполняется только при смене root, нажатии `Refresh inventory`, с заданным интервалом или после debounce-сигнала `FileSystemWatcher`.
- `FileSystemWatcher` используется как ускоритель обновления после изменений, но периодический полный refresh остаётся контрольным механизмом.
- Изменение правил, preset, prefix, форматов и фильтров использует текущий снимок в памяти и не читает файловую систему заново.
- Фоновые progress notifications ограничены батчами, чтобы не перегружать WPF UI.
- Поиск дерева использует debounce 250 мс; текстовые include/exclude фильтры используют debounce 600 мс.
- При закрытии приложения активная задача inventory отменяется, а таймеры освобождаются.

Обычный cache находится в `%LOCALAPPDATA%\\FilesCollector\\cache`; portable cache находится в `outputs/app-data/cache`.

---

## 9. Search, details и command palette

Поиск над деревом фильтрует уже загруженную часть дерева по имени или относительному пути (debounce 250 мс). Фильтры-чипы:

- `Files` / `Folders` — показывать файлы и папки;
- `Local rules` — только узлы с локальным правилом;
- `Excluded` — только `Excluded`;
- `>1 MB`, `Binary`, `No ext` — фильтры по атрибутам файлов.

Палитра `Ctrl+K` — единое поле команд и поиска: команды приложения (смена корня, refresh, генерация, пресеты, темы, панели) и поиск по **всему inventory** (не только загруженной части дерева) с переходом «показать в дереве» (авто-раскрытие цепочки каталогов).

Выбранный узел выводится в правом инспекторе (вкладка Details): имя, относительный путь, размер, атрибуты, доступность, effective mode с segmented-переключателем, источник правила и **цепочка разрешения** (System → Local → Inherited → Extension → Global → Final plan decision, победитель подсвечен), причина `Listed`/`Excluded` и быстрые действия (Reset rule, Reveal in Explorer, Open, Copy path, Copy relative path).

Поиск и фильтры не изменяют правила. Переход «показать в дереве» раскрывает необходимые каталоги.

---

## 10. Plan и Preview

Вкладка **Plan** инспектора (и нижняя план-полоса) показывает будущий отчёт без чтения содержимого файлов:

- счётчики и размеры по режимам `Full` / `Signatures` / `Listed` / `Excluded`;
- итоговый размер с оценкой токенов и отметкой о соответствии контекстному окну (128k);
- stacked-бар распределения;
- предупреждение о большом отчёте (>25 МБ);
- топ причин исключений (по кодам diagnostics) и топ расширений по вкладу в размер;
- проблемные файлы (access/read/decode) с переходом «показать в дереве»;
- диагностика последней генерации: группы по кодам с файлами.

Вкладка **Preview** показывает будущую структуру Markdown живьём (обновляется при любых изменениях правил с debounce): prefix text, YAML-блок метаданных (preset, prefix preset, root, счётчики), File index — первые 50 элементов плана с режимами; строки окрашены по режиму. Тумблеры: `Show excluded`, `Show reasons`, `YAML metadata`; `Refresh` — ручное обновление; кнопка «⤢» раскрывает превью на весь экран.

---

## 11. Markdown report

Файл имеет кодировку UTF-8 без BOM и начинается маркером:

```text
<!-- files-collector-report: 1 -->
```

Основные разделы:

1. выбранный prefix;
2. `Report metadata`;
3. `File index`;
4. `Files`.

Пример сокращённого результата:

````markdown
# Files Collector report

Project review instructions.

## Report metadata

```yaml
created_at: 2026-08-24T09:30:05+07:00
root: "D:\\Project"
preset: "Backend review"
prefix_preset: "Code review"
included:
  full: 1
  signatures: 1
  listed: 1
excluded: 3
```

## File index

| # | Path | Mode | Bytes | Status |
|--:|---|---|---:|---|
| 1 | `app/Program.cs` | Full | 240 | read |
| 2 | `app/Service.cs` | Signatures | 1450 | read |

## Files

### FILE 1 — `app/Program.cs`

```yaml
path: "app/Program.cs"
mode: Full
bytes: 240
encoding: utf-8
sha256: 8b3c...
```

```csharp
public static class Program
{
}
```
````

### Per-file YAML metadata blocks

Если `Include per-file YAML metadata blocks` выключен, блоки YAML перед конкретными файлами не выводятся. Это уменьшает расход токенов. Сохраняются заголовок `FILE`, index и содержимое `Full`/`Signatures`. Полные технические сведения всегда остаются в manifest.

### Текст и бинарные файлы

- Поддерживаются UTF-8, UTF-8 BOM, UTF-16 LE/BE и fallback Windows-1251.
- Переводы строк нормализуются до `LF`.
- Фактически бинарный файл становится `Listed` с причиной `binary_file`.
- Кодовые ограждения автоматически удлиняются, если содержимое содержит обратные кавычки.

---

## 12. Manifest

Каждому Markdown соответствует JSON manifest с тем же базовым именем.

Manifest содержит:

- версию схемы;
- имя и SHA-256 Markdown;
- root либо `<redacted>`;
- preset и prefix preset;
- счётчики режимов;
- все файлы из плана, включая `Excluded`;
- итоговый режим, размер, статус, кодировку, SHA-256 и extractor;
- diagnostics.

Типичные коды diagnostics:

| Код | Значение |
|---|---|
| `access_denied` | Доступ к файлу запрещён. |
| `read_failed` | Ошибка чтения. |
| `decode_failed` | Кодировка не декодирована. |
| `binary_file` | Бинарные байты обнаружены при чтении. |
| `size_limit` | Превышен лимит размера. |
| `extension_disabled` | Формат отключён. |
| `excluded_pattern` | Совпадение с exclude glob. |
| `not_included_pattern` | Нет совпадения с include glob. |
| `hidden_file` | Скрытые файлы выключены. |
| `system_file` | Системные файлы выключены. |
| `collection_mode_excluded` | Исключён режимом правила. |
| `signature_extractor_unavailable` | Нет extractor-а для формата. |
| `signature_extraction_failed` | Extractor не смог получить структуру. |

---

## 13. Signatures extractors

Extractors не исполняют пользовательский код.

| Форматы | Extractor | Представление |
|---|---|---|
| `.cs` | `csharp-roslyn-v1` | using, namespaces, visible types, visible members, enum members без тел. |
| `.ts`, `.tsx`, `.js`, `.mjs`, `.cjs`, `.mts`, `.cts`, `.jsx` | `javascript-typescript-lexical-v1` | imports/exports и декларации. |
| `.py` | `python-lexical-v1` | imports, classes, public functions и methods. |
| `.json` | `json-structure-v1` | ключи и типы значений без строковых значений. |
| `.yaml`, `.yml` | `yaml-structure-v1` | ключи и отступы без значений. |

Для неподдерживаемых форматов `Signatures` превращается в `Listed / signature_extractor_unavailable`. Полное содержимое не подставляется как fallback.

Roslyn для C# выполняет синтаксический анализ. JavaScript/TypeScript и Python используют безопасный declaration-oriented lexical extraction и не являются полноценной семантической компиляцией языков.

---

## 14. Надёжность и безопасность

- Отчёт создаётся сначала как `.tmp`, затем атомарно переименовывается.
- При отмене или ошибке временные файлы удаляются.
- Старые `*.tmp` в `outputs` очищаются при создании report writer на старте приложения.
- Ошибка одного файла не останавливает обработку остальных файлов.
- Reparse points не обходятся по умолчанию.
- Все операции локальные, сетевых запросов и телеметрии нет.
- Отчёты могут содержать секреты и исходный код. Перед передачей внешнему сервису проверяйте активный preset и prefix.

---

## 15. Логи и диагностика запуска

Обычный режим:

```text
%LOCALAPPDATA%\FilesCollector\logs
```

Portable mode:

```text
outputs\app-data\logs
```

Основные файлы:

```text
files-collector-YYYY-MM-DD.log
files-collector-startup-YYYY-MM-DD.log
```

`files-collector-startup-...` создаётся для ошибок раннего запуска и содержит stack trace. Ротация оставляет до 14 обычных файлов журналов.

---

## 16. Portable mode

Создайте пустой файл рядом с EXE:

```text
portable.mode
```

После перезапуска application data будет храниться в:

```text
outputs/app-data/
├── settings.json
├── presets/
├── prefixes/
└── logs/
```

В диалоге `About` отображается текущий storage mode.

---

## 17. Архитектура и технологический стек

### Проекты

| Проект | Роль |
|---|---|
| `FilesCollector.App` | WPF UI, MVVM, DI composition root. |
| `FilesCollector.Core` | Модели, правила, планирование, reporting contracts, session contracts. |
| `FilesCollector.Infrastructure` | Файловая система, JSON repositories, Markdown/manifest writer, логирование, paths. |
| `FilesCollector.Extractors` | C#, JS/TS, Python, JSON/YAML signatures extractors. |
| `FilesCollector.Tests` | Unit и integration-style tests. |

### Технологии

- C# / .NET 8;
- WPF;
- CommunityToolkit.Mvvm;
- Microsoft.Extensions.DependencyInjection и Logging;
- System.Text.Json;
- Roslyn `Microsoft.CodeAnalysis.CSharp`;
- xUnit и FluentAssertions.

Ключевые архитектурные решения:

- UI отделён от Core;
- правила и preset data сериализуются как JSON;
- filesystem доступен Core через контракт `IFileSystem`;
- signatures доступны через контракт `ISignatureExtractor`;
- генерация отчёта выполняется вне UI-потока и поддерживает отмену.

---

## 18. Сборка, тесты и release

Для разработки требуется .NET 8 SDK.

```powershell
cd files-collector
dotnet restore .\src\FilesCollector.sln
dotnet build .\src\FilesCollector.sln --configuration Debug --no-restore
dotnet test .\src\FilesCollector.sln --no-build
dotnet run --no-build --project .\src\FilesCollector.App\FilesCollector.App.csproj
```

### Portable release

```powershell
.\src\scripts\publish-release-win-x64.ps1 -Version 1.0.0
```

Скрипт выполняет clean, self-contained single-file publish для `win-x64`, staging, ZIP-упаковку и создаёт SHA-256.

Результаты:

```text
src/.artifacts/release/FilesCollector-1.0.0-win-x64.zip
src/.artifacts/release/FilesCollector-1.0.0-win-x64.zip.sha256
```

### Приёмка release

Перед распространением:

1. Убедитесь, что restore, Release build и tests проходят без предупреждений.
2. Проверьте ZIP: в корне только EXE, `docs`, `src`, `outputs`.
3. Проведите smoke test на чистых Windows 10 и Windows 11 x64 без Python, Node.js и .NET Runtime.
4. Проверьте создание `.md`/`.manifest.json`, `Full`, `Signatures`, `Listed`, `Excluded`, portable mode и восстановление последнего пресета.
5. Сверьте прямые и транзитивные зависимости командой:

```powershell
dotnet list .\src\FilesCollector.sln package --include-transitive
```

6. При публичном распространении рекомендуется Authenticode-подпись EXE в защищённом CI.

---

## 19. Прямые зависимости

| Компонент | Версия |
|---|---:|
| CommunityToolkit.Mvvm | 8.3.2 |
| Microsoft.Extensions.DependencyInjection | 8.0.1 |
| Microsoft.Extensions.Logging | 8.0.1 |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 |
| Microsoft.Extensions.Logging.Abstractions | 8.0.2 |
| Microsoft.CodeAnalysis.CSharp | 4.11.0 |
| System.Text.Encoding.CodePages | 8.0.0 |
| xUnit | 2.9.2 |
| FluentAssertions | 6.12.1 |
| Microsoft.NET.Test.Sdk | 17.11.1 |

Перед публикацией лицензии необходимо проверить по фактическому транзитивному dependency graph.
