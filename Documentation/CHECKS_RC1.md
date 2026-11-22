# Проверки 1.0 RC1

Дата: 20.11.2026. Выполнил ассистент без Unity, .NET и компилятора C#.

## Выполнено (ассистентом, статически)
| Проверка | Чем | Результат |
|---|---|---|
| Синтаксический разбор всех 15 .cs в Assets | tree-sitter-c-sharp (`syntax_check.py`) | 0 ошибок разбора. **Это не компиляция**: типы, перегрузки и API Unity не проверены |
| Каталоги: основной, резервный, исходный, расширенный | Python-зеркало правил проверки (`py_mirror_check.py`) | 0 ошибок; резервный побайтно совпадает с основным |
| Прохождение всех заказов верным путём и отказ неверного ремонта + сброс при повторе | Python-зеркало `OrderFlow` | Все заказы проходятся в бюджете, неверный ремонт даёт «Клиент вернулся» |
| 6 ошибочных каталогов (`CatalogExamples/invalid`) | Python-зеркало | Все отклоняются |
| Глифы шрифта во всех текстах каталогов | Python, набор символов из генератора | Недостающих символов нет |
| Раскладка UI при 6/7 действиях, 1280×720 и 1920×1080 | Арифметика прямоугольников (`layout_check.py`) | Пересечений нет, всё в пределах холста. **Это не рендер** |
| Генератор сцены не изменён | SHA-256 | `bd2130a5d4be098c2acc605fee41ca76ed1d32727476adb417872f793ab1987c` — совпадает |
| .meta у всех файлов и папок Assets, уникальность GUID, нет «сирот» | Python | В порядке; GUID прежних скриптов не менялись |

Python-проверки — модель на Python, повторяющая логику C#. Они **не являются** запуском C#-кода и не доказывают, что C# ведёт себя так же.

## Не выполнено (нужен Unity — делает пользователь)
- Компиляция проекта в Unity 6000.3.28f1.
- 30 EditMode-тестов `CarDiagnosis.Core.Tests` — **не запускались**.
- Создание сцены генератором с новым интерфейсом, Play, внешний вид окон и кнопок, чтение файла из StreamingAssets.
- Windows-сборка и путь `_Data/StreamingAssets` в ней.

## Места риска, которые стоит проверить первыми
1. `TMP_FontAsset.HasCharacters(string, out List<char>)` в `BatteryOrderSession` — перегрузка API TextMeshPro (ugui 2.0) не проверена компилятором.
2. `FindObjectsByType(FindObjectsInactive, FindObjectsSortMode)` — в новых версиях Unity может давать предупреждение об устаревании (не ошибку).
3. `JsonUtility.FromJson` для повреждённого JSON — ожидается исключение, тест `BrokenJson_UsesFallback`.
4. Кнопки проверок/ремонтов создаются только в Play; в режиме редактирования правой панели они не видны — это ожидаемо.
5. Подписи кнопок длиннее примерно 27 символов уменьшаются автоподбором шрифта до 82 %.

## Вывод Python-проверок
```
[CatalogExamples/invalid/bad_cost_and_time.json] ошибок 2: ['repair cost clean_terminals', 'budget order_04_click_again 15 < 20'] -> fallback
[CatalogExamples/invalid/broken_json.json] повреждён -> fallback (JSONDecodeError)
[CatalogExamples/invalid/duplicate_order_id.json] ошибок 1: ['orders: дубль order_01_no_start'] -> fallback
[CatalogExamples/invalid/empty_catalog.json] ошибок 1: ['empty'] -> fallback
[CatalogExamples/invalid/empty_file.json] пуст -> fallback
[CatalogExamples/invalid/missing_reference.json] ошибок 2: ['repair node ref trunk', 'order fault ref no_such_fault'] -> fallback
[CatalogExamples/orders_base.json] ошибок 0 []
    order_01_no_start: наблюдения ['10,5 В']; ремонт replace_battery@battery; контроль -> 12,6 В; потрачено 30/50
    order_02_intermittent: наблюдения ['Налёт на клеммах']; ремонт clean_terminals@battery; контроль -> 12,6 В; потрачено 25/50
    order_03_charge_lamp: наблюдения ['12,2 В', '12,3 В', 'Внешне в порядке']; ремонт replace_alternator@alternator; контроль -> 14,1 В; потрачено 45/50
    order_04_click_again: наблюдения ['Окисленная масса', 'Показания скачут']; ремонт clean_ground@engine; контроль -> Стабильно; потрачено 30/45
    order_01_no_start: неверный clean_terminals -> WrongFix, повтор сбросил состояние
    order_02_intermittent: неверный replace_battery -> WrongFix, повтор сбросил состояние
    order_03_charge_lamp: неверный replace_battery -> WrongFix, повтор сбросил состояние
    order_04_click_again: неверный replace_battery -> WrongFix, повтор сбросил состояние
[CatalogExamples/orders_extended.json] ошибок 0 []
    order_01_no_start: наблюдения ['10,5 В']; ремонт replace_battery@battery; контроль -> 12,6 В; потрачено 30/50
    order_02_intermittent: наблюдения ['Налёт на клеммах']; ремонт clean_terminals@battery; контроль -> 12,6 В; потрачено 25/50
    order_03_charge_lamp: наблюдения ['12,2 В', '12,3 В', 'Внешне в порядке']; ремонт replace_alternator@alternator; контроль -> 14,1 В; потрачено 45/50
    order_04_click_again: наблюдения ['Окисленная масса', 'Показания скачут']; ремонт clean_ground@engine; контроль -> Стабильно; потрачено 30/45
    order_05_long_parking: наблюдения ['10,5 В']; ремонт replace_battery@battery; контроль -> 12,6 В; потрачено 30/35
    order_01_no_start: неверный clean_terminals -> WrongFix, повтор сбросил состояние
    order_02_intermittent: неверный replace_battery -> WrongFix, повтор сбросил состояние
    order_03_charge_lamp: неверный replace_battery -> WrongFix, повтор сбросил состояние
    order_04_click_again: неверный replace_battery -> WrongFix, повтор сбросил состояние
    order_05_long_parking: неверный clean_terminals -> WrongFix, повтор сбросил состояние
[Assets/StreamingAssets/CarDiagnosis/orders.json] ошибок 0 []
    order_01_no_start: наблюдения ['10,5 В']; ремонт replace_battery@battery; контроль -> 12,6 В; потрачено 30/50
    order_02_intermittent: наблюдения ['Налёт на клеммах']; ремонт clean_terminals@battery; контроль -> 12,6 В; потрачено 25/50
    order_03_charge_lamp: наблюдения ['12,2 В', '12,3 В', 'Внешне в порядке']; ремонт replace_alternator@alternator; контроль -> 14,1 В; потрачено 45/50
    order_04_click_again: наблюдения ['Окисленная масса', 'Показания скачут']; ремонт clean_ground@engine; контроль -> Стабильно; потрачено 30/45
    order_01_no_start: неверный clean_terminals -> WrongFix, повтор сбросил состояние
    order_02_intermittent: неверный replace_battery -> WrongFix, повтор сбросил состояние
    order_03_charge_lamp: неверный replace_battery -> WrongFix, повтор сбросил состояние
    order_04_click_again: неверный replace_battery -> WrongFix, повтор сбросил состояние
[Assets/Diagnoz/Resources/CarDiagnosis/DefaultCatalog.json] ошибок 0 []
    order_01_no_start: наблюдения ['10,5 В']; ремонт replace_battery@battery; контроль -> 12,6 В; потрачено 30/50
    order_02_intermittent: наблюдения ['Налёт на клеммах']; ремонт clean_terminals@battery; контроль -> 12,6 В; потрачено 25/50
    order_03_charge_lamp: наблюдения ['12,2 В', '12,3 В', 'Внешне в порядке']; ремонт replace_alternator@alternator; контроль -> 14,1 В; потрачено 45/50
    order_04_click_again: наблюдения ['Окисленная масса', 'Показания скачут']; ремонт clean_ground@engine; контроль -> Стабильно; потрачено 30/45
    order_01_no_start: неверный clean_terminals -> WrongFix, повтор сбросил состояние
    order_02_intermittent: неверный replace_battery -> WrongFix, повтор сбросил состояние
    order_03_charge_lamp: неверный replace_battery -> WrongFix, повтор сбросил состояние
    order_04_click_again: неверный replace_battery -> WrongFix, повтор сбросил состояние
ИТОГ: OK

OK  Diagnoz/Editor/PrototypeSceneBuilder.cs []
OK  Diagnoz/Scripts/Core/CatalogData.cs []
OK  Diagnoz/Scripts/Core/CatalogLoader.cs []
OK  Diagnoz/Scripts/Core/CatalogValidator.cs []
OK  Diagnoz/Scripts/Core/DiagnosisRules.cs []
OK  Diagnoz/Scripts/Core/OrderFlow.cs []
OK  Diagnoz/Scripts/Runtime/BatteryOrderDefinition.cs []
OK  Diagnoz/Scripts/Runtime/BatteryOrderSession.cs []
OK  Diagnoz/Scripts/Runtime/BatteryOrderUI.cs []
OK  Diagnoz/Scripts/Runtime/DiagnosticNode.cs []
OK  Diagnoz/Scripts/Runtime/InteractionController.cs []
OK  Diagnoz/Scripts/Runtime/NodeSelectionPanel.cs []
OK  Diagnoz/Scripts/Runtime/OrbitCamera.cs []
OK  Diagnoz/Scripts/Runtime/UiInputBlocker.cs []
OK  Diagnoz/Tests/Editor/CatalogAndFlowTests.cs []
ошибок разбора: 0

(1280, 720): масштаб 0.889, холст 1440x810
(1920, 1080): масштаб 1.333, холст 1440x810
проверок 2, ремонтов 4: высота правой панели 649, низ 753/810; пересечений нет; в пределах холста: True
проверок 3, ремонтов 4: высота правой панели 693, низ 797/810; пересечений нет; в пределах холста: True
проверок 2, ремонтов 5: высота правой панели 693, низ 797/810; пересечений нет; в пределах холста: True
```
