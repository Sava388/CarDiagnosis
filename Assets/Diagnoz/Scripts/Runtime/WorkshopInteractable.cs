using UnityEngine;

namespace Diagnoz
{
    public enum WorkshopPoint { InspectCar, WheelSlot, TireChanger, Balancer }

    // Точка взаимодействия (триггер-коллайдер). Что доступно, решают OrderFlow и BatteryOrderUI.
    public sealed class WorkshopInteractable : MonoBehaviour
    {
        [SerializeField] private WorkshopPoint point;
        public WorkshopPoint Point => point;
        public void Configure(WorkshopPoint value) { point = value; }
    }
}
