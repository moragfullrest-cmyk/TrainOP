# Приёмы улучшения качества кода

Формулировки → что править. Сверять с текущим деревом.

## A. Cleanup

| Формулировка | Улучшение |
|--------------|-----------|
| удали неиспользуемый / мёртвый код | Меньше шума; `[Obsolete]` — только по команде |
| убери избыточную обёртку / однострочный proxy | Нет ложного второго API |
| убери избыточный проход / мёртвую ветку | Та же модель, меньше работы |
| убери `#region` в исходниках / лишние using | Плоская навигация |
| убери абстракцию без call sites (YAGNI) | Нет speculative generality |

## B. Dedupe

| Формулировка | Улучшение |
|--------------|-----------|
| убери дубли хелперов / схем / emit | Один источник правды |
| вынеси общий core sync+async | Одна семантика |
| централизуй разбор имён / invocation | Одна точка TryParse… |
| unroll при известной схеме | Нет runtime-копипасты циклов |
| consolidate duplicate conditional fragments | Общий хвост после ветвлений |

## C. Structure

| Формулировка | Улучшение |
|--------------|-----------|
| разложи по папкам / partial по роли | Навигация без «простыни» |
| унифицируй имена (файл = тип) | Предсказуемый поиск |
| semantic placeholder вместо `bool`/`true` | Тип несёт смысл |
| data clump (одни и те же 3–4 поля вместе) → тип | Меньше длинных сигнатур |

## D. Emit hygiene (TrainOP)

| Формулировка | Улучшение |
|--------------|-----------|
| Block()/IndentScope | Нет рассинхрона отступов |
| generator = orchestration | Emit в extensions |
| регионы/имена цепочек в `.g.cs` | Читаемый generated |

## E. Surface / visibility

| Формулировка | Улучшение |
|--------------|-----------|
| EditorBrowsable(Never) на infra | IntelliSense = пользовательский API |
| public → internal (+ IVT при нужде) | Уже контракт пакета |
| убери параллельный API | Один вход |
| XML на публичном; снять с невидимого | Контракт NuGet |
| CQS: не смешивать query и command в одном имени | Имя = эффект |

## F. Decompose

| Формулировка | Улучшение |
|--------------|-----------|
| разбей большой метод на фазы | Оркестратор + шаги; один уровень абстракции |
| выдели Validate / Prepare / Execute | Одна ответственность |
| разбей god-type на роли | Тестируемость |
| убери bool flag-argument (раздели методы) | Нет `Do(x, true)` |
| method group вместо lambda | Меньше шума |
| replace comment with named method/variable | Намерение в идентификаторе |

## G. Guards / layer

| Формулировка | Улучшение |
|--------------|-----------|
| схлопни дубли validation к leaf | Один guard внизу |
| вынеси валидацию из фасада драйвера/COM | Правильный слой |
| feature envy → move method/logic к данным | Знание рядом с данными |
| fail fast на входной границе | Ранний явный отказ |
| не выкидывай публичный ArgumentNull без команды | Граница пакета |

## H. Control flow

| Формулировка | Улучшение |
|--------------|-----------|
| pattern matching / switch expression | Одно ядро ветвлений |
| early return / guard clauses | Меньше глубины |
| убери `async` без `await` | Честный sync |
| foreach+continue vs кривой FilterOut | Читаемый поток |
| не Ok(default) при cancel | Согласованный fail |
| replace nested conditional with polymorphism/strategy | Только если ветвлений много и стабильны типы — иначе guards |

## I. Micro

| Формулировка | Улучшение |
|--------------|-----------|
| магические числа/строки → константа/`nameof` | Один рычаг; rename-safe |
| интерполяция | Читаемые сообщения |
| expression-bodied на тонких адаптерах | Меньше шума |
| sealed на ненаследуемых | Явный контракт |
| опечатки / пустые строки / usings | Чистый diff |
| `IReadOnlyCollection<T>` наружу | Контракт мутации |
| языковые типы; `== false` если канон репо | Единый стиль |
| `ArgumentNullException.ThrowIfNull` / `using var` | Идиомы современного C# (если TFM позволяет) |
| discard `_` для неиспользуемого out | Нет ложных локалов |

---

## Canon — priors модели / SE (не «только лучший код»)

Эвристики, которым обычно учат на рефакторинге. Применять **после** правил репо и семантики TrainOP.

### Smell → ход

| Smell | Ход | Тип |
|-------|-----|-----|
| Long Method | Extract Method; фазы оркестратора | F |
| Large Class / God Object | Extract Class по роли | F, C |
| Long Parameter List | Introduce Parameter Object; data clump | C, F |
| Divergent Change | Разделить тип по осям изменений | C, F |
| Shotgun Surgery | Свести знание в одно место | B, G |
| Feature Envy | Move Method к «хозяину» данных | G |
| Data Clumps | Extract Class/Record | C |
| Primitive Obsession | Rarely: маленький тип; не навязывать в CargoManifest hot path | C |
| Switch / type code explosion | Strategy / полиморфизм *или* оставить switch, если стабильно | H |
| Temporary Field | Убрать или выделить контекст-объект | F |
| Message Chains | Hide Delegate *или* Explicit intermediate (не всегда зло) | G |
| Middle Man | Remove Middle Man (однострочный proxy) | A |
| Inappropriate Intimacy | Ослабить связь; вынести общий кусок | B, G |
| Alternative Classes Different Interfaces | Унифицировать имена/сигнатуры | B, E |
| Incomplete Library Class | Extension / helper рядом, не «насильно» в BCL | B |
| Refused Bequest | Убрать наследование; composition | C |
| Comments (шум) | Лучшее имя; удалить эхо кода; оставить *почему* | F, I |
| Dual for-loops / copy-paste branches | Extract + parametrize | B |
| Speculative Generality | Inline / delete unused layer | A |
| Dead Code | Delete | A |
| Speculative boolean flags | Split methods | F |

### Принципы (коротко)

- **SLAP** — в одном методе один уровень абстракции.
- **DRY** — но не «неправильный DRY» (схлопывать только одинаковую *причину* изменения).
- **YAGNI** — не добавлять точки расширения без второго потребителя.
- **Fail fast** — на границе; внутри — согласованный leaf.
- **Names > comments** для *что*; комментарий для *почему/инвариант*.
- **Boy Scout** — хвост usings/пустых строк/опечаток в уже тронутых файлах ок; не раздувать diff на весь solution.

### C#-идиомы (качество без смены поведения)

- `using var` / `await using`; не вкладывать using без нужды.
- `ThrowIfNull`, `nameof` в исключениях и диагностиках.
- `switch` expression / pattern для плоских разборов типов.
- `sealed` + `file`-scoped namespace уже в каноне репо.
- Избегать sync-over-async и `.Result`/`.Wait()` в библиотечном коде.

### Не тащить из канона без ТЗ

- Повальное введение Value Objects / Result monad везде.
- Полиморфизм вместо каждого `switch` (в генераторе часто хуже).
- «Чистая архитектура» слоёв поверх уже выбранной схемы TrainOP.
- Микро-оптимизации под видом качества.

---

## Portable — хроники вне TrainOP

| Паттерн | Суть | Тип |
|---------|------|-----|
| PrintCheck | Validate / Prepare отдельными методами | F |
| DrvFR | Валидация не в фасаде драйвера | G |
| RailwayHelper | Core + pattern match; partial; false-async | B, C, H |
| AnswersService | God-service → роли | F |
| Endpoints | Группировка по домену | C |
| OmniBroker | sealed + XML; sync-over-async; CS1998 | E, H, I |
| Work style | `== false`, IReadOnlyCollection, без `#region` в тестах | I / basetest |
| Ошибки | Развести коды, не свалка в один UnableToConnect | E, I |
| Вложенность | early return / вынос шага | H, F |

**Не тащить слепо work Domain/Input/DI** в TrainOP без команды.

## Не качество-рефакторинг

- Объяснение двух типов без merge (`StationLink` IR vs emit `ChainSiteBinding`).
- Прозрачная typed-декомпозиция без derived `TrainRoute`.
- Perf-бэклог без структурного DRY.
- Полный перевод docs / смена семантики API.
- Снос thin null-check на `ServiceStation`/`Station` wrappers — leaf видит только non-null lambda.
