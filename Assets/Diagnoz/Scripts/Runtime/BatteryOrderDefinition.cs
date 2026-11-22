using UnityEngine;
namespace Diagnoz
{
    [CreateAssetMenu(menuName = "Diagnoz/Battery Order", fileName = "BatteryOrder")]
    public sealed class BatteryOrderDefinition : ScriptableObject
    {
        [SerializeField] private string orderTitle = "Машина не заводится";
        [SerializeField, TextArea(3,6)] private string complaint =
            "При повороте ключа стартер еле крутит, двигатель не запускается. Посмотрите, пожалуйста, в чём дело";
        [SerializeField, Min(0)] private float initialVoltage = 10.5f, replacementVoltage = 12.6f;
        [SerializeField, TextArea(2,5)] private string instrumentHint =
            "Сравните показание с ориентиром учебного заказа. Это условная игровая модель: " +
            "одно измерение не является универсальной диагностикой настоящего автомобиля.";
        public string OrderTitle => orderTitle;
        public string Complaint => complaint;
        public float InitialVoltage => initialVoltage;
        public float ReplacementVoltage => replacementVoltage;
        public string InstrumentHint => instrumentHint;
    }
}
