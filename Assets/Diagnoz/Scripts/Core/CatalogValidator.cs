using System;
using System.Collections.Generic;

namespace CarDiagnosis.Core
{
    // Проверяет каталог целиком. Пустой список ошибок = каталог можно применять.
    public static class CatalogValidator
    {
        public const int SupportedFormat = 1;
        public const int MaxCost = 120, MaxBudget = 600, MaxOrders = 8, MaxChecks = 3, MaxRepairs = 5, MaxActions = 7;
        public const int MaxTitle = 48, MaxComplaint = 220, MaxLabel = 34, MaxHeadline = 40, MaxText = 220, MaxIntro = 320;

        // sceneNodeIds — узлы, реально найденные в сцене (null = не проверять).
        // missingGlyphs — возвращает символы, которых нет в шрифте (null = не проверять).
        public static List<string> Validate(CatalogData c, ICollection<string> sceneNodeIds = null,
            Func<string, string> missingGlyphs = null)
        {
            var e = new List<string>();
            if (c == null) { e.Add("Каталог пуст или не прочитан."); return e; }
            if (c.formatVersion != SupportedFormat)
                e.Add("formatVersion = " + c.formatVersion + ", поддерживается " + SupportedFormat + ".");

            CatalogSettings s = c.settings;
            if (s == null) e.Add("Нет раздела settings.");
            else
            {
                Text(e, "settings.gameTitle", s.gameTitle, MaxTitle, true, missingGlyphs);
                Text(e, "settings.subtitle", s.subtitle, MaxTitle, false, missingGlyphs);
                Text(e, "settings.introTitle", s.introTitle, MaxTitle, true, missingGlyphs);
                Text(e, "settings.introText", s.introText, MaxIntro, true, missingGlyphs);
                Text(e, "settings.controlsText", s.controlsText, 90, true, missingGlyphs);
                Text(e, "settings.disclaimer", s.disclaimer, 90, false, missingGlyphs);
                Text(e, "settings.finalTitle", s.finalTitle, MaxTitle, true, missingGlyphs);
                Text(e, "settings.finalText", s.finalText, MaxText, false, missingGlyphs);
            }

            var nodes = Ids(e, "nodes", c.nodes, n => n?.id);
            if (Len(c.nodes) == 0) e.Add("Список nodes пуст.");
            foreach (NodeDef n in c.nodes ?? new NodeDef[0])
            {
                if (n == null) continue;
                Text(e, "nodes[" + n.id + "].name", n.name, MaxTitle, true, missingGlyphs);
                if (sceneNodeIds != null && n.id != null && !sceneNodeIds.Contains(n.id))
                    e.Add("Узел «" + n.id + "» есть в каталоге, но не найден в сцене.");
            }

            var checks = Ids(e, "checks", c.checks, x => x?.id);
            if (Len(c.checks) == 0) e.Add("Список checks пуст.");
            if (Len(c.checks) > MaxChecks) e.Add("Проверок больше " + MaxChecks + ": не поместятся в интерфейс.");
            foreach (CheckDef k in c.checks ?? new CheckDef[0])
            {
                if (k == null) continue;
                string p = "checks[" + k.id + "]";
                Text(e, p + ".label", k.label, MaxLabel, true, missingGlyphs);
                Text(e, p + ".windowTitle", k.windowTitle, MaxTitle, true, missingGlyphs);
                Text(e, p + ".hint", k.hint, MaxText, false, missingGlyphs);
                Text(e, p + ".neutralHeadline", k.neutralHeadline, MaxHeadline, true, missingGlyphs);
                Text(e, p + ".neutralText", k.neutralText, MaxText, true, missingGlyphs);
                Cost(e, p + ".cost", k.cost);
                Refs(e, p + ".nodeIds", k.nodeIds, nodes, false);
            }

            var repairs = Ids(e, "repairs", c.repairs, x => x?.id);
            if (Len(c.repairs) == 0) e.Add("Список repairs пуст.");
            if (Len(c.repairs) > MaxRepairs) e.Add("Ремонтов больше " + MaxRepairs + ": не поместятся в интерфейс.");
            if (Len(c.checks) + Len(c.repairs) > MaxActions) e.Add("Всего действий больше " + MaxActions + ".");
            foreach (RepairDef r in c.repairs ?? new RepairDef[0])
            {
                if (r == null) continue;
                string p = "repairs[" + r.id + "]";
                Text(e, p + ".label", r.label, MaxLabel, true, missingGlyphs);
                Text(e, p + ".resultName", r.resultName, MaxTitle, true, missingGlyphs);
                Cost(e, p + ".cost", r.cost);
                Refs(e, p + ".nodeIds", r.nodeIds, nodes, true);
            }

            Observations(e, "baseline", c.baseline, c, checks, nodes, missingGlyphs, false);

            var faults = Ids(e, "faults", c.faults, x => x?.id);
            if (Len(c.faults) == 0) e.Add("Список faults пуст.");
            foreach (FaultDef f in c.faults ?? new FaultDef[0])
            {
                if (f == null) continue;
                string p = "faults[" + f.id + "]";
                if (string.IsNullOrEmpty(f.fixedBy) || !repairs.Contains(f.fixedBy))
                    e.Add(p + ".fixedBy: ремонт «" + f.fixedBy + "» не найден.");
                if (Len(f.observations) == 0) e.Add(p + ": нет наблюдений, причину нельзя найти.");
                Observations(e, p + ".observations", f.observations, c, checks, nodes, missingGlyphs, true);
            }

            Ids(e, "orders", c.orders, x => x?.id);
            bool anyTire = false;
            foreach (OrderDef o in c.orders ?? new OrderDef[0]) if (OrderTypes.IsTire(o)) anyTire = true;
            if (anyTire) TireService(e, c.tireService, missingGlyphs);
            if (Len(c.orders) == 0) e.Add("Список orders пуст.");
            if (Len(c.orders) > MaxOrders) e.Add("Заказов больше " + MaxOrders + ".");
            foreach (OrderDef o in c.orders ?? new OrderDef[0])
            {
                if (o == null) continue;
                string p = "orders[" + o.id + "]";
                Text(e, p + ".title", o.title, MaxTitle, true, missingGlyphs);
                Text(e, p + ".complaint", o.complaint, MaxComplaint, true, missingGlyphs);
                if (o.timeBudget < 1 || o.timeBudget > MaxBudget)
                    e.Add(p + ".timeBudget = " + o.timeBudget + ": допустимо 1-" + MaxBudget + ".");
                Text(e, p + ".goal", o.goal, 160, false, missingGlyphs);
                if (OrderTypes.IsTire(o))
                {
                    if (!string.IsNullOrEmpty(o.faultId)) e.Add(p + ": у заказа шиномонтажа faultId не задаётся.");
                    if (c.tireService != null && TireCostsValid(c.tireService))
                    {
                        int tireNeed = TireRules.MinimalTotal(c.tireService);
                        if (o.timeBudget >= 1 && o.timeBudget < tireNeed)
                            e.Add(p + ".timeBudget = " + o.timeBudget + " меньше минимального пути " + tireNeed + " мин.");
                    }
                    continue;
                }
                if (!OrderTypes.IsDiagnosis(o)) { e.Add(p + ".type: неизвестный тип «" + o.type + "»."); continue; }
                FaultDef f = Find(c.faults, x => x?.id == o.faultId);
                if (f == null) { e.Add(p + ".faultId: причина «" + o.faultId + "» не найдена."); continue; }
                int need = DiagnosisRules.MinimalSuccessCost(c, f);
                if (need < 0) e.Add(p + ": верный ремонт недостижим (нет узла или контрольной проверки).");
                else if (o.timeBudget >= 1 && o.timeBudget < need)
                    e.Add(p + ".timeBudget = " + o.timeBudget + " меньше минимального пути " + need + " мин.");
            }
            return e;
        }

        private static bool TireCostsValid(TireServiceDef t)
        {
            foreach (int v in new[] { t.removeCost, t.dismountCost, t.mountCost, t.spinCost, t.weightCost, t.installCost, t.controlCost })
                if (v < 1 || v > MaxCost) return false;
            return true;
        }

        private static void TireService(List<string> e, TireServiceDef t, Func<string, string> glyphs)
        {
            if (t == null) { e.Add("Есть заказ шиномонтажа, но нет раздела tireService."); return; }
            Text(e, "tireService.wheelName", t.wheelName, MaxHeadline, true, glyphs);
            Text(e, "tireService.fromTire", t.fromTire, 20, true, glyphs);
            Text(e, "tireService.toTire", t.toTire, 20, true, glyphs);
            Text(e, "tireService.imbalanceSide", t.imbalanceSide, MaxHeadline, true, glyphs);
            if (t.fromTire == t.toTire) e.Add("tireService: fromTire и toTire совпадают — смена шины не видна.");
            Cost(e, "tireService.removeCost", t.removeCost); Cost(e, "tireService.dismountCost", t.dismountCost);
            Cost(e, "tireService.mountCost", t.mountCost); Cost(e, "tireService.spinCost", t.spinCost);
            Cost(e, "tireService.weightCost", t.weightCost); Cost(e, "tireService.installCost", t.installCost);
            Cost(e, "tireService.controlCost", t.controlCost);
            int n = Len(t.weightOptions);
            if (n < 2 || n > 3) e.Add("tireService.weightOptions: нужно 2-3 варианта груза.");
            var seen = new HashSet<int>();
            foreach (int g in t.weightOptions ?? new int[0])
            {
                if (g < 5 || g > 200) e.Add("tireService.weightOptions: груз " + g + " г вне диапазона 5-200.");
                if (!seen.Add(g)) e.Add("tireService.weightOptions: повтор груза " + g + " г.");
            }
            if (!seen.Contains(t.imbalanceGrams)) e.Add("tireService.imbalanceGrams = " + t.imbalanceGrams + " нет среди weightOptions.");
        }

        private static void Observations(List<string> e, string p, ObservationDef[] list, CatalogData c,
            HashSet<string> checks, HashSet<string> nodes, Func<string, string> glyphs, bool fault)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < Len(list); i++)
            {
                ObservationDef o = list[i];
                string q = p + "[" + i + "]";
                if (o == null) { e.Add(q + ": пустая запись."); continue; }
                if (!checks.Contains(o.checkId ?? "")) e.Add(q + ".checkId: проверка «" + o.checkId + "» не найдена.");
                if (!nodes.Contains(o.nodeId ?? "")) e.Add(q + ".nodeId: узел «" + o.nodeId + "» не найден.");
                CheckDef k = Find(c.checks, x => x?.id == o.checkId);
                if (k != null && Len(k.nodeIds) > 0 && Array.IndexOf(k.nodeIds, o.nodeId) < 0)
                    e.Add(q + ": проверка «" + o.checkId + "» недоступна на узле «" + o.nodeId + "».");
                if (!seen.Add(o.checkId + "@" + o.nodeId)) e.Add(q + ": повтор пары " + o.checkId + "@" + o.nodeId + ".");
                Text(e, q + ".headline", o.headline, MaxHeadline, true, glyphs);
                Text(e, q + ".text", o.text, MaxText, true, glyphs);
                Text(e, q + ".fixedHeadline", o.fixedHeadline, MaxHeadline, false, glyphs);
                Text(e, q + ".fixedText", o.fixedText, MaxText, false, glyphs);
                if (fault && string.IsNullOrEmpty(o.fixedHeadline) != string.IsNullOrEmpty(o.fixedText))
                    e.Add(q + ": fixedHeadline и fixedText задаются вместе.");
            }
        }

        private static HashSet<string> Ids<T>(List<string> e, string p, T[] list, Func<T, string> id)
        {
            var set = new HashSet<string>();
            for (int i = 0; i < Len(list); i++)
            {
                if (list[i] == null) { e.Add(p + "[" + i + "]: пустая запись."); continue; }
                string v = id(list[i]);
                if (string.IsNullOrWhiteSpace(v)) e.Add(p + "[" + i + "]: пустой id.");
                else if (!set.Add(v)) e.Add(p + ": повторяющийся id «" + v + "».");
            }
            return set;
        }

        private static void Refs(List<string> e, string p, string[] ids, HashSet<string> known, bool required)
        {
            if (required && Len(ids) == 0) e.Add(p + ": нужен хотя бы один узел.");
            foreach (string id in ids ?? new string[0])
                if (!known.Contains(id ?? "")) e.Add(p + ": узел «" + id + "» не найден.");
        }

        private static void Cost(List<string> e, string p, int v)
        { if (v < 1 || v > MaxCost) e.Add(p + " = " + v + ": допустимо 1-" + MaxCost + " мин."); }

        private static void Text(List<string> e, string p, string v, int max, bool required, Func<string, string> glyphs)
        {
            if (string.IsNullOrWhiteSpace(v)) { if (required) e.Add(p + ": пустой текст."); return; }
            if (v.Length > max) e.Add(p + ": длина " + v.Length + " > " + max + ".");
            if (glyphs != null) { string m = glyphs(v); if (!string.IsNullOrEmpty(m)) e.Add(p + ": нет в шрифте символов «" + m + "»."); }
        }

        internal static int Len<T>(T[] a) => a == null ? 0 : a.Length;
        internal static T Find<T>(T[] a, Predicate<T> p) where T : class
        { if (a != null) foreach (T x in a) if (p(x)) return x; return null; }
    }
}
