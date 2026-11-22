using System;
using System.Collections.Generic;

namespace CarDiagnosis.Core
{
    public enum FlowPhase { Intro, Order, Final }
    public enum OrderOutcome { None, Success, WrongFix, OutOfTime }

    public sealed class OrderState
    {
        public int CaseIndex;
        public int TimeLeft, TimeSpent, Actions, Repairs;
        public bool FaultFixed, AnyCheck, CheckedSinceRepair;
        public string LastRepairId, LastRepairNode;
        public OrderOutcome Outcome;
        public readonly List<Observation> Notes = new List<Observation>();   // наблюдения после последнего ремонта
        public TireJob Tire;            // только в заказе шиномонтажа; новый объект на каждую попытку
        public string LastTireMessage;
        public bool Finished => Outcome != OrderOutcome.None;
    }

    public sealed class OrderResult
    {
        public string OrderId, Title;
        public int Attempts, TimeSpent, Budget, Actions;
        public bool Completed;
    }

    // Состояние смены и применение действий. Каталог должен быть заранее проверен CatalogValidator.
    public sealed class OrderFlow
    {
        public CatalogData Catalog { get; }
        public FlowPhase Phase { get; private set; } = FlowPhase.Intro;
        public OrderState State { get; private set; } = new OrderState();
        public IReadOnlyList<OrderResult> Results => results;
        public event Action Changed;
        private readonly List<OrderResult> results = new List<OrderResult>();

        public OrderFlow(CatalogData validatedCatalog)
        {
            Catalog = validatedCatalog ?? throw new ArgumentNullException(nameof(validatedCatalog));
            if (CatalogValidator.Len(Catalog.orders) == 0) throw new ArgumentException("В каталоге нет заказов.");
        }

        public int OrderCount => Catalog.orders.Length;
        public OrderDef Current => Phase == FlowPhase.Order ? Catalog.orders[State.CaseIndex] : null;
        public FaultDef CurrentFault => Current == null ? null : CatalogValidator.Find(Catalog.faults, f => f?.id == Current.faultId);
        public bool HasNextOrder => State.CaseIndex + 1 < OrderCount;
        public bool Active => Phase == FlowPhase.Order && !State.Finished;
        public bool IsTireOrder => OrderTypes.IsTire(Current);
        public TireServiceDef TireDef => Catalog.tireService;

        public CheckDef Check(string id) => CatalogValidator.Find(Catalog.checks, x => x?.id == id);
        public RepairDef Repair(string id) => CatalogValidator.Find(Catalog.repairs, x => x?.id == id);

        public void Begin()
        {
            results.Clear();
            foreach (OrderDef o in Catalog.orders)
                results.Add(new OrderResult { OrderId = o.id, Title = o.title, Budget = o.timeBudget });
            Phase = FlowPhase.Order;
            StartCase(0);
        }

        public void Restart()
        {
            results.Clear();
            Phase = FlowPhase.Intro;
            State = new OrderState();
            Changed?.Invoke();
        }

        public bool CanCheck(string checkId, string nodeId)
        {
            CheckDef k = Check(checkId);
            return Active && !IsTireOrder && DiagnosisRules.CheckAllowed(k, nodeId) && State.TimeLeft >= k.cost;
        }

        public Observation TryCheck(string checkId, string nodeId)
        {
            if (!CanCheck(checkId, nodeId)) return null;
            Observation o = DiagnosisRules.Observe(Catalog, CurrentFault, State.FaultFixed, checkId, nodeId);
            Spend(o.Check.cost);
            State.AnyCheck = true;
            State.Notes.RemoveAll(n => n.Check.id == checkId && n.NodeId == nodeId);
            State.Notes.Add(o);
            if (State.Repairs > 0 && nodeId == State.LastRepairNode) State.CheckedSinceRepair = true;
            AfterAction();
            return o;
        }

        public bool CanRepair(string repairId, string nodeId)
        {
            RepairDef r = Repair(repairId);
            return Active && !IsTireOrder && DiagnosisRules.RepairAllowed(r, nodeId) && State.TimeLeft >= r.cost;
        }

        public bool TryRepair(string repairId, string nodeId)
        {
            if (!CanRepair(repairId, nodeId)) return false;
            Spend(Repair(repairId).cost);
            State.Repairs++;
            State.LastRepairId = repairId;
            State.LastRepairNode = nodeId;
            // Неверный ремонт другую причину не устраняет; уже устранённая причина не возвращается.
            if (DiagnosisRules.IsCorrectRepair(CurrentFault, repairId)) State.FaultFixed = true;
            State.CheckedSinceRepair = false;
            State.Notes.Clear();
            AfterAction();
            return true;
        }

        public bool CanComplete => Active && (IsTireOrder
            ? State.Tire.Place == WheelPlace.Car && State.Tire.Installed && State.Tire.Controlled
            : State.Repairs > 0 && State.CheckedSinceRepair);

        // null = можно; иначе причина. Учитывает тип заказа, фазу и остаток времени.
        public string TireRefusal(TireAction action, int grams = 0)
        {
            if (!Active) return "Заказ сейчас не активен.";
            if (!IsTireOrder) return "В этом заказе работа с колесом не нужна.";
            string why = TireRules.Refusal(State.Tire, TireDef, action, grams);
            if (why != null) return why;
            return State.TimeLeft < TireRules.Cost(TireDef, action) ? "Не хватает времени." : null;
        }

        public string TryTire(TireAction action, int grams = 0)
        {
            if (TireRefusal(action, grams) != null) return null;
            int cost = TireRules.Cost(TireDef, action);
            State.TimeLeft -= cost; State.TimeSpent += cost; State.Actions++;
            State.LastTireMessage = TireRules.Apply(State.Tire, TireDef, action, grams);
            AfterAction();
            return State.LastTireMessage;
        }

        // Начать текущий заказ заново в любой момент (из меню паузы). Предмет из рук возвращается на место.
        public bool TryRestartCurrent()
        {
            if (Phase != FlowPhase.Order) return false;
            StartCase(State.CaseIndex);
            return true;
        }

        public bool TryComplete()
        {
            if (!CanComplete) return false;
            bool ok = IsTireOrder ? State.Tire.Serviced : State.FaultFixed;
            State.Outcome = ok ? OrderOutcome.Success : OrderOutcome.WrongFix;
            if (State.Outcome == OrderOutcome.Success)
            {
                OrderResult r = results[State.CaseIndex];
                r.Completed = true; r.TimeSpent = State.TimeSpent; r.Actions = State.Actions;
            }
            Changed?.Invoke();
            return true;
        }

        public bool TryRetry()
        {
            if (Phase != FlowPhase.Order || !State.Finished) return false;
            StartCase(State.CaseIndex);
            return true;
        }

        // Следующий заказ открывается только после успеха. После последнего — финал.
        public bool TryNext()
        {
            if (Phase != FlowPhase.Order || State.Outcome != OrderOutcome.Success) return false;
            if (HasNextOrder) StartCase(State.CaseIndex + 1);
            else { Phase = FlowPhase.Final; Changed?.Invoke(); }
            return true;
        }

        private void Spend(int minutes) { State.TimeLeft -= minutes; State.TimeSpent += minutes; State.Actions++; }

        private void AfterAction()
        {
            int needed = IsTireOrder ? TireRules.RemainingMinimal(State.Tire, TireDef)
                : DiagnosisRules.TimeNeededToFinish(Catalog, State.Repairs > 0);
            if (!State.Finished && !CanComplete && State.TimeLeft < needed) State.Outcome = OrderOutcome.OutOfTime;
            Changed?.Invoke();
        }

        private void StartCase(int index)
        {
            State = new OrderState { CaseIndex = index, TimeLeft = Catalog.orders[index].timeBudget };
            if (OrderTypes.IsTire(Catalog.orders[index])) State.Tire = new TireJob();
            results[index].Attempts++;
            Changed?.Invoke();
        }
    }
}
