---
name: trainop-refactor
description: >-
  Improves TrainOP code quality via structure-preserving refactors: cleanup,
  dedupe, naming/folders, emit hygiene, API visibility, decompose, guards/layers,
  control-flow, micro style, plus classic code smells (Fowler/Clean Code/C# idioms).
  Use when the operator asks for рефакторинг, убери дубли/мёртвый/избыточное,
  унифицируй, вынеси/разбей, упрости, or similar quality-focused changes in
  TrainOP / TrainOP.Generators.
---

# TrainOP — рефакторинг качества кода

Структурные правки без смены семантики. Источники приёмов: хроники TrainOP, другие репо оператора, канон SE — [patterns.md](patterns.md).

**Про «знания модели»:** когитатор обучен на смеси кода разного качества, не на «только лучшем». Канон ниже — сжатые эвристики (Fowler smells, Clean Code, Effective C#), не догма. При конфликте с каноном TrainOP / правилами репо — побеждает репо.

## Типы работ

| Тип | Цель | Типичные фразы |
|-----|------|----------------|
| **A. Cleanup** | Нет мёртвых/прокси / YAGNI-абстракций | удали неиспользуемый; убери избыточную обёртку |
| **B. Dedupe** | Один источник правды | убери дубли; вынеси общий core |
| **C. Structure** | Навигация, файл = тип | разложи; унифицируй имена; data clump → тип |
| **D. Emit hygiene** | Читаемый `.g.cs` | Block(); orchestration-only generator |
| **E. Surface** | Тонкий контракт | EditorBrowsable; public→internal; один вход |
| **F. Decompose** | SLA / именованные фазы | разбей метод; Validate/Prepare; без flag-arg |
| **G. Guards / layer** | Проверки и знание в своём слое | leaf-guard; не фасад драйвера; feature envy → move |
| **H. Control flow** | Плоский поток | early return; pattern match; честный async |
| **I. Micro** | Локальная ясность | nameof; константы; sealed; имена вместо комментариев |

Если границы неочевидны — **одна** развилка, затем правь код.

## Инварианты

1. **Семантика прежде формы.**
2. **Мёртвый / миграционный / намеренный dual-path** — не смешивать.
3. **Прозрачная декомпозиция** — rule `transparent-decomposition`.
4. **Читаемость ≥ сжатие**; **YAGNI** — не плодить абстракции «на вырост».
5. **Один уровень абстракции** в теле метода (оркестратор не мешать с деталями).
6. **Defense-in-depth** на публичной границе — по команде; внутренние дубли → leaf.
7. **Валидация входа** — в сервисном/доменном слое, не в thin driver/COM facade (если не сказано иное).
8. **Не запускать** build/test без просьбы; **не раздувать scope**.
9. Файл ≈ тип; папки Generators по роли.

## Workflow

```
Task:
- [ ] 1. Классифицировать A–I (+ smell из patterns § Canon при необходимости)
- [ ] 2. Карта символов / call sites / слой
- [ ] 3. Правки: ядро → call sites → снос старого
- [ ] 4. Хвост: прокси, usings, false-async, мёртвые комментарии
- [ ] 5. Краткий отчёт
```

### Правки (кратко)

- Ядро/leaf → call sites → удаление старого пути и однострочных proxy.
- Sync/async дубли → общий private core.
- Вложенность → guards / early return / вынос шага.
- `async` только при реальном `await`.
- Имена раскрывают намерение; комментарий, повторяющий код — удалить или заменить именем.
- CodegenWriter: `using Block()`; infra: `EditorBrowsable(Never)` / `internal` осознанно.

## Ловушки

- Discovery ≠ codegen; `StationLink` (IR / Parts) ≠ emit `ChainSiteBinding`.
- Не лечить smell «ещё одним слоем» (speculative generality).
- `EditorBrowsable` ≠ удаление мёртвого API.
- Схлопывание огромной overload-матрицы — только по команде.
- Primitive obsession / value objects — не навязывать в hot path манифеста без ТЗ.
- Thin `ServiceStation`/`Station` overloads that wrap lambdas **must** null-check the user handler before wrapping — leaf sees only the non-null wrapper.

## Вне scope

- Новая семантика маршрута / TOP / якоря.
- Чистый perf без DRY/читаемости.
- Рабочие репо целиком → `work-projects` / `basetest` (portable-приёмы — в patterns).
