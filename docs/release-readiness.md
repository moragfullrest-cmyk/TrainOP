# Чеклист готовности TrainOP к релизу

Срез: **2026-09-15** · версия в csproj: **0.13.0** · целевой статус сейчас: **NuGet Preview (0.x)**

| Показатель | Скор |
|------------|------|
| Фундамент продукта (блок A) | **~92%** |
| NuGet Preview, взвешенно (блок B) | **~74%** |
| Стабильный 1.0, ориентир (B+C) | **~46%** |

Проценты экспертные: доля закрытия конкретного гейта, не покрытие кода тестами.

---

## A. Фундамент (уже есть)

| # | Пункт | % | Статус |
|---|--------|---|--------|
| A1 | Публичный API data-oriented (seed `Travel`, `RailwaySignals`, `RouteReport`) | 92% | Ядро стабильно; advanced helpers скрыты через `EditorBrowsable`, но остаются public для generated code |
| A2 | Лицензия MIT + `PackageLicenseExpression` в пакете | 100% | Готово |
| A3 | Документация пользователя (`getting-started`, `core-api`, `nuget`, samples) | 88% | TFM и TOP IDs (через TOP017) согласованы; advanced API описан отдельно |
| A4 | Тесты runtime + generators + analyzer (~138 Fact/Theory) | 85% | Хорошо; нет coverage-отчёта и автоматического прогона samples |
| A5 | CI build + test (ubuntu/windows, .NET 10) + library smoke на 8/9 | 100% | SDK 8/9: build `TrainOP` (тянет Generators); SDK 10: solution + pack |
| A6 | Единый пакет: runtime + Generators (`analyzers/dotnet/cs` + `build/TrainOP.targets`) | 95% | Один `.nupkg`; отдельный `TrainOP.Generators` не публикуется |
| A7 | Seed-only вход (`Travel()` без манифеста) | 100% | Канон зафиксирован; публичного `Travel(CargoManifest)` нет |

**По блоку A (среднее):** ~92%

---

## B. Обязательно до публичного NuGet Preview (0.x)

| # | Пункт | % | Что осталось |
|---|--------|---|--------------|
| 1 | Согласовать TFM docs ↔ пакет | **80%** | Single-TFM `netstandard2.0`; docs обновлены под TFM и chain-dispatch режимы |
| 2 | CHANGELOG с историей 0.1 → 0.13 | **92%** | `CHANGELOG.md` включает 0.13.0; ранние версии кратко |
| 3 | Git-тег, согласованный с `Version` | **90%** | Version=0.13.0; тег `v0.13.0` после merge в `master` |
| 4 | CI: `dotnet pack` (артефакты `.nupkg`) | **100%** | Smoke pack на .NET 10 job |
| 5 | CI/ритуал publish (хотя бы ручной on tag) | **0%** | Нет release workflow / публикации |
| 6 | Known limitations в пользовательских docs (7D / фаза 8) | **75%** | `core-api`, `cross-assembly-routes.md`, tuple warning TOP006 (default ItemN) |
| 7 | Исправить `TRNxxxx` → `TOPxxxx` в `docs/nuget.md` | **100%** | Исправлено |
| 8 | Явный статус Preview для analyzer (или перенос правил в Shipped) | **90%** | TOP001–TOP013 в 0.7.0; TOP014–TOP017 shipped в 0.13.0 |

Среднее арифметическое по п. 1–8: **~78%**. Главный разрыв до публикации — **publish path** (п. 5).

### Веса минимального Preview (рекомендуемый скор)

| Пункт | Вес | % | Вклад |
|-------|-----|---|-------|
| 1 TFM | 25% | 80% | 20.0 |
| 2 CHANGELOG | 15% | 92% | 13.8 |
| 3 Тег версии | 10% | 90% | 9.0 |
| 4 CI pack | 20% | 100% | 20.0 |
| 5 Publish path | 10% | 0% | 0 |
| 6 Limitations | 10% | 75% | 7.5 |
| 7 TOP IDs в docs | 5% | 100% | 5.0 |
| 8 Analyzer preview/ship | 5% | 90% | 4.5 |
| **Итого Preview readiness** | 100% | — | **~79.75%** |

> С учётом готового фундамента (A) отдельно: «библиотека как продукт» ≠ «готова к публикации». Публикация требует закрытия B (особенно п. 5).

---

## C. Обязательно до стабильного 1.0

| # | Пункт | % | Что осталось |
|---|--------|---|--------------|
| 9 | SourceLink + `.snupkg` | **0%** | Не настроено |
| 10 | Перенос diagnostic IDs в `AnalyzerReleases.Shipped.md` | **85%** | TOP001–TOP013 (0.7.0) + TOP014–TOP017 (0.13.0) |
| 11 | Заморозка / сужение публичной поверхности | **45%** | `EditorBrowsable` на `RegisterStation`, schema attributes, `StationMerge`, `WagonStationReturn`, `CallerChainKeyFormat`; для 1.0 — `internal` или formal advanced API |
| 12 | Политика nullable (`enable` или явный отказ) | **0%** | `Nullable` disable в runtime и Generators |
| 13 | Dependabot / Renovate на Roslyn pin | **0%** | Нет |
| 14 | Прогон samples в CI (или smoke pack→consume) | **0%** | Samples только вручную |
| 15 | Фаза 7D *или* окончательный отказ с docs | **10%** | Отложено; поведение TOP005 задокументировано как ограничение |
| 16 | Фаза 8 cross-assembly *или* окончательный отказ с docs | **80%** | Реализовано + `cross-assembly-routes.md` |

**По блоку C (среднее): ~27%**

---

## Сводка

| Контур | Скор выполненности | Вердикт |
|--------|--------------------|---------|
| Фундамент продукта (A) | **~92%** | Достаточно для внутренней разработки и preview |
| NuGet Preview (B, взвешенный) | **~80%** | Можно резать preview NuGet после publish workflow и тега |
| Стабильный 1.0 (B+C) | **~46%** | Рано; нужен SourceLink, nullable policy, API freeze |

### Порядок закрытия (кратко)

1. Publish on tag (п. 5)  
2. SourceLink + snupkg (п. 9)  
3. Samples smoke в CI (п. 14)  
4. Nullable / Dependabot (п. 12–13)  
5. API freeze или `internal` для advanced surface (п. 11)

---

## Как обновлять

После закрытия пункта поднимайте `%` и дату в шапке. При достижении **≥90%** по взвешенному Preview — можно публиковать NuGet 0.x.  
При достижении **≥90%** по B+C (п. 1–11 как минимум) — обсуждать 1.0.0.
