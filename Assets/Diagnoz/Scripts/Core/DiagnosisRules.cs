using System;

namespace CarDiagnosis.Core
{
    public sealed class Observation
    {
        public CheckDef Check;
        public string NodeId;
        public string NodeName;
        public string Headline;
        public string Text;
        public bool Neutral;    // ничего важного: узел без отклонений
    }

    // Правила диагностики и ремонта. Без состояния и без Unity: удобно тестировать.
    public static class DiagnosisRules
    {
        public static bool Allowed(string[] nodeIds, string nodeId, bool emptyMeansAll)
        {
            if (string.IsNullOrEmpty(nodeId)) return false;
            if (nodeIds == null || nodeIds.Length == 0) return emptyMeansAll;
            return Array.IndexOf(nodeIds, nodeId) >= 0;
        }

        public static bool CheckAllowed(CheckDef check, string nodeId) => check != null && Allowed(check.nodeIds, nodeId, true);
        public static bool RepairAllowed(RepairDef repair, string nodeId) => repair != null && Allowed(repair.nodeIds, nodeId, false);
        public static bool IsCorrectRepair(FaultDef fault, string repairId) => fault != null && fault.fixedBy == repairId;

        public static string NodeName(CatalogData c, string nodeId)
        {
            NodeDef n = CatalogValidator.Find(c.nodes, x => x?.id == nodeId);
            return n != null ? n.name : nodeId;
        }

        // Что увидит игрок: сначала картина неисправности (пока она не устранена),
        // после верного ремонта — fixed-вариант, иначе нормальная картина узла, иначе нейтральный текст.
        public static Observation Observe(CatalogData c, FaultDef fault, bool faultFixed, string checkId, string nodeId)
        {
            CheckDef check = CatalogValidator.Find(c.checks, x => x?.id == checkId);
            if (check == null || !CheckAllowed(check, nodeId)) return null;
            var result = new Observation { Check = check, NodeId = nodeId, NodeName = NodeName(c, nodeId) };
            ObservationDef f = fault == null ? null :
                CatalogValidator.Find(fault.observations, x => x != null && x.checkId == checkId && x.nodeId == nodeId);
            ObservationDef b = CatalogValidator.Find(c.baseline, x => x != null && x.checkId == checkId && x.nodeId == nodeId);
            if (f != null && !faultFixed) { result.Headline = f.headline; result.Text = f.text; }
            else if (f != null && !string.IsNullOrEmpty(f.fixedHeadline)) { result.Headline = f.fixedHeadline; result.Text = f.fixedText; }
            else if (b != null) { result.Headline = b.headline; result.Text = b.text; }
            else { result.Headline = check.neutralHeadline; result.Text = check.neutralText; result.Neutral = true; }
            result.Text = (result.Text ?? "").Replace("{node}", result.NodeName);
            result.Headline = (result.Headline ?? "").Replace("{node}", result.NodeName);
            return result;
        }

        public static int MinCheckCost(CatalogData c)
        {
            int min = int.MaxValue;
            foreach (CheckDef k in c.checks ?? new CheckDef[0]) if (k != null && k.cost < min) min = k.cost;
            return min == int.MaxValue ? 0 : min;
        }

        public static int MinRepairCost(CatalogData c)
        {
            int min = int.MaxValue;
            foreach (RepairDef r in c.repairs ?? new RepairDef[0]) if (r != null && r.cost < min) min = r.cost;
            return min == int.MaxValue ? 0 : min;
        }

        // Самая дешёвая проверка, которую можно сделать на узле (для контроля после ремонта). -1 = нет.
        public static int CheapestCheckOn(CatalogData c, string nodeId)
        {
            int min = -1;
            foreach (CheckDef k in c.checks ?? new CheckDef[0])
                if (k != null && CheckAllowed(k, nodeId) && (min < 0 || k.cost < min)) min = k.cost;
            return min;
        }

        // Минимальное время верного прохождения: верный ремонт + контрольная проверка на том же узле.
        public static int MinimalSuccessCost(CatalogData c, FaultDef fault)
        {
            RepairDef r = fault == null ? null : CatalogValidator.Find(c.repairs, x => x?.id == fault.fixedBy);
            if (r == null || r.nodeIds == null) return -1;
            int best = -1;
            foreach (string node in r.nodeIds)
            {
                int k = CheapestCheckOn(c, node);
                if (k >= 0 && (best < 0 || r.cost + k < best)) best = r.cost + k;
            }
            return best;
        }

        // Сколько минут минимально нужно, чтобы ещё можно было закончить заказ.
        public static int TimeNeededToFinish(CatalogData c, bool anyRepairDone)
            => anyRepairDone ? MinCheckCost(c) : MinRepairCost(c) + MinCheckCost(c);
    }
}
