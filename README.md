# Dynamic Island PC (WPF)

Динамический «остров» для Windows в стиле iPhone Dynamic Island — живёт сверху экрана,
раскрывается по наведению курсора и показывает часы, раскладку, Caps Lock, громкость,
смену аудиоустройств, полку файлов и стек буфера обмена.

Написан на **C# / .NET 8 / WPF**. Рендер через DirectX — плавные анимации и гладкие
скруглённые углы без пикселизации (в отличие от WinForms-версии).

![Скриншот](docs/screenshot.png)
> _Добавь скриншот в папку `docs/` — пока placeholder._

## Возможности

| Фича | Описание |
|---|---|
| 🕐 Часы | Локальное время `HH:mm`, обновление раз в секунду |
| ⌨️ Раскладка | RU / EN индикатор + подсветка Caps Lock |
| 🔊 Громкость | OSD при изменении системных клавиш + слайдер в панели |
| 🔌 Аудиоустройства | Переключение вывода **через COM `IPolicyConfig`** — без прав администратора |
| 📎 Полка | Drag&Drop файлов на остров; открыть / папка / копировать / удалить |
| 📋 Буфер обмена | История текста, изображений и HEX-цветов с превью; клик — восстановить |
| ✨ Hover-анимация | Раскрытие при наведении, сворачивание при уходе курсора, spring-easing |
| 🌑 Тень и углы | `DropShadowEffect` + нативные скругления Windows 11 через DWM |

## Требования

- Windows 10 22H2 / Windows 11
- .NET 8 SDK
- Visual Studio 2022 (или Rider)

## Сборка и запуск

```bash
# из корня репозитория
dotnet restore
dotnet build -c Release
dotnet run --project DynamicIslandPC.Wpf -c Release
```

Или просто открой `.sln` в Visual Studio → **F5**.

### Зависимости

- **NAudio** (NuGet) — CoreAudio API для громкости и устройств.
- **SoundVolumeView.exe** (NirSoft, лежит в `tools/`) — fallback для смены устройства,
  если COM-путь недоступен. Источник: https://www.nirsoft.net/utils/sound_volume_view.html
  (freeware). В обычной работе не требуется — COM переключает устройства сам.

## Структура проекта

```
DynamicIslandPC.Wpf/
├─ App.xaml / App.xaml.cs        # точка входа
├─ MainWindow.xaml / .cs         # остров: XAML-разметка + логика UI
├─ Models/
│  └─ ClipboardItem.cs           # модель элемента буфера
├─ Services/
│  ├─ AudioService.cs            # NAudio + COM IPolicyConfig
│  ├─ ClipboardService.cs        # мониторинг и история буфера
│  ├─ LayoutService.cs           # раскладка + Caps Lock (Win32)
│  └─ ShelfService.cs            # полка файлов
└─ Helpers/
   └─ NativeMethods.cs           # P/Invoke: user32, dwmapi
```

## Как это работает (кратко)

- **Hover** — `DispatcherTimer` каждые 80 мс сравнивает позицию курсора (`GetCursorPos`)
  с физическими границами окна (`GetWindowRect`). Сравнение в физических пикселях, поэтому
  корректно работает при любом DPI-масштабе (125%, 150%, …).
- **Анимация** — `DoubleAnimation` + `CubicEase(EaseOut)` на `Width/Height/Left`. Флаг
  `_isAnimating` блокирует повторные триггеры, чтобы остров не «дёргался».
- **Буфер** — `AddClipboardFormatListener` + перехват `WM_CLIPBOARDUPDATE` через `HwndSource`.
  Мгновенно, без polling.
- **Смена устройства** — COM `IPolicyConfig::SetDefaultEndpoint`. Работает без UAC.
  `SoundVolumeView` остаётся как запасной путь.

## Roadmap

- [ ] Иконка в системном трее + автостарт с Windows
- [ ] Конфиг JSON (позиция, вкл/выкл фич, горячие клавиши)
- [ ] Single-instance защита
- [ ] P1: Bluetooth-статус и заряд наушников
- [ ] P1: индикатор сети / потеря интернета
- [ ] P1: быстрая заметка по хоткею
- [ ] P1: режим «Не беспокоить»
- [ ] Drag-out файлов из полки (боремся с UIPI/TopMost)

## Лицензия

MIT (добавь `LICENSE`, если хочешь открытую лицензию).
`SoundVolumeView.exe` — отдельная freeware-лицензия NirSoft.