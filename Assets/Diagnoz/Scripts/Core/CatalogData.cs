using System;

namespace CarDiagnosis.Core
{
    // Формат файла orders.json (formatVersion = 1). Читается JsonUtility.
    // Упрощённая игровая модель. Не руководство по ремонту автомобиля.
    [Serializable]
    public sealed class CatalogData
    {
        public int formatVersion;
        public CatalogSettings settings;
        public NodeDef[] nodes;
        public CheckDef[] checks;
        public RepairDef[] repairs;
        public ObservationDef[] baseline;   // нормальная картина узлов, если неисправность её не меняет
        public FaultDef[] faults;
        public OrderDef[] orders;           // порядок в массиве = порядок прохождения
        public TireServiceDef tireService;  // нужен только если есть заказ type = "tire_service"
    }

    // Параметры сезонного шиномонтажа одного колеса. Упрощённая игровая модель, не инструкция.
    [Serializable]
    public sealed class TireServiceDef
    {
        public string wheelName;        // «Переднее левое колесо»
        public string fromTire;         // «летняя»
        public string toTire;           // «зимняя»
        public string imbalanceSide;    // «наружная сторона диска»
        public int removeCost, dismountCost, mountCost, spinCost, weightCost, installCost, controlCost;
        public int imbalanceGrams;      // что покажет первый прогон
        public int[] weightOptions;     // грузы на выбор; верный = imbalanceGrams
    }

    [Serializable]
    public sealed class CatalogSettings
    {
        public string gameTitle;
        public string subtitle;
        public string introTitle;
        public string introText;
        public string controlsText;
        public string disclaimer;
        public string finalTitle;
        public string finalText;
    }

    [Serializable]
    public sealed class NodeDef
    {
        public string id;     // совпадает с DiagnosticNode.Id в сцене
        public string name;
    }

    [Serializable]
    public sealed class CheckDef
    {
        public string id;
        public string label;          // подпись кнопки
        public int cost;              // игровые минуты
        public string windowTitle;
        public string hint;           // доп. строка в окне результата (может быть пустой)
        public string[] nodeIds;      // где доступна; пусто = на любом узле
        public string neutralHeadline;
        public string neutralText;    // {node} заменяется именем узла
    }

    [Serializable]
    public sealed class RepairDef
    {
        public string id;
        public string label;
        public int cost;
        public string resultName;     // для окна завершения: «Замена аккумулятора»
        public string[] nodeIds;      // где доступен; обязательно хотя бы один узел
    }

    [Serializable]
    public sealed class ObservationDef
    {
        public string checkId;
        public string nodeId;
        public string headline;       // крупная строка: «10,5 В», «Налёт на клеммах»
        public string text;
        public string fixedHeadline;  // после верного ремонта (если пусто — берётся baseline)
        public string fixedText;
    }

    [Serializable]
    public sealed class FaultDef
    {
        public string id;
        public string fixedBy;        // id ремонта, который устраняет причину
        public ObservationDef[] observations;
    }

    [Serializable]
    public sealed class OrderDef
    {
        public string id;
        public string title;
        public string complaint;
        public string faultId;          // для диагностики; у шиномонтажа пусто
        public int timeBudget;
        public string type;             // пусто или "diagnosis" — диагностика; "tire_service" — шиномонтаж
        public string goal;             // необязательная цель для окна заказа
    }

    public static class OrderTypes
    {
        public const string Diagnosis = "diagnosis", TireService = "tire_service";
        public static bool IsTire(OrderDef o) => o != null && o.type == TireService;
        public static bool IsDiagnosis(OrderDef o) => o != null && (string.IsNullOrEmpty(o.type) || o.type == Diagnosis);
    }
}
