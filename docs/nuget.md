# Установка через NuGet

TrainOP поставляется двумя пакетами:

| Пакет | Назначение |
|-------|------------|
| **TrainOP** | Основная библиотека: `TrainRoute`, `CargoManifest`, сигналы, runtime |
| **TrainOP.Generators** | Source generator и analyzer для data-oriented `.Station(...)` |

Оба пакета нужны для data-oriented API (`.Station` handlers).

## Требования

- Проект на **.NET Standard 2.0** или выше (например `net8.0`, `net9.0`)
- **SDK-style** `.csproj` (C# с поддержкой source generators; обычно .NET SDK 6+)
- Версии пакетов **TrainOP** и **TrainOP.Generators** должны совпадать

## Установка из nuget.org

### CLI

```bash
dotnet add package TrainOP
dotnet add package TrainOP.Generators
```

Указать версию явно:

```bash
dotnet add package TrainOP --version 0.3.0
dotnet add package TrainOP.Generators --version 0.3.0
```

### PackageReference в `.csproj`

```xml
<ItemGroup>
  <PackageReference Include="TrainOP" Version="0.3.0" />
  <PackageReference Include="TrainOP.Generators" Version="0.3.0" />
</ItemGroup>
```

Дополнительных атрибутов (`OutputItemType`, `ReferenceOutputAssembly`) для NuGet **не требуется** — генератор подключается автоматически из папки `analyzers/dotnet/cs` внутри пакета.

### Visual Studio

**Tools → NuGet Package Manager → Manage NuGet Packages for Solution** — найдите `TrainOP` и `TrainOP.Generators`, установите оба в нужные проекты.

## Локальный feed (до публикации или для отладки)

Если пакеты ещё не на nuget.org или вы собираете их из исходников:

```bash
dotnet pack src/TrainOP/TrainOP.csproj -c Release
dotnet pack src/TrainOP.Generators/TrainOP.Generators.csproj -c Release
```

Артефакты: `src/TrainOP/bin/Release/TrainOP.*.nupkg` и `src/TrainOP.Generators/bin/Release/TrainOP.Generators.*.nupkg`.

### Одноразовый источник при установке

```bash
dotnet add package TrainOP --source C:\path\to\TrainOP\src\TrainOP\bin\Release
dotnet add package TrainOP.Generators --source C:\path\to\TrainOP\src\TrainOP.Generators\bin\Release
```

### Постоянный локальный feed

```bash
dotnet nuget add source C:\path\to\local-nuget-feed --name trainop-local
```

Скопируйте `.nupkg` в эту папку, затем:

```bash
dotnet add package TrainOP --source trainop-local
dotnet add package TrainOP.Generators --source trainop-local
```

## Проверка подключения

После `dotnet restore` и сборки проекта:

1. В **Solution Explorer** (Visual Studio) или в дереве зависимостей должны быть оба пакета.
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
    <PackageReference Include="TrainOP" Version="0.3.0" />
    <PackageReference Include="TrainOP.Generators" Version="0.3.0" />
  </ItemGroup>
</Project>
```

```csharp
using TrainOP;

var route = new TrainRoute()
    .Station("Seed", () => new { id = 1 })
    .Station("Next", (int id) => new { id = id + 1 });

var report = route.DispatchTrain().Travel();
var id = report.Get<int>("id");
```

## NuGet vs ProjectReference

| | NuGet | ProjectReference (разработка в монорепо) |
|---|-------|----------------------------------------|
| Сценарий | Внешние приложения, CI без клонирования TrainOP | Разработка TrainOP, примеры в `samples/` |
| Генератор | Через пакет `TrainOP.Generators` | Явная ссылка с `OutputItemType="Analyzer"` |
| Версионирование | SemVer пакетов | Текущий коммит исходников |

Подключение из исходников — в [Начало работы → разработка в решении](getting-started.md#разработка-в-решении-projectreference).

## Частые проблемы

### Генератор не срабатывает

- Убедитесь, что установлен **TrainOP.Generators**, а не только TrainOP.
- Пересоберите проект (`dotnet build`); IDE иногда кэширует анализаторы — перезапуск может понадобиться.
- Проверьте, что используется SDK-style проект, а не старый `packages.config` без analyzers.

### Ошибки анализатора цепочки (TRNxxxx)

Генератор проверяет совместимость параметров и возвратов между станциями. Сообщения указывают на конкретную станцию в цепочке — исправьте сигнатуру handler'а или тип возврата. Подробнее — [Основной API](core-api.md).

## Следующие шаги

- [Начало работы](getting-started.md) — первый маршрут и типичный поток
- [Основной API](core-api.md) — сигналы, async, ServiceStation
