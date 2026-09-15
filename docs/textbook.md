# TrainOP: учебник

Этот текст можно читать сверху вниз. Он объясняет, *зачем* нужна библиотека, *как* писать маршруты и *что* происходит между вашей лямбдой и `RouteReport`. Справочные таблицы и глубокий разбор Roslyn-пайплайна остаются в соседних документах; здесь — связная картина.

---

## 1. Зачем TrainOP

В обычном C#-коде пайплайн «проверь → посчитай → сохрани» быстро обрастает вложенными `if`, ранними `return` и ручной передачей промежуточных значений. Railway Oriented Programming предлагает другую модель: шаги идут по рельсам; успех везёт данные дальше; ошибка останавливает поезд на станции и не заставляет каждую следующую функцию проверять «а не сломалось ли уже».

TrainOP воплощает эту идею для .NET (`netstandard2.0`). Вы описываете **маршрут** из **станций**. Между станциями едет **манифест груза** — набор именованных значений («вагонов»). Станция либо обновляет груз и даёт **зелёный** сигнал, либо возвращает **красный** и останавливает движение. В конце вы получаете **отчёт**: доехали ли до конца, какие станции посетили, какой груз остался на терминале.

Главное удобство библиотеки — не в том, что вы руками крутите словарь. Рекомендуемый стиль — **обработчики над данными**: обычные функции. Имена параметров — это ключи вагонов. Возврат — новые данные или сигналы из `RailwaySignals`. Манифест и адаптеры вызова скрыты в коде, который генерирует source generator: он сам достаёт вагоны, вызывает handler и записывает возвращённые поля обратно в манифест.

Без генератора fluent `.Station(...)` над данными не заработает: именно он эмитит типизированные расширения. Анализатор в том же пакете ловит ошибки потока вагонов ещё на этапе компиляции.

---

## 2. Метафора: рельсы, станции, сигналы

Думайте о пайплайне как о железной дороге.

**Манифест** (`CargoManifest`) — состав поезда: словарь `имя вагона → значение`. Он мутабельный: станции меняют его на месте через адаптер, а не передают новую копию на каждом шаге в пользовательском коде.

**Маршрут** (`TrainRoute`) — план пути: упорядоченный список станций, который вы собираете fluent-цепочкой.

**Запуск** — `TrainRoute.Travel()` / `TravelAsync()`: снимок плана + пустой манифест на каждый рейс.

**Зелёный сигнал** — «продолжай». Груз обновлён (или оставлен как был) и едет дальше.

**Красный сигнал** — «стоп». Обычные станции дальше по плану на красном не вызываются; сработать может только следующая станция техобслуживания (если она есть).

**Отчёт** (`RouteReport`) — итог рейса: `ReachedDestination`, история визитов, `FailureCode` / `FailureMessage`, доступ к терминальным вагонам через `Get<T>` или индексатор.

Эти слова — не украшение API. Они помогают читать код: ранняя станция загружает состав, проверка ставит семафор, техобслуживание чинит состав или фиксирует ошибку. Имя вроде `"Seed"` — привычка в примерах, а не отдельный тип станции.

---

## 3. Подключение

Нужны оба пакета: библиотека выполнения и генератор.

```bash
dotnet add package TrainOP
dotnet add package TrainOP.Generators
```

В `.csproj` это два `PackageReference` одной версии (сейчас ориентируйтесь на `CHANGELOG.md` / NuGet). При разработке внутри solution вместо пакетов используют `ProjectReference` на `TrainOP` и на `TrainOP.Generators` с `OutputItemType="Analyzer"`, плюс импорт `TrainOP.Generators.targets` — подробности в [nuget.md](nuget.md) и [getting-started.md](getting-started.md).

Требование к целевой платформе потребителя — совместимость с `netstandard2.0`. Проект должен быть SDK-style, иначе analyzers и source generators не подключатся.

---

## 4. Первый маршрут

Начнём с короткого платёжного сценария: положить данные, снизить сумму, проверить, прочитать результат.

```csharp
using TrainOP;

var route = new TrainRoute()
    .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
    .Station("Discount", (string paymentId, decimal amount) =>
        new { paymentId, amount = amount * 0.9m })
    .Station("Validate", (string paymentId, decimal amount) =>
        amount > 0
            ? RailwaySignals.Green(new { paymentId, amount })
            : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"));

var report = route.Travel();

if (report.ReachedDestination)
{
    var paymentId = report.Get<string>("paymentId");
    var amount = report.Get<decimal>("amount");
}
else
{
    Console.WriteLine($"{report.FailureCode}: {report.FailureMessage}");
}
```

Разберём по строкам.

`new TrainRoute()` создаёт пустой план. Каждое `.Station(имя, handler)` добавляет шаг. Имя станции попадает в отчёт и в `SignalIssue.StationName` при ошибке.

Канон один: вагоны появляются только из возвратов станций. Поэтому в примерах часто ставят первую станцию без параметров и называют её `"Seed"`: лямбда замыкает внешние переменные или просто кладёт константы (`() => new { paymentId, amount }`). Это **обычный сценарий**, а не особый вид станции. Для библиотеки `"Seed"` — такое же строковое имя, как `"Discount"` или `"Validate"`.

Стартовые данные можно набрать и иначе. Первая станция может вызвать метод и вернуть его результат:

```csharp
.Station("LoadPayment", () => PaymentRepository.GetDraft(orderId))
```

Или состав собирается **несколькими** ранними станциями: одна кладёт идентификатор, следующая по нему подгружает сумму, третья добавляет контекст. Пока анализатор видит, что к моменту чтения вагон уже произведён, порядок и число «загрузочных» шагов — дело сценария, а не особого API.

**Правильно** — вагоны появляются из станций:

```csharp
// Одна загрузочная станция
.Station("Seed", () => new { paymentId, amount })

// Вызов метода
.Station("Load", () => repo.Get(id))

// Постепенный набор
.Station("Id", () => new { paymentId })
.Station("Amount", (string paymentId) => new { paymentId, amount = repo.AmountOf(paymentId) })
```

**Неправильно** — читать вагон, который ещё никто не положил (TOP001):

```csharp
.Station("OnlyId", () => new { paymentId = "pay-1" })
.Station("UseAmount", (decimal amount) => new { amount })
```

`Discount` объявляет параметры `paymentId` и `amount`. Генератор трактует их как ключи вагонов: адаптер перед вызовом достанет эти значения из манифеста. Возврат анонимного типа означает: записать эти поля в манифест и дать зелёный сигнал.

`Validate` показывает явный сигнал через `RailwaySignals`: зелёный с данными (`Green(...)`) и красный (`Red(code, message)`). По смыслу зелёный с данными близок к голому `new { … }`; красный останавливает обычное движение по плану.

`Travel()` снимает снимок плана, создаёт пустой манифест и гоняет рейс. Успех читаете из отчёта; провал — через `FailureCode` / `FailureMessage` (и при необходимости `FailureIssues`).

Уже здесь виден контракт стиля «над данными»: **параметры = входы из манифеста**, **возврат = новые данные или сигнал**, **манифест в handler трогать не обязательно**.

---

## 5. Как текут данные

Представьте манифест после каждой станции в примере выше.

1. Старт: пусто.
2. После первой станции (`"Seed"` в примере): `paymentId`, `amount`.
3. После `Discount`: те же ключи, `amount` обновлён.
4. После `Validate` (зелёный): состав сохранён, рейс завершён успешно.

Запись возврата handler'а в манифест делает библиотека во время выполнения, а не ваш код. Правила стоит запомнить сразу — без них поведение «пропавшего» вагона выглядит магией.

Кратко по виду возврата:

| Что вернул handler | Что происходит |
|--------------------|----------------|
| Анонимный тип / record / именованный tuple | Поля записываются в манифест; дальше идёт зелёный сигнал |
| `RailwaySignals.Green(...)` с данными | То же: данные из аргумента записываются в манифест, затем зелёный |
| `RailwaySignals.Red(code, msg)` | Красный сигнал; успешная запись возврата не выполняется |
| `RailwaySignals.White` | Манифест **не меняется** (даже если в handler меняли `ref`-параметры) |
| `void` / `new { }` | Частичный возврат: значения `ref`-вагонов записываются; обычные входные вагоны, которых нет в возврате, **снимаются** с манифеста |
| `CargoManifest` | Манифест заменяется целиком (анализатор предупредит TOP004) |

Ниже — развёрнутые варианты на одной и той же исходной картине.

### Исходный манифест для примеров

Перед станцией в манифесте уже есть:

- `paymentId` = `"pay-1"`
- `amount` = `100m`
- `note` = `"keep"` — вагон, который станция **не** принимает (лежит «сбоку»)

Если не сказано иное, handler обычной `.Station` объявлен так: `(string paymentId, decimal amount) => …`.

### Обычная станция: варианты возврата

**1. Полный возврат обоих входов**

```csharp
(string paymentId, decimal amount) => new { paymentId, amount = amount * 0.9m }
```

После: `paymentId` = `"pay-1"`, `amount` = `90m`, `note` = `"keep"`.  
Оба входа обновлены/сохранены; посторонний `note` не тронут.

**2. То же через `Green`**

```csharp
(string paymentId, decimal amount) =>
    RailwaySignals.Green(new { paymentId, amount = amount * 0.9m })
```

После: как в п.1. `Green` только оборачивает те же данные.

**3. Частичный возврат — вернули только `amount`**

```csharp
(string paymentId, decimal amount) => new { amount = amount * 0.9m }
```

После: `amount` = `90m`, `note` = `"keep"`. Вагона `paymentId` **больше нет**: обычный вход, которого нет в возврате, снимается.  
Именно поэтому частичный возврат опасен, если хвост снова ждёт снятый вагон (TOP003).

**4. Добавили новое поле**

```csharp
(string paymentId, decimal amount) =>
    new { paymentId, amount, status = "discounted" }
```

После: `paymentId`, `amount` = `100m`, `status` = `"discounted"`, `note` = `"keep"`.  
Новый ключ появляется; `note` по-прежнему на месте.

**5. `White` — ничего не писать**

```csharp
(string paymentId, decimal amount) => RailwaySignals.White
```

После: как было — `paymentId`, `amount` = `100m`, `note`.  
Даже если внутри handler'а меняли локальные переменные или `ref`, в манифест это не попадёт.

**6. Пустой возврат / `void`**

```csharp
(string paymentId, decimal amount) => { /* side effect */ }
// или: => new { }
```

После: остаётся только `note` = `"keep"`.  
Обычные входы `paymentId` и `amount` сняты (их нет в возврате), `ref` здесь не было.

**7. `ref` вместо возврата поля (сахар)**

```csharp
(string paymentId, ref decimal amount) => { amount *= 0.9m; }
```

После: `amount` = `90m`, `note` = `"keep"`. Вагона `paymentId` **нет**.  
`amount` записан из `ref`; `paymentId` — обычный вход без поля в возврате, поэтому снят. Эквивалент по смыслу для `amount`: вернуть `new { amount = amount * 0.9m }` без `ref`, но тогда нужно явно решить судьбу `paymentId` (вернуть его или осознанно снять).

**8. `ref` + явный возврат другого входа**

```csharp
(string paymentId, ref decimal amount) =>
{
    amount *= 0.9m;
    return new { paymentId = paymentId + "-x" };
}
```

После: `paymentId` = `"pay-1-x"`, `amount` = `90m`, `note` = `"keep"`.  
`paymentId` из возврата, `amount` из `ref`.

**9. `ref` + `White`**

```csharp
(ref decimal amount) =>
{
    amount = 1m;
    return RailwaySignals.White;
}
```

После: `amount` по-прежнему `100m` (и остальные вагоны как были).  
`White` отменяет любую запись, в том числе обратную запись `ref`.

**10. Красный сигнал**

```csharp
(string paymentId, decimal amount) =>
    RailwaySignals.Red("INVALID", "bad amount")
```

Манифест для успешной записи возврата не обновляется этим handler'ом: рейс уходит в красную ветку обхода (сервисные станции дальше по плану). Состав на момент ошибки — тот, что был **до** этой станции.

### Станция техобслуживания: те же возвраты, другие правила

На `.ServiceStation` состав манифеста **нельзя** расширить или сузить. Разрешено только обновить значения уже существующих ключей. Возьмём тот же исходный манифест и handler `(decimal amount, SignalIssue issue) => …` (вход один — `amount`).

**11. Обновили существующий ключ**

```csharp
(decimal amount, SignalIssue issue) => RailwaySignals.Green(new { amount = 1m })
```

После: `paymentId` = `"pay-1"`, `amount` = `1m`, `note` = `"keep"`.  
`amount` обновлён; остальные вагоны на месте.

**12. Попытались добавить новый ключ**

```csharp
(decimal amount, SignalIssue issue) => new { amount = 1m, status = "recovered" }
```

Анализатор: **TOP015** — сервисная станция не может расширить состав манифеста.

**13. Опустили входной вагон**

```csharp
(string paymentId, decimal amount, SignalIssue issue) => new { amount = 1m }
```

Анализатор: **TOP016** — опуск non-`ref` входа изменил бы состав (на обычной станции `paymentId` снялся бы).  
Обновление только уже существующих ключей (в том числе невходных, если они уже в манифесте) — допустимо:

```csharp
(decimal amount, SignalIssue issue) => new { amount = 1m } // OK: единственный вход возвращён
```

**14. `White` / красный на техобслуживании**

`RailwaySignals.White` — манифест как был.  
`RailwaySignals.Red(...)` — снова красный, запись успешного возврата не делается.

### Как этим пользоваться

- Нужны те же вагоны дальше — верните их явно (или обновите через `ref` и верните остальные входы).
- Хотите убрать вагон на обычной станции — не включайте его в возврат (частичный возврат).
- Хотите «только посмотреть / залогировать» без изменений — `RailwaySignals.White`.
- На техобслуживании не рассчитывайте добавить или снять вагон: только правка существующих значений.

Имена вагонов сравниваются как обычные строки и **чувствительны к регистру**. Типы должны согласовываться вдоль цепочки: нельзя на одной станции положить `string id`, а на следующей читать его как `int` — будет TOP002 ещё до запуска.

Для кортежей предпочитайте имена: `(paymentId: …, amount: …)` или вывод имён из идентификаторов `(paymentId, amount)`. Голый `(expr1, expr2)` без имён даёт `Item1`/`Item2` и предупреждение TOP006: соответствие полям идёт по позиции и легко ломается при перестановке.

**Правильно / неправильно** для кортежей:

```csharp
// OK — имена
(paymentId: paymentId + "-x", amount: amount * 0.9m)
(paymentId, amount)

// Плохо — TOP006, хрупкий ItemN
(paymentId + "-x", amount * 0.9m)
```

---

## 6. Сигналы и остановка

Зелёный — продолжение. Красный — остановка **на этой станции**: хвост маршрута не выполняется.

```csharp
.Station("Validate", (string paymentId, decimal amount) =>
    amount > 0
        ? RailwaySignals.Green(new { paymentId, amount })
        : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"))
```

`RailwaySignals` — то, что возвращает пользовательский handler. Типы движка `GreenSignal` / `RedSignal` из handler'а **не** возвращайте: анализатор ответит TOP010. Адаптер сам превратит `RailwaySignals.Red` во внутренний красный сигнал с `SignalIssue(code, message, stationName)`.

**Правильно:**

```csharp
amount > 0
    ? RailwaySignals.Green(new { paymentId, amount })
    : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive")

// или просто данные — поля тоже запишутся в манифест, сигнал будет зелёным
return new { paymentId, amount };
```

**Неправильно** (TOP010):

```csharp
return RailwaySignals.Green();
return RailwaySignals.Red(issue);
```

Красный сигнал хранит **цепочку** записей об ошибках: от корневой причины к непосредственной остановке. `Issue` — последний элемент (то же, что отражает `FailureCode` / `FailureMessage` в отчёте). Одна простая станция без вложенных поездов даёт один элемент. При провале подмаршрута родитель может пробросить историю:

```csharp
return RailwaySignals.Red(
    "BRANCH_FAILED",
    "branch did not complete",
    subReport.FailureIssues);
```

Необработанное исключение внутри станции (кроме отмены) тоже становится красным: код `STATION_EXCEPTION`, сообщение с текстом исключения. Для станции техобслуживания — `SERVICE_STATION_EXCEPTION`.

`OperationCanceledException` **не** маскируется под красный сигнал: она пробрасывается наружу. Отмена — не бизнес-ошибка маршрута.

---

## 7. Станция техобслуживания

`ServiceStation` стоит в том же плане, что и обычные станции. Правила обхода простые: обычная станция вызывается только после зелёного сигнала; сервисная — только после красного; иначе шаг пропускается. Типичные роли — **восстановить** данные и продолжить хвост маршрута, либо **отреагировать на красный** без восстановления (лог, метрика) и снова вернуть красный. Чередование `Station → ServiceStation → Station → ServiceStation` даёт локальное восстановление после каждого рискованного шага. Если красный дошёл до конца плана — отчёт неудачный.

```csharp
var route = new TrainRoute()
    .Station("Seed", () => new { amount = -1m })
    .Station("Validate", (decimal amount) =>
        amount > 0
            ? RailwaySignals.Green(new { amount })
            : RailwaySignals.Red("INVALID", "amount must be positive"))
    .ServiceStation("Recovery", (decimal amount, SignalIssue issue) =>
        issue.Code == "INVALID"
            ? RailwaySignals.Green(new { amount = 1m })
            : RailwaySignals.Red("CANNOT_RECOVER", "unsupported failure"))
    .Station("Double", (decimal amount) => new { amount = amount * 2m });
```

**Правильно** — позиция зависит от роли:

```csharp
// Локальное восстановление: сервисная сразу после риска, чтобы хвост ещё мог поехать
.Station("Validate", …)
.ServiceStation("Recovery", …)
.Station("Charge", …)

// Несколько точек восстановления
.Station("Validate", …)
.ServiceStation("Recovery", …)
.Station("Charge", …)
.ServiceStation("ChargeRecovery", …)

// Финальная обработка ошибки без восстановления (лог, метрика, аудит):
// сервисная в конце — нормальный паттерн; обычно снова красный сигнал после побочного эффекта
.Station("Validate", …)
.Station("Charge", …)
.ServiceStation("LogFailure", (SignalIssue issue) =>
{
    logger.LogWarning("{Code}: {Message}", issue.Code, issue.Message);
    return RailwaySignals.Red(issue.Code, issue.Message);
})
```

После зелёного сигнала шаги `ServiceStation` пропускаются. После красного пропускаются обычные `.Station`, пока не встретится сервисная или конец плана.

**Не путать роли.** Если нужно восстановить данные и продолжить хвост, сервисная в самом конце после ещё не выполненных обычных станций не «долечит» уже пропущенный участок: при красном сигнале `Charge` между `Validate` и конечной сервисной не вызовется. Для продолжения маршрута ставьте восстановление **перед** шагами, которые должны ехать после починки. Если восстановление не предусмотрено — конечная `ServiceStation` как раз для финальной реакции на ошибку (лог и повторный красный сигнал).

Отдельно про запись возврата в манифест на техобслуживании. На обычной станции возврат может добавить поля, обновить существующие и снять входные вагоны, которых нет в возврате. На `ServiceStation` иначе: разрешено только **обновить значения уже существующих вагонов**. Попытка добавить ключ (**TOP015**), опустить входной non-`ref` вагон (**TOP016**) или вернуть `CargoManifest` (**TOP017**) — ошибка анализатора. Так сделано потому, что хвост маршрута уже проверен на исходный набор вагонов, а техобслуживание может вообще не вызваться — нельзя подменять состав манифеста «на удачу».

**Неправильно** — менять состав на сервисной станции:

```csharp
// TOP015 — status не был в манифесте
.ServiceStation("Recovery", (decimal amount, SignalIssue issue) =>
    new { amount = 1m, status = "recovered" })

// TOP016 — опущен вход paymentId
.ServiceStation("Recovery", (string paymentId, decimal amount, SignalIssue issue) =>
    new { amount = 1m })
```

**Правильно** — менять только то, что уже есть:

```csharp
.ServiceStation("Recovery", (decimal amount, SignalIssue issue) =>
    RailwaySignals.Green(new { amount = 1m }))
```

Параметры handler'а техобслуживания знакомы: вагоны из манифеста рейса, плюс по желанию служебные параметры — `SignalIssue issue` (непосредственная остановка), `IReadOnlyList<SignalIssue> issues` (полная цепочка), `RedSignal red` (issues), `CargoManifest manifest`, `CancellationToken`.

Контракт возврата тот же (зелёный / красный / `RailwaySignals.White` / данные), но запись в манифест — только обновление существующих ключей, как выше.

Если ни одна `ServiceStation` не стоит после упавшей станции (или все снова вернули красный), ошибка доходит до конца рейса.

Есть и низкоуровневый запасной вариант без сгенерированного адаптера вагонов: handler вида `Func<RedSignal, CargoManifest, Signal>` (и асинхронный вариант) с правками через `manifest.LoadWagon(...)`. Для обычного кода достаточно формы над данными.

---

## 8. Async, отмена и `ref`

Станция может быть асинхронной: `async`-лямбда, возврат `Task` / `Task<T>`. Тогда запускайте только `TravelAsync`. Синхронный `Travel()` на таком маршруте бросит `InvalidOperationException` с намёком «Use TravelAsync».

```csharp
var route = new TrainRoute()
    .Station("Seed", () => new { counter = 10 })
    .Station("Fetch", async (int counter, CancellationToken token) =>
    {
        await Task.Delay(50, token);
        return new { counter = counter * 2 };
    });

var report = await route.TravelAsync();
```

`CancellationToken` — служебный параметр: его подставляет библиотека при выполнении, это не вагон. Тот же токен можно передать в `Travel(ct)` / `TravelAsync(ct)`.

Параметр `ref` на вагоне — по сути синтаксический сахар вместо явного возврата нового значения обычным способом. Вместо «принять `amount`, посчитать, вернуть `new { amount = … }`» можно написать `(ref decimal amount) => { amount *= 0.9m; }`: библиотека после вызова запишет изменённое значение обратно в манифест, даже если поля нет в анонимном возврате. По смыслу это тот же результат, что у стандартного возврата обновлённого вагона; меняется только форма записи. На обычной станции и на `ServiceStation` `ref` необязателен. Но **`async` + `ref` запрещены языком C# (CS1988)**: машина состояний не может безопасно удержать ссылку между `await`. Поэтому асинхронные handler'ы принимают вагоны по значению и возвращают новые данные обычным возвратом. Параметры `in` / `out` библиотека для обратной записи не использует.

**Правильно:**

```csharp
.Station("Fetch", async (int counter, CancellationToken token) =>
{
    await Task.Delay(50, token);
    return new { counter = counter * 2 };
});

await route.TravelAsync();

// sync + ref — допустимо
.Station("Bump", (ref decimal amount) => { amount += 1m; })
```

**Неправильно:**

```csharp
// на маршруте с async-станциями
route.Travel(); // InvalidOperationException: Use TravelAsync

// CS1988 — язык не позволяет
.Station("Fetch", async (ref decimal amount, CancellationToken token) => { … })
```

`RailwaySignals.White` при наличии `ref` тоже не записывает изменения: `White` означает «манифест как был». Чтобы сохранить новые значения `ref`-вагонов, нужен пустой/`void`-возврат, частичный возврат или явные данные в возврате.

---

## 9. Композиция: вложенные маршруты и ветвление

Отдельного API «switch/fork» нет. Ветвление собирают композицией:

1. Подмаршруты — свои `TrainRoute`, обычно фабрики `Build(...)` с собственной первой загрузочной станцией.
2. Родительская станция по данным выбирает подмаршрут, вызывает `Travel()`.
3. По `RouteReport` дочернего рейса родитель **сам** решает, что вернуть: данные или `RailwaySignals.Red(...)`.

Манифест руками между родителем и ребёнком не собирают: в дочерний seed передают значения параметрами `Build`. Каждый подмаршрут анализируется генератором независимо.

```csharp
internal static class PremiumBranchRoute
{
    public static TrainRoute Build(string paymentId, decimal amount) => new TrainRoute()
        .Station("Seed", () => new { paymentId, amount })
        .Station("ApplyPremiumDiscount", (string paymentId, decimal amount) =>
            new { paymentId = paymentId + "-premium", amount = amount * 0.8m, channel = "premium" });
}

var route = new TrainRoute()
    .Station("Seed", () => new { paymentId = "pay-branch", amount = 100m, tier = "premium" })
    .Station("Branch", (string paymentId, decimal amount, string tier) =>
    {
        var subRoute = tier == "premium"
            ? PremiumBranchRoute.Build(paymentId, amount)
            : StandardBranchRoute.Build(paymentId, amount);

        var subReport = subRoute.Travel();
        if (!subReport.ReachedDestination)
        {
            return RailwaySignals.Red(
                "BRANCH_FAILED",
                $"tier '{tier}' did not complete",
                subReport.FailureIssues);
        }

        return new
        {
            paymentId = subReport.Get<string>("paymentId"),
            amount = subReport.Get<decimal>("amount"),
            channel = subReport.Get<string>("channel"),
        };
    })
    .Station("Finalize", (string paymentId, decimal amount, string channel) =>
        new { paymentId, amount, channel, status = "completed" });
```

Runnable-пример: `samples/TrainOP.Samples/Examples/NestedBranchingRouteExample.cs`.

Так библиотека остаётся простой на поверхности: один примитив станции плюс вложенные поездки, без отдельного языка ветвлений.

---

## 10. Какие формы кода «видит» библиотека

Генератор и анализатор понимают не любой C#. Handler должен быть:

- лямбдой `(string paymentId, decimal amount) => …`,
- anonymous method `delegate(…) { … }`,
- или method group / local function, **объявленными в текущей compilation** (есть исходник в проекте).

**Правильно:**

```csharp
.Station("Discount", (string paymentId, decimal amount) =>
    new { paymentId, amount = amount * 0.9m })

.Station("Discount", Discount);

static object Discount(string paymentId, decimal amount) =>
    new { paymentId, amount = amount * 0.9m };
```

**Неправильно** (TOP009):

```csharp
Func<string, decimal, object> discount = (paymentId, amount) =>
    new { paymentId, amount = amount * 0.9m };

.Station("Discount", discount); // переменная Func<> — нет
```

Цепочку тоже нужно собирать узнаваемо.

**Правильно:**

```csharp
var route = new TrainRoute()
    .Station("Seed", () => new { id = 1 })
    .Station("Next", (int id) => new { id = id + 1 });

var route = new TrainRoute();
route = route.Station("Seed", () => new { id = 1 });

var route = CreateSeed().Station("Next", (int id) => new { id = id + 1 });
```

**Неправильно** (TOP005 / TOP014):

```csharp
void Extend(TrainRoute baseRoute) =>
    baseRoute.Station("Next", (int id) => new { id }); // receiver-параметр

var a = new TrainRoute(); var b = new TrainRoute(); // два new на одной строке — TOP014
```

Допустимый пользовательский API — fluent `.Station` / `.ServiceStation`, `RailwaySignals`, `Travel*`. Методы вроде `RegisterStation` существуют для генератора и скрыты из IntelliSense; руками их вызывать не нужно.

---

## 11. Что происходит при компиляции

Когда вы пишете `.Station(...)`, компилятор видит вызов расширения. Откуда берётся overload с вашей сигнатурой делегата?

**Source generator** (`TrainRouteStationGenerator` в пакете `TrainOP.Generators`) сканирует синтаксис, находит кандидатов `.Station` / `.ServiceStation`, строит схему каждого handler'а и эмитит файл вроде `TrainRouteStation.Extensions.g.cs`.

Упрощённо пайплайн такой:

1. **Обнаружение.** SyntaxProvider отсеивает узлы дешёвым фильтром, затем семантически разбирает handler: параметры (вагон vs служебные), `ref`, форму возврата.
2. **Граф маршрута.** Из якорей (`new TrainRoute()`, factory) и следующих `.Station` собираются цепочки с ключом цепочки вызывающего кода и порядковым индексом станции.
3. **Группировка.** Handler'ы с одинаковой CLR-сигнатурой типов попадают в одну группу делегата.
4. **Эмиссия.** Для группы эмитится публичный `.Station` и тело адаптера: достать вагоны → вызвать handler → записать возврат в манифест → зарегистрировать станцию.

Параллельно **анализатор** (`ChainValidationAnalyzer`) не генерирует код. Он симулирует поток вагонов по цепочке: какие ключи «живы», каких типов, что снято частичным возвратом. Отсюда TOP001 (вагона ещё нет), TOP002 (конфликт типов), TOP003 (сняли, а ниже нужен). Он же ловит станции вне цепочки (TOP005), плохие формы handler'а (TOP009), возврат внутренних типов сигнала (TOP010), несходящиеся ветки (TOP008), разъехавшиеся пути возврата у factory (TOP012/TOP013).

Практический смысл: без генератора API над данными просто не соберётся; без анализатора «едет», но ошибки вагонов всплывут при запуске как `KeyNotFoundException` или неверная запись возврата в манифест. Вместе они переносят контракт маршрута на этап компиляции.

Глубокий разбор шагов Roslyn, `RegisterSourceOutput` и эмиссии — в [architecture-internals.md](architecture-internals.md). Для работы с библиотекой достаточно понимать роли: **генератор пишет адаптеры**, **анализатор проверяет поток данных**, **исполнитель гоняет зарегистрированный план**.

---

## 12. Зачем различать цепочки с одной сигнатурой

Два handler'а `(string, decimal)` для CLR — один и тот же тип делегата. Но в одном маршруте параметры могут называться `paymentId`/`amount`, в другом — `orderId`/`total`. Одна общая overload не знает, какие имена вагонов подставить на конкретном месте вызова.

TrainOP решает это разведением по цепочке вызывающего кода. На `new TrainRoute()` штампуется `CallerChainKey` — идентичность цепочки. Каждая регистрация станции несёт ещё порядковый индекс. Сгенерированный код по паре «ключ цепочки + индекс станции» выбирает привязки, известные на этапе компиляции: имена входов, члены возврата, флаги `ref`. Разбор имён параметров во время выполнения не нужен: адаптер уже знает ключи.

Если в группе сигнатур только один набор имён, эмитится более простой общий вариант. Если наборов несколько — таблицы выбора привязки (`ResolveChainBinding_*`). В обоих случаях публичный API для вас один: `.Station("Name", handler)`.

Отсюда ограничения вроде TOP014 и запрет «плавающих» receiver'ов: генератору нужна стабильная идентичность цепочки в исходнике.

---

## 13. Как едет поезд при выполнении

После того как все `.Station` / `.ServiceStation` отработали на этапе построения маршрута, у `TrainRoute` есть единый список шагов — план.

1. `Travel()` / `TravelAsync()` копирует план и создаёт пустой манифест рейса.
2. Обход стартует с условного зелёного сигнала (без груза в самом сигнале).
3. Для каждого шага по порядку: обычная станция выполняется только после зелёного, сервисная — только после красного; иначе шаг пропускается.
4. Обычная станция: достать вагоны → вызвать handler → записать возврат в манифест → получить сигнал.
5. Сервисная: обработка красного сигнала; зелёный продолжает план, красный идёт дальше по тем же правилам обхода.
6. Исключение станции → красный с `STATION_EXCEPTION` / `SERVICE_STATION_EXCEPTION` (кроме отмены).
7. Конец плана на зелёном → успешный `RouteReport` (сигнал + `Manifest`); красный до конца → неудачный отчёт.

Пользовательский код в счастливом пути не трогает `CargoManifest`. Исключения — служебный параметр `CargoManifest` в handler'е и низкоуровневый `ServiceStation` с `(RedSignal, CargoManifest)`. Для отладки полезны `report.Visits` и `report.Manifest.InspectWagons()`, но итоговый результат рейса — отчёт.

---

## 14. Маршруты между сборками

Маршрут можно собрать в class library и продолжить в приложении.

В библиотеке публикуют public factory:

```csharp
public static class PaymentModule
{
    public static TrainRoute Build() => new TrainRoute()
        .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
        .Station("Discount", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}
```

Генератор в проекте библиотеки эмитит метаданные схемы (`RouteSchemaFor`, `RouteSchemaWagon`, включая `CallerChainKey` и `StationCount`). Приложение-потребитель продолжает цепочку:

```csharp
PaymentModule.Build()
    .Station("Finalize", (string paymentId, decimal amount) =>
        new { paymentId, status = "completed" });
```

Анализатор сборки-потребителя читает экспортированную схему терминальных вагонов и проверяет продолжение. Закрытая (private/internal) фабрика в том же проекте схему не требует — тело видно межпроцедурно. Публичная фабрика без схемы даст информационный TOP011: стык не проверяется надёжно.

Если у фабрики несколько `return`, все пути должны сходиться к одному итоговому набору вагонов (TOP012 / TOP013). Иначе потребитель не сможет честно продолжить маршрут.

Подробности и тесты: [cross-assembly-routes.md](cross-assembly-routes.md), проекты `TrainOP.RouteLib.Tests` / `TrainOP.RouteConsumer.Tests`.

---

## 15. Диагностики как учебник ошибок

Когда IDE подчёркивает вызов станции, почти всегда это спор о потоке данных или о форме цепочки. Краткая карта:

| Код | Смысл |
|-----|--------|
| TOP001 | Станция ждёт вагон, которого ещё нет |
| TOP002 | Конфликт типов одного имени |
| TOP003 | Вагон сняли, а ниже он снова нужен |
| TOP004 | `return CargoManifest` — полная замена (warning) |
| TOP005 | `.Station` вне поддерживаемой цепочки |
| TOP006 | Tuple без имён → `ItemN` (warning) |
| TOP007 | Разные имена вагонов при одной type-сигнатуре без разведения цепочек |
| TOP008 | Ветки не сходятся перед следующей станцией |
| TOP009 | Неподдерживаемая форма handler'а |
| TOP010 | Вернули внутренние `GreenSignal`/`RedSignal` вместо `RailwaySignals` |
| TOP011 | Публичная фабрика без экспортированной схемы (info) |
| TOP012 / TOP013 | Пути возврата фабрики разъехались / неизвестны |
| TOP014 | Два `new TrainRoute()` на одной строке |
| TOP015 | ServiceStation добавляет вагон |
| TOP016 | ServiceStation опускает входной non-`ref` вагон |
| TOP017 | ServiceStation возвращает `CargoManifest` |

TOP001–TOP003 чаще всего учат правильной загрузке вагонов в начале цепочки и осторожному частичному возврату. TOP005/TOP009/TOP014 — правильной форме кода, которую видит генератор. TOP010 — границе между `RailwaySignals` и внутренними типами сигналов. TOP008/TOP012 — композиции веток и библиотек маршрутов. TOP015–TOP017 — запрету менять состав манифеста на техобслуживании.

---

## 16. Практический стиль письма маршрутов

Соберите привычки в один список — они следуют из глав выше.

1. Вагоны появляются из станций. Частый приём — первая станция-загрузчик (`"Seed"` или вызов метода); можно набирать состав несколькими ранними станциями. У `Build(...)` вход обычно уходит в замыкание первой станции фабрики.
2. Имена параметров = стабильные ключи предметной области (`paymentId`, не `p`).
3. Для успеха возвращайте данные или `RailwaySignals.Green`; для остановки — только `RailwaySignals.Red`.
4. Частичный возврат используйте осознанно; иначе верните все поля, нужные хвосту.
5. При асинхронных станциях — `TravelAsync`; без `ref` на вагонах.
6. Ветвление — вложенные `Build` + родительская станция, читающая `RouteReport`.
7. `ServiceStation` в плане по роли: локальное восстановление перед хвостом или финальный лог/аудит в конце без продолжения; на техобслуживании можно только обновить уже существующие вагоны, не меняя состав манифеста.
8. Держите цепочку «видимой»: fluent от `new` / локальная переменная / factory, без receiver-параметров.
9. Читайте TOP* как контракт, а не как шум анализатора.
10. Смотрите примеры в `samples/TrainOP.Samples/Examples/` и сквозной тест `DataOrientedPaymentRouteEndToEndTests`.

---

## 17. Куда идти дальше

Вы прошли круг: метафора → первый маршрут → поток данных и сигналы → техобслуживание → async → композиция → компиляция и выполнение → маршруты между сборками.

Дальше по задаче:

- детали API и таблицы — [core-api.md](core-api.md);
- установка и устранение неполадок пакетов — [nuget.md](nuget.md);
- устройство генератора, разведение цепочек, карта файлов — [architecture-internals.md](architecture-internals.md);
- библиотеки маршрутов — [cross-assembly-routes.md](cross-assembly-routes.md);
- объём кода и бенчмарки — [code-volume-comparison.md](code-volume-comparison.md), [`benchmarks/README.md`](../benchmarks/README.md).

Если читать только один документ «чтобы понять библиотеку» — достаточно этого учебника. Остальное — справочник и углубление, когда конкретный TOP* или сценарий потребует точности.
