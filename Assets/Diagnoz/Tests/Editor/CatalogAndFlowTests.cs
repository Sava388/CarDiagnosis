using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CarDiagnosis.Core.Tests
{
    // EditMode-тесты правил. Запуск: Window > General > Test Runner > EditMode > Run All.
    public sealed class CatalogAndFlowTests
    {
        private static readonly string[] SceneNodes = { "battery", "alternator", "engine", "brakes" };
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string StreamingPath => Path.Combine(Application.dataPath, "StreamingAssets", "CarDiagnosis", "orders.json");
        private static string FallbackPath => Path.Combine(Application.dataPath, "Diagnoz", "Resources", "CarDiagnosis", "DefaultCatalog.json");
        private static string ExtendedPath => Path.Combine(ProjectRoot, "CatalogExamples", "orders_extended.json");

        private static string BaseJson => File.ReadAllText(StreamingPath);
        private static CatalogData Base()
        {
            var errors = new List<string>();
            CatalogData c = CatalogLoader.Parse(BaseJson, "orders.json", errors);
            Assert.IsEmpty(errors);
            return c;
        }

        private static OrderFlow FlowAt(CatalogData c, string orderId)
        {
            var f = new OrderFlow(c);
            f.Begin();
            while (f.Current.id != orderId)
            {
                SolveCurrent(f);
                Assert.IsTrue(f.TryNext());
            }
            return f;
        }

        private static string CompatPath => Path.Combine(ProjectRoot, "CatalogExamples", "orders_rc1_compat.json");
        private static string TireFirstPath => Path.Combine(ProjectRoot, "CatalogExamples", "orders_tire_first.json");

        // Полный верный путь шиномонтажа для одного колеса.
        private static void SolveTire(OrderFlow f)
        {
            TireServiceDef d = f.TireDef;
            Assert.IsNotNull(f.TryTire(TireAction.Remove));
            Assert.IsNotNull(f.TryTire(TireAction.PlaceOnChanger));
            Assert.IsNotNull(f.TryTire(TireAction.Dismount));
            Assert.IsNotNull(f.TryTire(TireAction.Mount));
            Assert.IsNotNull(f.TryTire(TireAction.TakeFromChanger));
            Assert.IsNotNull(f.TryTire(TireAction.PlaceOnBalancer));
            Assert.IsNotNull(f.TryTire(TireAction.Spin));
            Assert.AreEqual(d.imbalanceGrams, f.State.Tire.LastReading);
            Assert.IsNotNull(f.TryTire(TireAction.ApplyWeight, d.imbalanceGrams));
            Assert.IsNotNull(f.TryTire(TireAction.Spin));
            Assert.IsTrue(f.State.Tire.Balanced);
            Assert.IsNotNull(f.TryTire(TireAction.TakeFromBalancer));
            Assert.IsNotNull(f.TryTire(TireAction.Install));
            Assert.IsNotNull(f.TryTire(TireAction.Control));
            Assert.IsTrue(f.TryComplete());
            Assert.AreEqual(OrderOutcome.Success, f.State.Outcome);
        }

        private static void SolveCurrent(OrderFlow f)
        {
            if (f.IsTireOrder) { SolveTire(f); return; }
            FaultDef fault = f.CurrentFault;
            RepairDef r = f.Repair(fault.fixedBy);
            string node = r.nodeIds[0];
            Assert.IsTrue(f.TryRepair(r.id, node));
            Assert.IsNotNull(f.TryCheck("multimeter", node));
            Assert.IsTrue(f.TryComplete());
            Assert.AreEqual(OrderOutcome.Success, f.State.Outcome);
        }

        // ---------- Каталоги ----------

        [Test] public void BaseCatalog_IsValid_FourDiagnosisAndOneTireOrder()
        {
            CatalogData c = Base();
            Assert.IsEmpty(CatalogValidator.Validate(c, SceneNodes));
            Assert.AreEqual(5, c.orders.Length);
            Assert.AreEqual(4, c.orders.Count(OrderTypes.IsDiagnosis));
            Assert.AreEqual(1, c.orders.Count(OrderTypes.IsTire));
        }

        [Test] public void Rc1Catalog_WithoutTypes_StillValidAndPlayable()
        {
            CatalogLoadResult r = CatalogLoader.Load(File.ReadAllText(CompatPath), "orders_rc1_compat.json", BaseJson, SceneNodes);
            Assert.AreEqual(CatalogSource.Primary, r.Source, string.Join("\n", r.PrimaryErrors));
            Assert.AreEqual(4, r.Catalog.orders.Length);
            var f = new OrderFlow(r.Catalog); f.Begin();
            for (int i = 0; i < 4; i++) { Assert.IsFalse(f.IsTireOrder); SolveCurrent(f); Assert.IsTrue(f.TryNext()); }
            Assert.AreEqual(FlowPhase.Final, f.Phase);
        }

        [Test] public void TireFirstCatalog_IsValid()
        {
            CatalogLoadResult r = CatalogLoader.Load(File.ReadAllText(TireFirstPath), "orders_tire_first.json", BaseJson, SceneNodes);
            Assert.AreEqual(CatalogSource.Primary, r.Source, string.Join("\n", r.PrimaryErrors));
            var f = new OrderFlow(r.Catalog); f.Begin();
            Assert.IsTrue(f.IsTireOrder);
            SolveCurrent(f); Assert.IsTrue(f.TryNext());
            Assert.IsFalse(f.IsTireOrder);
            Assert.IsNull(f.State.Tire, "Состояние колеса не переносится в следующий заказ");
        }

        [Test] public void InvalidExamples_AllRejected()
        {
            foreach (string file in Directory.GetFiles(Path.Combine(ProjectRoot, "CatalogExamples", "invalid"), "*.json"))
            {
                CatalogLoadResult r = CatalogLoader.Load(File.ReadAllText(file), Path.GetFileName(file), BaseJson, SceneNodes);
                Assert.AreEqual(CatalogSource.Fallback, r.Source, Path.GetFileName(file));
            }
        }

        [Test] public void FallbackCatalog_EqualsStreamingCatalog()
        {
            Assert.AreEqual(File.ReadAllText(StreamingPath), File.ReadAllText(FallbackPath));
        }

        [Test] public void ExtendedCatalog_IsValid_AndSixthOrderIsPlayable()
        {
            CatalogLoadResult r = CatalogLoader.Load(File.ReadAllText(ExtendedPath), "orders_extended.json", BaseJson, SceneNodes);
            Assert.AreEqual(CatalogSource.Primary, r.Source, string.Join("\n", r.PrimaryErrors));
            Assert.AreEqual(6, r.Catalog.orders.Length);
            OrderFlow f = FlowAt(r.Catalog, "order_06_long_parking");
            Assert.AreEqual(5, f.State.CaseIndex);
            SolveCurrent(f);
            Assert.IsTrue(f.TryNext());
            Assert.AreEqual(FlowPhase.Final, f.Phase);
        }

        // ---------- Ремонт и контроль ----------

        [Test] public void CorrectRepair_AfterControl_Success()
        {
            var f = new OrderFlow(Base()); f.Begin();
            Assert.AreEqual("10,5 В", f.TryCheck("multimeter", "battery").Headline);
            Assert.IsTrue(f.TryRepair("replace_battery", "battery"));
            Assert.IsFalse(f.CanComplete, "Без контрольной проверки завершать нельзя");
            Assert.AreEqual("12,6 В", f.TryCheck("multimeter", "battery").Headline);
            Assert.IsTrue(f.TryComplete());
            Assert.AreEqual(OrderOutcome.Success, f.State.Outcome);
            Assert.AreEqual(30, f.State.TimeSpent);
        }

        [Test] public void WrongRepair_AfterControl_ClientReturns()
        {
            var f = new OrderFlow(Base()); f.Begin();
            Assert.IsTrue(f.TryRepair("clean_terminals", "battery"));
            Assert.AreEqual("10,5 В", f.TryCheck("multimeter", "battery").Headline, "Неверный ремонт не устраняет причину");
            Assert.IsTrue(f.TryComplete());
            Assert.AreEqual(OrderOutcome.WrongFix, f.State.Outcome);
            Assert.IsFalse(f.TryNext(), "После неверного ремонта следующий заказ закрыт");
        }

        [Test] public void ControlCheck_OnOtherNode_DoesNotCount()
        {
            var f = new OrderFlow(Base()); f.Begin();
            f.TryRepair("replace_battery", "battery");
            f.TryCheck("multimeter", "brakes");
            Assert.IsFalse(f.CanComplete);
            f.TryCheck("inspect_contacts", "battery");
            Assert.IsTrue(f.CanComplete);
        }

        [Test] public void Repair_NotAllowed_OnWrongNode()
        {
            var f = new OrderFlow(Base()); f.Begin();
            Assert.IsFalse(f.TryRepair("replace_battery", "engine"));
            Assert.IsFalse(f.TryRepair("replace_battery", null));
            Assert.AreEqual(0, f.State.TimeSpent);
        }

        [Test] public void Repair_ClearsPreviousNotes()
        {
            var f = new OrderFlow(Base()); f.Begin();
            f.TryCheck("multimeter", "battery");
            Assert.AreEqual(1, f.State.Notes.Count);
            f.TryRepair("replace_battery", "battery");
            Assert.AreEqual(0, f.State.Notes.Count);
        }

        [Test] public void ChargingOrder_BatteryReplacementDoesNotHelp_AlternatorDoes()
        {
            CatalogData c = Base();
            OrderFlow f = FlowAt(c, "order_03_charge_lamp");
            Assert.AreEqual("12,2 В", f.TryCheck("multimeter", "battery").Headline);
            Assert.AreEqual("12,3 В", f.TryCheck("multimeter", "alternator").Headline);
            Assert.IsTrue(f.TryRepair("replace_battery", "battery"));
            Assert.AreEqual("12,2 В", f.TryCheck("multimeter", "battery").Headline);
            f.TryComplete();
            Assert.AreEqual(OrderOutcome.WrongFix, f.State.Outcome);
            Assert.IsTrue(f.TryRetry());
            Assert.IsTrue(f.TryRepair("replace_alternator", "alternator"));
            Assert.AreEqual("14,1 В", f.TryCheck("multimeter", "alternator").Headline);
            Assert.IsTrue(f.TryComplete());
            Assert.AreEqual(OrderOutcome.Success, f.State.Outcome);
        }

        [Test] public void GroundOrder_BatteryLooksNormal_EngineShowsCause()
        {
            OrderFlow f = FlowAt(Base(), "order_04_click_again");
            Assert.AreEqual("12,6 В", f.TryCheck("multimeter", "battery").Headline);
            Assert.AreEqual("Клеммы в порядке", f.TryCheck("inspect_contacts", "battery").Headline);
            Assert.AreEqual("Окисленная масса", f.TryCheck("inspect_contacts", "engine").Headline);
            Assert.IsTrue(f.TryRepair("clean_ground", "engine"));
            Assert.AreEqual("Масса в порядке", f.TryCheck("inspect_contacts", "engine").Headline);
            Assert.IsTrue(f.TryComplete());
            Assert.AreEqual(OrderOutcome.Success, f.State.Outcome);
        }

        // ---------- Время ----------

        [Test] public void TimeBoundary_ExactBudget_IsEnough()
        {
            CatalogData c = Base();
            c.orders[0].timeBudget = 25;   // замена 20 + контроль 5
            Assert.IsEmpty(CatalogValidator.Validate(c, SceneNodes));
            var f = new OrderFlow(c); f.Begin();
            Assert.IsTrue(f.TryRepair("replace_battery", "battery"));
            Assert.AreEqual(OrderOutcome.None, f.State.Outcome);
            Assert.IsNotNull(f.TryCheck("multimeter", "battery"));
            Assert.AreEqual(0, f.State.TimeLeft);
            Assert.IsTrue(f.TryComplete());
            Assert.AreEqual(OrderOutcome.Success, f.State.Outcome);
        }

        [Test] public void TimeInsufficient_OutOfTime_AndActionsBlocked()
        {
            CatalogData c = Base();
            c.orders[0].timeBudget = 25;
            var f = new OrderFlow(c); f.Begin();
            f.TryCheck("multimeter", "battery");          // осталось 20: ещё можно (15 + 5)
            Assert.AreEqual(OrderOutcome.None, f.State.Outcome);
            Assert.IsTrue(f.TryRepair("replace_battery", "battery")); // осталось 0 < 5
            Assert.AreEqual(OrderOutcome.OutOfTime, f.State.Outcome);
            Assert.IsNull(f.TryCheck("multimeter", "battery"));
            Assert.IsFalse(f.TryComplete());
        }

        [Test] public void Repair_Refused_WhenNotEnoughTime()
        {
            CatalogData c = Base();
            c.orders[0].timeBudget = 25;
            var f = new OrderFlow(c); f.Begin();
            f.TryCheck("multimeter", "battery");
            f.TryCheck("inspect_contacts", "battery");    // осталось 15 < 15+5: даже самый дешёвый ремонт с контролем не успеть
            Assert.AreEqual(OrderOutcome.OutOfTime, f.State.Outcome);
            Assert.IsFalse(f.CanRepair("replace_battery", "battery"));
        }

        // ---------- Повтор и переходы ----------

        [Test] public void Retry_ResetsOrderState()
        {
            var f = new OrderFlow(Base()); f.Begin();
            f.TryCheck("multimeter", "battery");
            f.TryRepair("clean_terminals", "battery");
            f.TryCheck("multimeter", "battery");
            f.TryComplete();
            Assert.IsTrue(f.TryRetry());
            Assert.AreEqual(50, f.State.TimeLeft);
            Assert.AreEqual(0, f.State.TimeSpent);
            Assert.AreEqual(0, f.State.Repairs);
            Assert.IsFalse(f.State.FaultFixed);
            Assert.IsFalse(f.State.AnyCheck);
            Assert.AreEqual(OrderOutcome.None, f.State.Outcome);
            Assert.AreEqual(0, f.State.Notes.Count);
            Assert.AreEqual("10,5 В", f.TryCheck("multimeter", "battery").Headline);
            Assert.AreEqual(2, f.Results[0].Attempts);
        }

        [Test] public void Retry_NotAllowed_WhileOrderActive()
        {
            var f = new OrderFlow(Base()); f.Begin();
            Assert.IsFalse(f.TryRetry());
        }

        [Test] public void Transitions_Intro_Orders_Final_Restart()
        {
            var f = new OrderFlow(Base());
            Assert.AreEqual(FlowPhase.Intro, f.Phase);
            Assert.IsNull(f.TryCheck("multimeter", "battery"), "До начала смены действия недоступны");
            f.Begin();
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(i, f.State.CaseIndex);
                Assert.IsFalse(f.TryNext(), "Переход без успеха запрещён");
                SolveCurrent(f);
                Assert.IsTrue(f.TryNext());
            }
            Assert.AreEqual(FlowPhase.Final, f.Phase);
            Assert.IsTrue(f.Results.All(r => r.Completed));
            f.Restart();
            Assert.AreEqual(FlowPhase.Intro, f.Phase);
            Assert.AreEqual(0, f.Results.Count);
            f.Begin();
            Assert.AreEqual(0, f.State.CaseIndex);
            Assert.AreEqual(50, f.State.TimeLeft);
        }

        // ---------- Шиномонтаж: одно колесо ----------

        private static OrderFlow TireFlow() => FlowAt(Base(), "order_05_tire_season");

        [Test] public void Tire_FullPath_Success_WithinBudget()
        {
            OrderFlow f = TireFlow();
            Assert.IsFalse(f.CanCheck("multimeter", "battery"), "В заказе шиномонтажа диагностика узлов недоступна");
            SolveTire(f);
            Assert.AreEqual(TireRules.MinimalTotal(f.TireDef), f.State.TimeSpent);
            Assert.IsTrue(f.State.Tire.NewTireOn);
        }

        [Test] public void Tire_WrongWeight_ThenCorrect()
        {
            OrderFlow f = TireFlow(); TireServiceDef d = f.TireDef;
            f.TryTire(TireAction.Remove); f.TryTire(TireAction.PlaceOnChanger);
            f.TryTire(TireAction.Dismount); f.TryTire(TireAction.Mount);
            f.TryTire(TireAction.TakeFromChanger); f.TryTire(TireAction.PlaceOnBalancer);
            Assert.IsNull(f.TryTire(TireAction.ApplyWeight, d.imbalanceGrams), "Груз до измерения запрещён");
            f.TryTire(TireAction.Spin);
            int wrong = d.weightOptions.First(w => w != d.imbalanceGrams);
            Assert.IsNotNull(f.TryTire(TireAction.ApplyWeight, wrong));
            Assert.IsNull(f.TryTire(TireAction.ApplyWeight, d.imbalanceGrams), "Второй груз поверх первого запрещён");
            f.TryTire(TireAction.Spin);
            Assert.IsFalse(f.State.Tire.Balanced);
            Assert.AreEqual(System.Math.Abs(d.imbalanceGrams - wrong), f.State.Tire.LastReading);
            Assert.IsNull(f.TryTire(TireAction.ApplyWeight, 999), "Несуществующий груз");
            Assert.IsNotNull(f.TryTire(TireAction.ApplyWeight, d.imbalanceGrams));
            f.TryTire(TireAction.Spin);
            Assert.IsTrue(f.State.Tire.Balanced);
            Assert.AreEqual(OrderOutcome.None, f.State.Outcome);
        }

        [Test] public void Tire_CannotComplete_WithWheelRemoved()
        {
            OrderFlow f = TireFlow();
            Assert.IsFalse(f.CanComplete);
            f.TryTire(TireAction.Remove);
            Assert.AreEqual(WheelPlace.Hands, f.State.Tire.Place);
            Assert.IsFalse(f.TryComplete());
            Assert.IsNull(f.TryTire(TireAction.Control));
            Assert.IsNull(f.TryTire(TireAction.Remove), "Нельзя снять колесо второй раз");
        }

        [Test] public void Tire_BalancerBeforeNewTire_Refused()
        {
            OrderFlow f = TireFlow();
            f.TryTire(TireAction.Remove);
            Assert.IsNotNull(f.TireRefusal(TireAction.PlaceOnBalancer));
            Assert.IsNull(f.TryTire(TireAction.PlaceOnBalancer));
            f.TryTire(TireAction.PlaceOnChanger);
            Assert.IsNull(f.TryTire(TireAction.Mount), "Монтаж до демонтажа старой шины запрещён");
            Assert.IsNull(f.TryTire(TireAction.Spin), "Балансировка не на балансировочном станке");
        }

        [Test] public void Tire_InstallWithoutService_ClientReturns()
        {
            OrderFlow f = TireFlow();
            f.TryTire(TireAction.Remove); f.TryTire(TireAction.Install); f.TryTire(TireAction.Control);
            Assert.IsTrue(f.TryComplete());
            Assert.AreEqual(OrderOutcome.WrongFix, f.State.Outcome);
        }

        [Test] public void Tire_RestartMidCarry_WheelBackOnCar()
        {
            OrderFlow f = TireFlow();
            f.TryTire(TireAction.Remove); f.TryTire(TireAction.PlaceOnChanger); f.TryTire(TireAction.Dismount);
            f.TryTire(TireAction.TakeFromChanger);
            Assert.IsTrue(f.State.Tire.Carried);
            Assert.IsTrue(f.TryRestartCurrent());
            TireJob j = f.State.Tire;
            Assert.AreEqual(WheelPlace.Car, j.Place);
            Assert.IsTrue(j.Installed);
            Assert.IsFalse(j.OldTireOff || j.NewTireOn || j.Measured || j.Balanced || j.Controlled || j.EverRemoved);
            Assert.AreEqual(f.Current.timeBudget, f.State.TimeLeft);
        }

        [Test] public void Tire_RetryAfterWrongResult_ResetsWheel()
        {
            OrderFlow f = TireFlow();
            f.TryTire(TireAction.Remove); f.TryTire(TireAction.Install); f.TryTire(TireAction.Control); f.TryComplete();
            Assert.IsTrue(f.TryRetry());
            Assert.AreEqual(WheelPlace.Car, f.State.Tire.Place);
            Assert.IsFalse(f.State.Tire.EverRemoved);
            Assert.AreEqual(OrderOutcome.None, f.State.Outcome);
        }

        [Test] public void Tire_OutOfTime_WhenWastingMinutes()
        {
            OrderFlow f = TireFlow(); TireServiceDef d = f.TireDef;
            f.TryTire(TireAction.Remove); f.TryTire(TireAction.PlaceOnChanger);
            f.TryTire(TireAction.Dismount); f.TryTire(TireAction.Mount);
            f.TryTire(TireAction.TakeFromChanger); f.TryTire(TireAction.PlaceOnBalancer);
            f.TryTire(TireAction.Spin);
            int wrong = d.weightOptions.First(w => w != d.imbalanceGrams);
            for (int i = 0; i < 20 && f.State.Outcome == OrderOutcome.None; i++)
            { f.TryTire(TireAction.ApplyWeight, wrong); f.TryTire(TireAction.Spin); }
            Assert.AreEqual(OrderOutcome.OutOfTime, f.State.Outcome);
            Assert.IsNull(f.TryTire(TireAction.Spin), "После нехватки времени действия заблокированы");
        }

        [Test] public void Tire_NotPossibleInDiagnosisOrder()
        {
            var f = new OrderFlow(Base()); f.Begin();
            Assert.IsFalse(f.IsTireOrder);
            Assert.IsNull(f.State.Tire);
            Assert.IsNotNull(f.TireRefusal(TireAction.Remove));
        }

        [Test] public void Tire_TimeBudgetBelowMinimal_UsesFallback() =>
            AssertFallback(LoadModified(c => c.orders[4].timeBudget = TireRules.MinimalTotal(c.tireService) - 1), "минимального пути");

        [Test] public void Tire_ImbalanceNotInOptions_UsesFallback() =>
            AssertFallback(LoadModified(c => c.tireService.imbalanceGrams = 40), "imbalanceGrams");

        // ---------- Недопустимые данные ----------

        private static CatalogLoadResult LoadModified(System.Action<CatalogData> change)
        {
            CatalogData c = Base(); change(c);
            return CatalogLoader.Load(JsonUtility.ToJson(c), "orders.json", BaseJson, SceneNodes);
        }

        private static void AssertFallback(CatalogLoadResult r, string expectedFragment)
        {
            Assert.AreEqual(CatalogSource.Fallback, r.Source);
            Assert.IsTrue(r.PrimaryErrors.Any(e => e.Contains(expectedFragment)),
                "Ожидалась ошибка «" + expectedFragment + "», есть:\n" + string.Join("\n", r.PrimaryErrors));
            Assert.AreEqual(5, r.Catalog.orders.Length);
            Assert.AreEqual("order_01_no_start", r.Catalog.orders[0].id);
        }

        [Test] public void MissingFile_UsesFallback() => AssertFallback(CatalogLoader.Load(null, "orders.json", BaseJson, SceneNodes), "не найден");
        [Test] public void EmptyFile_UsesFallback() => AssertFallback(CatalogLoader.Load("  ", "orders.json", BaseJson, SceneNodes), "пуст");
        [Test] public void BrokenJson_UsesFallback() => AssertFallback(CatalogLoader.Load("{ \"orders\": [ {", "orders.json", BaseJson, SceneNodes), "повреждён");
        [Test] public void EmptyCatalog_UsesFallback() => AssertFallback(CatalogLoader.Load("{}", "orders.json", BaseJson, SceneNodes), "formatVersion");

        [Test] public void DuplicateId_UsesFallback() =>
            AssertFallback(LoadModified(c => c.orders[1].id = c.orders[0].id), "повторяющийся id");

        [Test] public void MissingFaultReference_UsesFallback() =>
            AssertFallback(LoadModified(c => c.orders[0].faultId = "no_such_fault"), "не найдена");

        [Test] public void MissingNodeReference_UsesFallback() =>
            AssertFallback(LoadModified(c => c.repairs[0].nodeIds = new[] { "trunk" }), "«trunk»");

        [Test] public void InvalidCost_UsesFallback() =>
            AssertFallback(LoadModified(c => c.checks[0].cost = 0), "cost");

        [Test] public void InvalidTimeBudget_UsesFallback() =>
            AssertFallback(LoadModified(c => c.orders[2].timeBudget = -5), "timeBudget");

        [Test] public void BudgetBelowMinimalPath_UsesFallback() =>
            AssertFallback(LoadModified(c => c.orders[0].timeBudget = 24), "минимального пути");

        [Test] public void InvalidPrimary_IsNotPartiallyApplied()
        {
            CatalogLoadResult r = LoadModified(c => { c.orders[0].title = "ИЗМЕНЕНО"; c.orders[3].faultId = "bad"; });
            Assert.AreEqual(CatalogSource.Fallback, r.Source);
            Assert.AreEqual("Машина не заводится", r.Catalog.orders[0].title);
        }

        [Test] public void NodeMissingInScene_IsError()
        {
            List<string> e = CatalogValidator.Validate(Base(), new[] { "battery", "alternator", "brakes" });
            Assert.IsTrue(e.Any(x => x.Contains("engine")));
        }

        [Test] public void MissingGlyph_IsError()
        {
            CatalogData c = Base();
            c.orders[0].title = "Машина → не заводится";
            List<string> e = CatalogValidator.Validate(c, SceneNodes, t => t.Contains("→") ? "→" : null);
            Assert.IsTrue(e.Any(x => x.Contains("→")));
        }

        [Test] public void BothCatalogsInvalid_NoCatalog()
        {
            CatalogLoadResult r = CatalogLoader.Load("{}", "orders.json", "", SceneNodes);
            Assert.AreEqual(CatalogSource.None, r.Source);
            Assert.IsNull(r.Catalog);
            Assert.IsNotEmpty(r.FallbackErrors);
        }
    }
}
