# Архитектура 1.0 RC1 (фактическая)

Схемы — Mermaid (редактируемый текст; открываются в GitHub, VS Code с расширением Mermaid, mermaid.live).

## Слои
```mermaid
graph TD
  JSON["StreamingAssets/CarDiagnosis/orders.json"] --> Loader
  RES["Resources/CarDiagnosis/DefaultCatalog.json<br/>(резерв)"] --> Loader
  subgraph Core["CarDiagnosis.Core (asmdef, без MonoBehaviour)"]
    Loader[CatalogLoader] --> Validator[CatalogValidator]
    Flow[OrderFlow<br/>состояние смены] --> Rules[DiagnosisRules]
    Data[CatalogData<br/>формат]
  end
  subgraph Runtime["Assembly-CSharp (Diagnoz)"]
    Session[BatteryOrderSession<br/>читает файл, узлы сцены, шрифт] --> Loader
    Session --> Flow
    UI[BatteryOrderUI<br/>показ + команды] -->|TryCheck / TryRepair / TryComplete / TryNext / TryRetry / Begin / Restart| Flow
    Flow -->|Changed| UI
    Interaction[InteractionController] -->|SelectionChanged| UI
    Interaction --> Blocker[UiInputBlocker]
    Orbit[OrbitCamera] --> Blocker
    Node[DiagnosticNode.Id] --> Session
  end
  Builder[Editor: PrototypeSceneBuilder<br/>не изменён] -.Configure / Build.-> Session
  Builder -.-> UI
  Tests[Tests: CarDiagnosis.Core.Tests] --> Core
```

Кто за что отвечает:
- `CatalogData` — только поля формата JSON.
- `CatalogValidator` — полная проверка каталога; возвращает список ошибок, ничего не меняет.
- `CatalogLoader` — разбор основного файла → проверка → либо основной целиком, либо резервный целиком (тоже после проверки), либо ничего.
- `DiagnosisRules` — без состояния: какой текст покажет проверка, верен ли ремонт, допустим ли он на узле, минимальный путь.
- `OrderFlow` — единственное место, где меняется состояние: время, ремонты, контроль, исход, попытки, фазы Intro/Order/Final.
- `BatteryOrderSession` — MonoBehaviour-связка: путь к файлу, `Resources`, список узлов сцены, проверка глифов шрифта, логирование.
- `BatteryOrderUI` — строит кнопки из каталога и показывает состояние; правил не содержит (доступность кнопок спрашивает у `OrderFlow`).

## Поток игры
```mermaid
flowchart LR
  Start([Play]) --> Load{Каталог верен?}
  Load -- да --> Intro[Стартовое окно]
  Load -- нет --> FB{Резерв верен?}
  FB -- да --> Intro2[Стартовое окно<br/>+ «Резервный каталог»] --> Order
  FB -- нет --> Fatal[«Каталог не загружен»<br/>Выход]
  Intro --> Order[Заказ N]
  Order --> Check[Проверка узла] --> Order
  Order --> Repair[Ремонт узла] --> Control[Контрольная проверка<br/>этого узла] --> Done{Завершить}
  Done -- причина устранена --> Win[«Заказ выполнен»]
  Done -- нет --> Back[«Клиент вернулся»]
  Order -. время меньше минимума .-> Late[«Время вышло»]
  Back --> Retry[Повтор: сброс заказа] --> Order
  Late --> Retry
  Win -- есть следующий --> Order
  Win -- последний --> Final[Итоги смены] --> Restart[Начать заново] --> Intro
```

## Схема данных
```mermaid
erDiagram
  ORDER }o--|| FAULT : faultId
  FAULT }o--|| REPAIR : fixedBy
  FAULT ||--o{ OBSERVATION : observations
  OBSERVATION }o--|| CHECK : checkId
  OBSERVATION }o--|| NODE : nodeId
  REPAIR }o--o{ NODE : nodeIds
  CHECK }o--o{ NODE : "nodeIds (пусто = все)"
  BASELINE }o--|| CHECK : checkId
  BASELINE }o--|| NODE : nodeId
```

Выбор наблюдения: если причина заказа ещё не устранена — `fault.observations`; после верного ремонта — `fixedHeadline/fixedText`
этой записи; иначе нормальная картина `baseline`; иначе нейтральный текст проверки (`{node}` → имя узла).

## Что сознательно не сделано
- Нет универсального движка механик: новые узлы сцены и новые типы взаимодействий требуют Unity и C#.
- Нет симуляции электросистемы, разряда во времени, ожидания; наблюдения — заданные тексты.
- Нет сохранений, магазина, экономики, новых машин.
