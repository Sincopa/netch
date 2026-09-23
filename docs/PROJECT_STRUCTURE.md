# Структура проекта

Исходники физически сгруппированы по назначению. Откройте `Netch.sln` в Visual Studio
или `Netch.code-workspace` в VS Code.

| Каталог | Назначение |
| --- | --- |
| `src/Netch` | C# приложение, сетевое ядро и Windows-хост |
| `src/Netch.WebUI` | Самостоятельный Svelte/TypeScript интерфейс |
| `src/native/Redirector` | Нативная переадресация трафика приложений |
| `src/native/RouteHelper` | Нативная работа с маршрутами |
| `tests/Netch.Tests` | .NET тесты |
| `tests/RedirectorTester` | Отладочная программа Redirector |
| `vendor` | Исходники и сборка внешних proxy/DNS-ядер |
| `assets/runtime` | Режимы, переводы и ресурсы поставки |
| `assets/branding` | Предоставленные SVG/PNG логотипы |
| `scripts` | Общие настройки сборки и служебные скрипты |
| `docs` | Документация и решения по архитектуре |
| `artifacts` | Готовые приложения и промежуточные публикации; вне Git |
| `.cache` | Локальные SDK, NuGet и временные проверки; вне Git |

`release-local` оставлен как прежняя пользовательская копия приложения: в нём могут
находиться настройки и логи. Новые сборки по умолчанию помещаются в `artifacts/Netch`.
`bin`, `obj`, `node_modules` и `dist` генерируются инструментами и не входят в исходники.

## Границы ответственности

`Svelte → typed IPC → Application → сетевое ядро`.

Внутри `src/Netch` слой `Application` содержит операции и DTO. `Desktop` обслуживает
WebView2, IPC, окно и трей. `Controllers`, `Servers`, `Services` и `Interops` содержат
сетевую реализацию. `Models` и `Utils` — конфигурацию, модели и общие функции.
Интерфейс обращается к прикладным операциям через IPC, а не напрямую к Windows API.

Внешние ядра остаются в `vendor`, поскольку у них собственные системы сборки и лицензии.
Нативные компоненты нужны для работы маршрутизации и не являются дубликатами C# проекта.
Регрессионные тесты Svelte находятся рядом с компонентами в `src/Netch.WebUI/src`.

## Основные команды

```powershell
# Полная сборка чистого пакета (требуются .NET 8, Node.js 22 и C++ build tools)
.\build.ps1 -Configuration Release

# Быстрая сборка приложения с уже собранными нативными ядрами
.\build.ps1 -Configuration Release -SkipNativeBuild

# Сборка поверх существующей папки с сохранением настроек пользователя
.\build.ps1 -Configuration Release -PreserveUserData

# Проверки
dotnet test tests/Netch.Tests/Tests.csproj -c Release -p:Platform=x64
npm --prefix src/Netch.WebUI run check
npm --prefix src/Netch.WebUI test

# Перегенерация иконок из assets/branding
.\scripts\update-icons.ps1
```

Пути обновлены в solution, project references, скриптах сборки и CI.
По умолчанию `build.ps1` собирает чистый пакет без `data`, логов и `mode/Custom`.
Личные настройки держите в `release-local`. Чтобы при сборке поверх существующей
папки сохранить `data`, `logging` и `mode/Custom`, передайте `-PreserveUserData`.
