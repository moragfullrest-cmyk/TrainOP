# Установка через NuGet

TrainOP поставляется **одним пакетом**:

| Пакет | Назначение |
|-------|------------|
| **TrainOP** | Runtime (`TrainRoute`, `CargoManifest`, сигналы) + source generator / analyzer для data-oriented `.Station(...)` |

Генератор лежит в `analyzers/dotnet/cs` внутри того же `.nupkg` и подключается автоматически.

## Требования

- Пакет **TrainOP**: `netstandard2.0` (single-TFM)
- **SDK-style** `.csproj` с поддержкой source generators

### Chain-dispatch

При конфликте имён вагонов у handler'ов с одной сигнатурой типов генератор использует **caller dispatch** (ctor+ordinal): идентичность цепочки штампуется на `new TrainRoute()`, binding resolve идёт по `CallerChainKey` + порядковому индексу. Дополнительных MSBuild-свойств не требуется.

Сравнение скорости: [`benchmarks/README.md`](../benchmarks/README.md).

## Установка из nuget.org

### CLI

```bash
dotnet add package TrainOP
```

Указать версию явно:

```bash
dotnet add package TrainOP --version 0.13.0
```

### PackageReference в `.csproj`

```xml
<ItemGroup>
  <PackageReference Include="TrainOP" Version="0.13.0" />
</ItemGroup>
```

Дополнительных атрибутов (`OutputItemType`, `ReferenceOutputAssembly`) для NuGet **не требуется** — генератор подключается автоматически из папки `analyzers/dotnet/cs` внутри пакета.

### Visual Studio

**Tools → NuGet Package Manager → Manage NuGet Packages for Solution** — найдите `TrainOP` и установите в нужные проекты.

## Локальный feed (до публикации или для отладки)

Если пакет ещё не на nuget.org или вы собираете его из исходников:

```bash
dotnet pack src/TrainOP/TrainOP.csproj -c Release
```

Артефакт: `src/TrainOP/bin/Release/TrainOP.*.nupkg` (внутри — runtime + analyzer).

### Одноразовый источник при установке

```bash
dotnet add package TrainOP --source C:\path\to\TrainOP\src\TrainOP\bin\Release
```

### Постоянный локальный feed

```bash
dotnet nuget add source C:\path\to\local-nuget-feed --name trainop-local
```

Скопируйте `.nupkg` в эту папку, затем:

```bash
dotnet add package TrainOP --source trainop-local
```

## Проверка подключения

После `dotnet restore` и сборки проекта:

1. В **Solution Explorer** (Visual Studio) или в дереве зависимостей виден пакет **TrainOP**.
2. Data-oriented `.Station(...)` компилируется без ошибок «метод не найден».
3. Data-oriented `.Station(...)` компилируется; терминальные вагоны — `report.Get<T>("name")` / `report["name"]`.

Минимальный потребительский проект:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TrainOP" Version="0.13.0" />
  </ItemGroup>
</Project>
```

```csharp
using TrainOP;

var route = new TrainRoute()
    .Station("Seed", () => new { id = 1 })
    .Station("Next", (int id) => new { id = id + 1 });

var report = route.Travel();
var id = report.Get<int>("id");
```

## NuGet vs ProjectReference

| | NuGet | ProjectReference (разработка в монорепо) |
|---|-------|----------------------------------------|
| Сценарий | Внешние приложения, CI без клонирования TrainOP | Разработка TrainOP, примеры в `samples/` |
| Генератор | Внутри пакета `TrainOP` (`analyzers/dotnet/cs`) | Явная ссылка на `TrainOP.Generators` с `OutputItemType="Analyzer"` |
| Версионирование | SemVer пакета | Текущий коммит исходников |

Подключение из исходников — в [Начало работы → разработка в решении](getting-started.md#разработка-в-решении-projectreference).

## Частые проблемы

### Генератор не срабатывает

- Убедитесь, что установлен пакет **TrainOP** (в нём же analyzer) и выполнен `dotnet restore`.
- Пересоберите проект (`dotnet build`); IDE иногда кэширует анализаторы — перезапуск может понадобиться.
- Проверьте, что используется SDK-style проект, а не старый `packages.config` без analyzers.

### Ошибки анализатора цепочки (TOPxxxx)

Генератор проверяет совместимость параметров и возвратов между станциями. Сообщения указывают на handler, tuple literal или станцию в цепочке — исправьте сигнатуру handler'а или тип возврата. Полный список — [Основной API → диагностики](core-api.md#диагностики-analyzer).

## Маршруты в библиотеке и композиция в приложении

Class library с `public static TrainRoute Build()` автоматически экспортирует terminal schema при сборке с TrainOP (generator в пакете). Consumer добавляет `.Station(...)` после `ExternalModule.Build()` — analyzer проверяет стык по exported schema.

Подробнее: [cross-assembly-routes.md](cross-assembly-routes.md).

## Следующие шаги

- [Начало работы](getting-started.md) — первый маршрут и типичный поток
- [Основной API](core-api.md) — сигналы, async, ServiceStation
