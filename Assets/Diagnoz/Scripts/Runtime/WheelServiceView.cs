using CarDiagnosis.Core;
using TMPro;
using UnityEngine;

namespace Diagnoz
{
    // Показывает состояние колеса из OrderFlow. Объект колеса один; он только переставляется между точками,
    // поэтому дублирование и «колесо в двух местах» невозможны. После повтора/новой смены состояние новое.
    public sealed class WheelServiceView : MonoBehaviour
    {
        [SerializeField] private BatteryOrderSession session;
        [SerializeField] private Transform wheel, carSlot, handsSlot, changerSlot, balancerSlot;
        [SerializeField] private GameObject summerTire, winterTire, axleStand;
        [SerializeField] private Transform changerTable, balancerShaft;
        [SerializeField] private TMP_Text balancerReadout, tireTag;
        private float changerTimer, balancerTimer;

        public void Configure(BatteryOrderSession s, Transform wheelRoot, Transform car, Transform hands, Transform changer,
            Transform balancer, GameObject summer, GameObject winter, GameObject stand, Transform table, Transform shaft,
            TMP_Text readout, TMP_Text tag)
        {
            session = s; wheel = wheelRoot; carSlot = car; handsSlot = hands; changerSlot = changer; balancerSlot = balancer;
            summerTire = summer; winterTire = winter; axleStand = stand; changerTable = table; balancerShaft = shaft;
            balancerReadout = readout; tireTag = tag;
        }

        private void Start() { if (session != null) session.Changed += Sync; Sync(); }
        private void OnDestroy() { if (session != null) session.Changed -= Sync; }

        public void Sync()
        {
            if (wheel == null) return;
            OrderFlow flow = session != null ? session.Flow : null;
            TireJob job = flow != null ? flow.State.Tire : null;
            WheelPlace place = job != null ? job.Place : WheelPlace.Car;
            Transform slot = place == WheelPlace.Hands ? handsSlot : place == WheelPlace.Changer ? changerSlot :
                place == WheelPlace.Balancer ? balancerSlot : carSlot;
            if (slot == null) slot = carSlot;
            wheel.SetParent(slot, false);
            wheel.localPosition = Vector3.zero; wheel.localRotation = Quaternion.identity;
            bool winter = job != null && job.NewTireOn;
            bool bare = job != null && job.OldTireOff && !job.NewTireOn;
            if (summerTire != null) summerTire.SetActive(!winter && !bare);
            if (winterTire != null) winterTire.SetActive(winter);
            if (axleStand != null) axleStand.SetActive(place != WheelPlace.Car);
            if (balancerTimer <= 0 && balancerReadout != null)
                balancerReadout.text = place != WheelPlace.Balancer || job == null ? "" :
                    job.LastReading < 0 ? "--- г" : job.LastReading == 0 ? "0 г  OK" : job.LastReading + " г";
            if (tireTag != null)
                tireTag.text = job == null || flow.TireDef == null ? "" : "Шина: " + TireRules.TireLabel(job, flow.TireDef);
        }

        public void PlayAction(TireAction action)
        {
            if (action == TireAction.Dismount || action == TireAction.Mount) changerTimer = 1.6f;
            if (action == TireAction.Spin)
            {
                balancerTimer = 2.2f;
                if (balancerReadout != null) balancerReadout.text = "ПРОГОН";
            }
        }

        private void Update()
        {
            if (changerTimer > 0 && changerTable != null)
            { changerTimer -= Time.deltaTime; changerTable.Rotate(0, 200f * Time.deltaTime, 0, Space.Self); }
            if (balancerTimer > 0 && balancerShaft != null)
            {
                balancerTimer -= Time.deltaTime;
                float speed = Mathf.Clamp01(Mathf.Min(2.2f - balancerTimer, balancerTimer) * 2f) * 900f;
                balancerShaft.Rotate(speed * Time.deltaTime, 0, 0, Space.Self);
                if (balancerTimer <= 0) Sync();
            }
        }
    }
}
