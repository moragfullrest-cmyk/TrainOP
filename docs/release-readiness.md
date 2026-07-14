# Чеклист готовности TrainOP к релизу

Срез: **2026-07-14** · версия в csproj: **0.5.0** · целевой статус сейчас: **NuGet Preview (0.x)**

| Показатель | Скор |
|------------|------|
| Фундамент продукта (блок A) | **~90%** |
| NuGet Preview, взвешенно (блок B) | **~11%** |
| Стабильный 1.0, ориентир (B+C) | **~10–15%** |

Проценты экспертные: доля закрытия конкретного гейта, не покрытие кода тестами.

---

## A. Фундамент (уже есть)

| # | Пункт | % | Статус |
|---|--------|---|--------|
| A1 | Публичный API data-oriented (seed `Travel`, `RailwaySignals`, `RouteReport`) | 90% | Есть; публичная поверхность шире ментальной модели (`StationMerge`, `WagonStationReturn`, `RegisterStation`) |
| A2 | Лицензия MIT + `PackageLicenseExpression` в пакетах | 100% | Готово |
| A3 | Документация пользователя (`getting-started`, `core-api`, `nuget`, samples) | 75% | Сильная база; ложный TFM и опечатка diagnostic ID |
| A4 | Тесты runtime + generators + analyzer (~138 Fact/Theory) | 85% | Хорошо; нет coverage-отчёта и автоматического прогона samples |
| A5 | CI build + test (ubuntu/windows, .NET 10) | 100% | Готово для compile/test |
| A6 | Упаковка Generators (`analyzers/dotnet/cs` + `.targets` / InterceptorsNamespaces) | 80% | Схема верная; нет PackageReadme у Generators |
| A7 | Seed-only вход (`Travel()` без манифеста) | 100% | Канон зафиксирован; публичного `Travel(CargoManifest)` нет |

**По блоку A (среднее):** ~90%

---

## B. Обязательно до публичного NuGet Preview (0.x)

| # | Пункт | % | Что осталось |
|---|--------|---|--------------|
| 1 | Согласовать TFM docs ↔ пакет | **10%** | Пакет `net10.0`; docs всё ещё пишут netstandard2.0 / пример net8.0. Нужно: multi-target **или** полностью переписать docs под .NET 10 |
| 2 | CHANGELOG с историей 0.1 → 0.3 | **0%** | Файла нет |
| 3 | Git-тег, согласованный с `Version` (например `v0.5.0`) | **25%** | Version=0.5.0 в csproj; теги только `v0.1.0` / `v0.1.1` |
| 4 | CI: `dotnet pack` (артефакты `.nupkg`) | **0%** | В workflow только restore/build/test |
| 5 | CI/ритуал publish (хотя бы ручной on tag) | **0%** | Нет release workflow / публикации |
| 6 | Known limitations в пользовательских docs (7D / фаза 8) | **50%** | `core-api` уже говорит про `Build().Station`; нет явного «limitations» и нет `cross-assembly-routes.md` |
| 7 | Исправить `TRNxxxx` → `TOPxxxx` в `docs/nuget.md` | **0%** | Заголовок troubleshooting всё ещё `TRNxxxx` |
| 8 | Явный статус Preview для analyzer (или перенос правил в Shipped) | **15%** | Правила работают; `AnalyzerReleases.Shipped.md` пуст («No rules shipped yet») |

Среднее арифметическое по п. 1–8: **~13%**. Главный разрыв до публикации — блок B.

### Веса минимального Preview (рекомендуемый скор)

| Пункт | Вес | % | Вклад |
|-------|-----|---|-------|
| 1 TFM | 25% | 10% | 2.5 |
| 2 CHANGELOG | 15% | 0% | 0 |
| 3 Тег версии | 10% | 25% | 2.5 |
| 4 CI pack | 20% | 0% | 0 |
| 5 Publish path | 10% | 0% | 0 |
| 6 Limitations | 10% | 50% | 5 |
| 7 TOP IDs в docs | 5% | 0% | 0 |
| 8 Analyzer preview/ship | 5% | 15% | 0.75 |
| **Итого Preview readiness** | 100% | — | **~11%** |

> С учётом готового фундамента (A) отдельно: «библиотека как продукт» ≠ «готова к публикации». Публикация требует закрытия B.

---

## C. Обязательно до стабильного 1.0

| # | Пункт | % | Что осталось |
|---|--------|---|--------------|
| 9 | SourceLink + `.snupkg` | **0%** | Не настроено |
| 10 | Перенос diagnostic IDs в `AnalyzerReleases.Shipped.md` | **0%** | Все TOP* в Unshipped |
| 11 | Заморозка / сужение публичной поверхности | **20%** | `EditorBrowsable` на `RegisterStation` есть; `StationMerge` / `WagonStationReturn` остаются публичными без политики 1.0 |
| 12 | Политика nullable (`enable` или явный отказ) | **0%** | `Nullable` disable в обоих пакетах |
| 13 | Dependabot / Renovate на Roslyn pin | **0%** | Нет |
| 14 | Прогон samples в CI (или smoke pack→consume) | **0%** | Samples только вручную |
| 15 | Фаза 7D *или* окончательный отказ с docs | **10%** | Отложено; поведение TOP005 задокументировано как ограничение |
| 16 | Фаза 8 cross-assembly *или* окончательный отказ с docs | **5%** | План есть; spike/док `cross-assembly-routes.md` нет |

**По блоку C (среднее): ~4%**

---

## Сводка

| Контур | Скор выполненности | Вердикт |
|--------|--------------------|---------|
| Фундамент продукта (A) | **~90%** | Достаточно для внутренней разработки |
| NuGet Preview (B, взвешенный) | **~11%** | Не публиковать, пока не закрыты TFM + CHANGELOG + pack |
| Стабильный 1.0 (B+C) | **~10–15%** | Рано; нужен shipped analyzer contract и заморозка API |

### Порядок закрытия (кратко)

1. TFM (п.1)  
2. CHANGELOG + тег (п.2–3)  
3. CI pack → publish path (п.4–5)  
4. Docs: limitations + TOP IDs + Preview label (п.6–8)  
5. Затем 1.0-гейт: SourceLink, Shipped rules, API freeze (п.9–11)

---

## Как обновлять

После закрытия пункта поднимайте `%` и дату в шапке. При достижении **≥90%** по взвешенному Preview — можно резать NuGet 0.x.  
При достижении **≥90%** по B+C (п.1–11 как минимум) — обсуждать 1.0.0.
