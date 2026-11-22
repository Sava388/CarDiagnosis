using System;
using System.Collections.Generic;
using System.Text;
using CarDiagnosis.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diagnoz
{
    // Показывает состояние OrderFlow и передаёт ему команды. Правил и текстов заказов здесь нет.
    // RC2: компактная карточка заказа, подсказка действия по E, окна станков, пауза; панель узлов — только в осмотре.
    public sealed class BatteryOrderUI : MonoBehaviour
    {
        private enum Window { None, Intro, Briefing, Observation, ConfirmRepair, ConfirmCompletion, Result, Final, Fatal, Pause, Station, TireControl }
        private sealed class ActionButton { public Button Button; public bool Repair; public string Id; }
        private struct Choice { public string Label; public Action Run; public bool Enabled, Primary; }
        private const int MaxChoices = 6;

        [SerializeField] private BatteryOrderSession session;
        [SerializeField] private InteractionController interaction;
        [SerializeField] private UiInputBlocker blocker;
        [SerializeField] private OrbitCamera orbit;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private WorkshopDirector director;
        [SerializeField] private WheelServiceView wheelView;
        [SerializeField] private WorkshopAudio audioFx;
        [SerializeField] private RectTransform nodePanel, complaintPanel, modalCard;
        [SerializeField] private TMP_Text eyebrow, title, goal, timeLeft, complaint, observation, reason, hints, disclaimer;
        [SerializeField] private Image timeFill;
        [SerializeField] private Button finish, exitInspect;
        [SerializeField] private GameObject crosshair, prompt, carry, overlay;
        [SerializeField] private TMP_Text promptKey, promptText, carryText, toastText;
        [SerializeField] private Image promptKeyBg;
        [SerializeField] private CanvasGroup toast;
        [SerializeField] private TMP_Text modalTitle, reading, modalBody;
        [SerializeField] private Button[] choiceButtons;
        [SerializeField] private TMP_Text[] choiceLabels;

        public TMP_FontAsset Font => font;
        private readonly List<ActionButton> actions = new List<ActionButton>();
        private readonly Choice[] choices = new Choice[MaxChoices];
        private Window window;
        private WorkshopPoint stationPoint;
        private string pendingRepair, lastPrompt;
        private bool lastPromptEnabled, inspectMode, started;
        private OrderState briefed;
        private float nextActionTime, toastUntil;

        public static readonly Color Accent = new Color(.95f, .66f, .22f);
        private static readonly Color Ink = new Color(.93f, .94f, .93f);
        private static readonly Color Muted = new Color(.66f, .70f, .71f);
        private static readonly Color Dim = new Color(.45f, .48f, .49f);
        private static readonly Color PanelColor = new Color(.075f, .085f, .09f, .90f);
        private static readonly Color CardColor = new Color(.085f, .095f, .10f, .98f);
        private static readonly Color Warn = new Color(.86f, .36f, .30f);
        private const float ActionsTop = 214f, ButtonStep = 42f, ButtonHeight = 36f;
        private const float NodeWidth = 340f, Inner = 304f;
        private const float CenterW = 720f, CenterH = 540f, SideW = 470f, SideH = 640f;

        private OrderFlow Flow => session != null ? session.Flow : null;
        private CatalogData Catalog => Flow.Catalog;

        // Только для генератора сцены: ссылки сохраняются в сцене.
        public void Build(BatteryOrderSession order, InteractionController controller,
            UiInputBlocker inputBlocker, OrbitCamera cameraOrbit, TMP_FontAsset uiFont)
        {
            session = order; interaction = controller; blocker = inputBlocker; orbit = cameraOrbit; font = uiFont;

            RectTransform card = Panel("OrderCard", transform, new Vector2(0,1), new Vector2(0,1), new Vector2(20,-20), new Vector2(390,182), PanelColor);
            Panel("Accent", card, new Vector2(0,1), new Vector2(0,1), Vector2.zero, new Vector2(4,182), Accent);
            eyebrow = Text("Eyebrow", card, "", 13, 20, 14, 350, 18, Accent);
            title = Text("OrderTitle", card, "", 23, 20, 34, 350, 32, Ink, true);
            Text("GoalLabel", card, "ЦЕЛЬ", 12, 20, 74, 350, 16, Dim);
            goal = Text("Goal", card, "", 17, 20, 91, 350, 46, Ink);
            timeLeft = Text("TimeLeft", card, "", 14, 20, 141, 350, 18, Muted);
            Panel("TimeBack", card, new Vector2(0,1), new Vector2(0,1), new Vector2(20,-164), new Vector2(350,5), new Color(.2f,.22f,.23f));
            timeFill = Panel("TimeFill", card, new Vector2(0,1), new Vector2(0,1), new Vector2(20,-164), new Vector2(350,5), Accent).GetComponent<Image>();

            complaintPanel = Panel("ComplaintPanel", transform, new Vector2(0,1), new Vector2(0,1), new Vector2(20,-212), new Vector2(390,150), PanelColor);
            Text("ComplaintLabel", complaintPanel, "ЖАЛОБА КЛИЕНТА", 12, 20, 14, 350, 16, Dim);
            complaint = Text("Complaint", complaintPanel, "", 16, 20, 34, 350, 104, Ink);

            nodePanel = Panel("NodePanel", transform, new Vector2(1,1), new Vector2(1,1), new Vector2(-20,-20), new Vector2(NodeWidth,640), PanelColor);
            Text("SelectedLabel", nodePanel, "ВЫБРАННЫЙ УЗЕЛ", 12, 18, 14, Inner, 16, Dim);
            TMP_Text nodeName = Text("SelectedName", nodePanel, "", 22, 18, 34, Inner, 30, Ink, true);
            Button clear = Button("Clear", nodePanel, "Снять выбор", 18, 72, Inner, 34, false);
            observation = Text("Observation", nodePanel, "", 14, 18, 116, Inner, 88, Muted);
            finish = Button("Finish", nodePanel, "Завершить заказ", 18, 560, Inner, 40, true);
            reason = Text("FinishReason", nodePanel, "", 13, 18, 604, Inner, 20, Muted);
            exitInspect = Button("ExitInspect", nodePanel, "Выйти из осмотра  [Esc]", 18, 630, Inner, 36, false);
            gameObject.AddComponent<NodeSelectionPanel>().Configure(interaction, blocker, nodeName, clear);

            crosshair = Panel("Crosshair", transform, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(6,6), new Color(1,1,1,.85f)).gameObject;

            RectTransform p = Panel("Prompt", transform, new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(0,92), new Vector2(560,48), PanelColor);
            prompt = p.gameObject;
            promptKeyBg = Panel("Key", p, new Vector2(0,1), new Vector2(0,1), new Vector2(8,-7), new Vector2(34,34), Accent).GetComponent<Image>();
            promptKey = Text("KeyLabel", promptKeyBg.transform, "E", 18, 0, 0, 34, 34, new Color(.08f,.08f,.08f), true);
            promptKey.alignment = TextAlignmentOptions.Center;
            promptText = Text("PromptText", p, "", 18, 54, 0, 494, 48, Ink);
            promptText.alignment = TextAlignmentOptions.MidlineLeft;

            RectTransform c = Panel("Carry", transform, new Vector2(0,0), new Vector2(0,0), new Vector2(20,20), new Vector2(330,64), PanelColor);
            carry = c.gameObject;
            Text("CarryLabel", c, "В РУКАХ", 12, 18, 10, 300, 16, Accent);
            carryText = Text("CarryText", c, "", 16, 18, 28, 300, 30, Ink);

            RectTransform h = Panel("Controls", transform, new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(0,14), new Vector2(640,50), new Color(0,0,0,0));
            hints = Text("ControlsText", h, "", 14, 10, 4, 620, 22, Muted);
            hints.alignment = TextAlignmentOptions.Center;
            disclaimer = Text("Disclaimer", h, "", 12, 10, 28, 620, 18, Dim);
            disclaimer.alignment = TextAlignmentOptions.Center;

            RectTransform t = Panel("Toast", transform, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-20), new Vector2(600,46), CardColor);
            toast = t.gameObject.AddComponent<CanvasGroup>(); toast.alpha = 0; toast.blocksRaycasts = false; toast.interactable = false;
            t.GetComponent<Image>().raycastTarget = false;
            toastText = Text("ToastText", t, "", 16, 16, 0, 568, 46, Ink);
            toastText.alignment = TextAlignmentOptions.Center;

            RectTransform dim = Panel("ModalOverlay", transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(.02f,.025f,.03f,.78f));
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.offsetMin = dim.offsetMax = Vector2.zero;
            overlay = dim.gameObject;
            modalCard = Panel("Window", dim, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(CenterW,CenterH), CardColor);
            RectTransform stripe = Panel("Accent", modalCard, new Vector2(0,1), new Vector2(0,1), Vector2.zero, new Vector2(CenterW,4), Accent);
            stripe.anchorMax = new Vector2(1,1); stripe.sizeDelta = new Vector2(0,4);
            modalTitle = Text("Title", modalCard, "", 28, 30, 26, CenterW - 60, 40, Ink, true);
            reading = Text("Reading", modalCard, "", 30, 30, 74, CenterW - 60, 56, Accent);
            modalBody = Text("Body", modalCard, "", 18, 30, 140, CenterW - 60, 250, Ink);
            modalBody.fontSizeMin = 13;
            choiceButtons = new Button[MaxChoices]; choiceLabels = new TMP_Text[MaxChoices];
            for (int i = 0; i < MaxChoices; i++)
            {
                choiceButtons[i] = Button("Choice" + i, modalCard, "", 30, 460, 200, 44, i == 0);
                choiceLabels[i] = choiceButtons[i].GetComponentInChildren<TMP_Text>();
            }
            overlay.SetActive(false);
        }

        public void BindWorkshop(WorkshopDirector workshop, WheelServiceView wheel, WorkshopAudio fx)
        { director = workshop; wheelView = wheel; audioFx = fx; }

        private void Start()
        {
            if (session == null || interaction == null || choiceButtons == null || choiceButtons.Length < MaxChoices || nodePanel == null)
            { Debug.LogError("Car Diagnosis: интерфейс не настроен. Пересоздайте сцену: Tools > Diagnoz > Create Workshop Scene (RC2).", this); return; }
            for (int i = 0; i < MaxChoices; i++) { int index = i; choiceButtons[i].onClick.AddListener(() => OnChoice(index)); }
            if (Flow == null) { started = true; ShowFatal(); return; }
            CatalogSettings s = Catalog.settings;
            disclaimer.text = s.disclaimer ?? "";
            CreateActionButtons();
            finish.onClick.AddListener(AskCompletion);
            exitInspect.onClick.AddListener(() => { PlaySfx(Sfx.Click); if (director != null) director.ExitInspect(); });
            session.Changed += Refresh;
            interaction.SelectionChanged += OnSelection;
            started = true;
            Refresh();
            ShowIntro();
        }

        private void OnDestroy()
        {
            if (session != null) session.Changed -= Refresh;
            if (interaction != null) interaction.SelectionChanged -= OnSelection;
        }

        private void Update()
        {
            if (toast == null) return;
            float left = toastUntil - Time.unscaledTime;
            toast.alpha = left <= 0 ? 0 : Mathf.Clamp01(left / .4f);
        }

        private void CreateActionButtons()
        {
            float y = ActionsTop;
            Text("ChecksLabel", nodePanel, "ПРОВЕРКИ", 12, 18, y, Inner, 16, Dim); y += 20;
            foreach (CheckDef c in Catalog.checks) { Add(false, c.id, c.label + " · " + c.cost + " мин", y); y += ButtonStep; }
            y += 4;
            Text("RepairsLabel", nodePanel, "РЕМОНТ", 12, 18, y, Inner, 16, Dim); y += 20;
            foreach (RepairDef r in Catalog.repairs) { Add(true, r.id, r.label + " · " + r.cost + " мин", y); y += ButtonStep; }
            y += 8;
            Place(finish.GetComponent<RectTransform>(), y); y += 44;
            Place(reason.rectTransform, y); y += 24;
            Place(exitInspect.GetComponent<RectTransform>(), y); y += 36 + 14;
            nodePanel.sizeDelta = new Vector2(NodeWidth, y);
        }

        private void Add(bool repair, string id, string label, float y)
        {
            Button b = Button((repair ? "Repair_" : "Check_") + id, nodePanel, label, 18, y, Inner, ButtonHeight, false);
            var item = new ActionButton { Button = b, Repair = repair, Id = id };
            b.onClick.AddListener(() => OnAction(item));
            actions.Add(item);
        }

        private static void Place(RectTransform r, float y) { r.anchoredPosition = new Vector2(r.anchoredPosition.x, -y); }
        private void OnSelection(DiagnosticNode node) { Refresh(); }
        private string SelectedId => interaction.Selected != null ? interaction.Selected.Id : null;
        private void PlaySfx(Sfx s) { if (audioFx != null) audioFx.Play(s); }

        public void SetInspectMode(bool value) { inspectMode = value; Refresh(); }

        public void Toast(string text)
        {
            if (toastText == null || string.IsNullOrEmpty(text)) return;
            toastText.text = text; toastUntil = Time.unscaledTime + 3.2f;
        }

        // ---------- Состояние HUD ----------

        private void Refresh()
        {
            if (!started || Flow == null) return;
            OrderFlow f = Flow;
            OrderDef order = f.Current;
            OrderState s = f.State;
            bool inOrder = order != null;
            nodePanel.gameObject.SetActive(inOrder && inspectMode);
            complaintPanel.gameObject.SetActive(inOrder && inspectMode);
            eyebrow.transform.parent.gameObject.SetActive(inOrder);
            TireJob job = s.Tire;
            carry.SetActive(inOrder && job != null && job.Carried);
            if (job != null && f.TireDef != null) carryText.text = f.TireDef.wheelName + " · шина: " + TireRules.TireLabel(job, f.TireDef);
            hints.text = inspectMode ? (Catalog.settings.controlsText + "   Esc - выйти из осмотра")
                : "WASD - ходьба   Shift - быстрее   Мышь - обзор   E - действие   Esc - меню";
            UpdateCrosshair();
            if (!inOrder)
            {
                eyebrow.text = title.text = goal.text = timeLeft.text = complaint.text = observation.text = reason.text = "";
                foreach (ActionButton a in actions) SetButton(a.Button, false);
                SetButton(finish, false);
                return;
            }
            string node = SelectedId;
            eyebrow.text = "ЗАКАЗ " + (s.CaseIndex + 1).ToString("00") + " / " + f.OrderCount.ToString("00") +
                (f.IsTireOrder ? "  ·  ШИНОМОНТАЖ" : "  ·  ДИАГНОСТИКА");
            title.text = order.title;
            complaint.text = order.complaint;
            int left = Mathf.Max(0, s.TimeLeft);
            timeLeft.text = "Осталось " + left + " из " + order.timeBudget + " мин";
            float k = order.timeBudget > 0 ? Mathf.Clamp01(left / (float)order.timeBudget) : 0;
            timeFill.rectTransform.sizeDelta = new Vector2(350 * k, 5);
            timeFill.color = k < .25f ? Warn : Accent;
            goal.text = GoalText(f, s, node);
            if (!f.IsTireOrder) RefreshNodePanel(f, s, node);
            if (window == Window.None && s != briefed && !s.Finished) { briefed = s; ShowBriefing(); return; }
            if (window == Window.None && s.Finished) ShowResult();
        }

        private string GoalText(OrderFlow f, OrderState s, string node)
        {
            if (s.Finished) return "Заказ закрыт";
            if (f.IsTireOrder) return TireRules.NextHint(s.Tire, f.TireDef);
            if (!inspectMode)
                return s.Repairs > 0 && s.CheckedSinceRepair ? "Завершите заказ в режиме осмотра (E у капота)"
                    : "Подойдите к капоту и нажмите E, чтобы осмотреть узлы";
            return node == null ? "Выберите узел автомобиля" :
                !s.AnyCheck ? "Выберите проверку" :
                s.Repairs == 0 ? "Сопоставьте наблюдения и выберите ремонт" :
                !s.CheckedSinceRepair ? "Проведите контрольную проверку отремонтированного узла" :
                "Завершите заказ или продолжите диагностику";
        }

        private void RefreshNodePanel(OrderFlow f, OrderState s, string node)
        {
            var notes = new StringBuilder();
            int from = Mathf.Max(0, s.Notes.Count - 3);
            for (int i = from; i < s.Notes.Count; i++)
            {
                Observation o = s.Notes[i];
                if (notes.Length > 0) notes.Append('\n');
                notes.Append(o.NodeName).Append(", ").Append(o.Check.windowTitle).Append(": ").Append(o.Headline);
            }
            if (notes.Length == 0)
                notes.Append(s.Repairs > 0 ? "После ремонта прежние результаты сброшены. Нужна контрольная проверка." :
                    node == null ? "Щёлкните по узлу под капотом или по колесу." : "Проверок ещё не было.");
            observation.text = notes.ToString();
            bool anyRepairHere = false;
            foreach (ActionButton a in actions)
            {
                SetButton(a.Button, a.Repair ? f.CanRepair(a.Id, node) : f.CanCheck(a.Id, node));
                if (a.Repair && DiagnosisRules.RepairAllowed(f.Repair(a.Id), node)) anyRepairHere = true;
            }
            SetButton(finish, f.CanComplete);
            reason.text = s.Finished ? "Заказ закрыт." : node == null ? "Сначала выберите узел." :
                s.Repairs == 0 ? (anyRepairHere ? "Завершение - после ремонта и проверки." : "Для этого узла ремонтов в списке нет.") :
                !s.CheckedSinceRepair ? "Нужна контрольная проверка узла после ремонта." : "Можно завершить заказ.";
        }

        private void UpdateCrosshair()
        {
            bool show = Flow != null && Flow.Phase == FlowPhase.Order && !inspectMode && window == Window.None;
            crosshair.SetActive(show);
            if (!show) { prompt.SetActive(false); lastPrompt = null; }
        }

        // ---------- Подсказка и действие E ----------

        public void SetFocus(WorkshopInteractable focus)
        {
            if (!started || prompt == null) return;
            bool can = false;
            string text = focus != null && window == Window.None && !inspectMode ? PromptFor(focus.Point, out can) : null;
            if (text == lastPrompt && can == lastPromptEnabled) return;
            lastPrompt = text; lastPromptEnabled = can;
            prompt.SetActive(text != null);
            if (text == null) return;
            promptText.text = text;
            promptText.color = can ? Ink : Muted;
            promptKeyBg.color = can ? Accent : new Color(.28f,.30f,.31f);
        }

        private string PromptFor(WorkshopPoint point, out bool can)
        {
            OrderFlow f = Flow; can = false;
            if (f == null || f.Phase != FlowPhase.Order || f.State.Finished) return null;
            TireJob j = f.State.Tire;
            switch (point)
            {
                case WorkshopPoint.InspectCar:
                    if (f.IsTireOrder) return "Осмотр узлов: в этом заказе не нужен";
                    can = true; return "Осмотреть узлы автомобиля";
                case WorkshopPoint.WheelSlot:
                    if (!f.IsTireOrder) return "Колесо: в этом заказе не обслуживается";
                    if (j.Place == WheelPlace.Hands) return Labeled(f, TireAction.Install, "Установить колесо на автомобиль", out can);
                    if (j.Place != WheelPlace.Car) return "Колесо сейчас на станке";
                    if (j.Controlled && j.Serviced) { can = f.CanComplete; return "Завершить заказ"; }
                    if (j.EverRemoved && j.Installed && !j.Controlled) return Labeled(f, TireAction.Control, "Проверить установку колеса", out can);
                    return Labeled(f, TireAction.Remove, "Снять колесо", out can);
                case WorkshopPoint.TireChanger:
                    if (!f.IsTireOrder) return "Шиномонтажный станок: в этом заказе не используется";
                    if (j.Place == WheelPlace.Changer) { can = true; return "Работать на шиномонтажном станке"; }
                    if (j.Place == WheelPlace.Hands) return Labeled(f, TireAction.PlaceOnChanger, "Закрепить колесо на станке", out can);
                    return "Шиномонтажный станок: колеса нет";
                case WorkshopPoint.Balancer:
                    if (!f.IsTireOrder) return "Балансировочный станок: в этом заказе не используется";
                    if (j.Place == WheelPlace.Balancer) { can = true; return "Работать на балансировочном станке"; }
                    if (j.Place == WheelPlace.Hands) return Labeled(f, TireAction.PlaceOnBalancer, "Установить колесо на вал станка", out can);
                    return "Балансировочный станок: колеса нет";
            }
            return null;
        }

        private static string Labeled(OrderFlow f, TireAction a, string label, out bool can)
        {
            string why = f.TireRefusal(a);
            can = why == null;
            int cost = TireRules.Cost(f.TireDef, a);
            return can ? (cost > 0 ? label + " · " + cost + " мин" : label) : why;
        }

        public void Interact(WorkshopPoint point)
        {
            if (window != Window.None || Time.unscaledTime < nextActionTime || Flow == null) return;
            nextActionTime = Time.unscaledTime + .25f;
            string text = PromptFor(point, out bool can);
            if (text == null) return;
            if (!can) { PlaySfx(Sfx.Error); Toast(text); return; }
            OrderFlow f = Flow; TireJob j = f.State.Tire;
            switch (point)
            {
                case WorkshopPoint.InspectCar: PlaySfx(Sfx.Click); director.EnterInspect(); break;
                case WorkshopPoint.WheelSlot:
                    if (j.Place == WheelPlace.Hands) DoTire(TireAction.Install);
                    else if (j.Controlled && j.Serviced) AskCompletion();
                    else if (j.EverRemoved && j.Installed && !j.Controlled) { DoTire(TireAction.Control); if (!f.State.Finished) ShowTireControl(); }
                    else DoTire(TireAction.Remove);
                    break;
                case WorkshopPoint.TireChanger:
                    if (j.Place == WheelPlace.Hands) DoTire(TireAction.PlaceOnChanger);
                    if (j.Place == WheelPlace.Changer && !f.State.Finished) ShowStation(WorkshopPoint.TireChanger);
                    break;
                case WorkshopPoint.Balancer:
                    if (j.Place == WheelPlace.Hands) DoTire(TireAction.PlaceOnBalancer);
                    if (j.Place == WheelPlace.Balancer && !f.State.Finished) ShowStation(WorkshopPoint.Balancer);
                    break;
            }
        }

        private void DoTire(TireAction a, int grams = 0)
        {
            string msg = Flow.TryTire(a, grams);
            if (msg == null) { PlaySfx(Sfx.Error); Toast(Flow.TireRefusal(a, grams)); return; }
            if (wheelView != null) wheelView.PlayAction(a);
            PlaySfx(a == TireAction.Dismount || a == TireAction.Mount ? Sfx.Machine :
                a == TireAction.Spin ? Sfx.Spin :
                a == TireAction.Control ? Sfx.Confirm : Sfx.Clunk);
            Toast(msg);
        }

        // ---------- Окна станков ----------

        private void ShowStation(WorkshopPoint point)
        {
            OrderFlow f = Flow; TireJob j = f.State.Tire; TireServiceDef d = f.TireDef;
            stationPoint = point;
            string tire = "Шина: " + TireRules.TireLabel(j, d);
            string last = string.IsNullOrEmpty(f.State.LastTireMessage) ? "" : "Последнее действие: " + f.State.LastTireMessage + "\n\n";
            string body = last + "Следующий шаг: " + TireRules.NextHint(j, d) + ".\nОсталось времени: " + Mathf.Max(0, f.State.TimeLeft) + " мин.";
            var list = new List<Choice>();
            if (point == WorkshopPoint.TireChanger)
            {
                list.Add(TireChoice("Демонтировать старую шину", TireAction.Dismount, 0, !j.OldTireOff));
                list.Add(TireChoice("Смонтировать шину: " + d.toTire, TireAction.Mount, 0, j.OldTireOff && !j.NewTireOn));
                list.Add(TireChoice("Снять колесо со станка", TireAction.TakeFromChanger, 0, j.NewTireOn));
                list.Add(C("Закрыть", Close));
                ShowWindow(Window.Station, "Шиномонтажный станок", tire, body, true, list);
            }
            else
            {
                string value = j.LastReading < 0 ? "Дисбаланс: не измерен" : j.LastReading == 0 ? "Дисбаланс: 0 г" : "Дисбаланс: " + j.LastReading + " г";
                list.Add(TireChoice(j.WeightApplied > 0 ? "Контрольный прогон" : "Запустить станок", TireAction.Spin, 0, true));
                foreach (int g in d.weightOptions) list.Add(TireChoice("Груз " + g + " г", TireAction.ApplyWeight, g, false));
                list.Add(TireChoice("Снять колесо со станка", TireAction.TakeFromBalancer, 0, j.Balanced));
                if (list.Count < MaxChoices) list.Add(C("Закрыть", Close));
                ShowWindow(Window.Station, "Балансировочный станок", value, tire + ". " + body, true, list);
            }
        }

        private Choice TireChoice(string label, TireAction a, int grams, bool primary)
        {
            int cost = TireRules.Cost(Flow.TireDef, a);
            return new Choice
            {
                Label = cost > 0 ? label + " · " + cost + " мин" : label,
                Enabled = Flow.TireRefusal(a, grams) == null, Primary = primary,
                Run = () =>
                {
                    DoTire(a, grams);
                    if (Flow.State.Finished) { Close(); return; }
                    bool stays = a != TireAction.TakeFromChanger && a != TireAction.TakeFromBalancer;
                    if (stays) ShowStation(stationPoint); else Close();
                }
            };
        }

        private void ShowTireControl()
        {
            OrderFlow f = Flow; TireJob j = f.State.Tire; TireServiceDef d = f.TireDef;
            string body = f.State.LastTireMessage + "\n\nОбслуживается одно колесо: " + d.wheelName.ToLowerInvariant() + ".";
            body += j.Serviced ? "\nМожно завершить заказ." : "\nРабота не закончена: если завершить сейчас, клиент вернётся.";
            ShowWindow(Window.TireControl, "Контроль установки", "Шина: " + TireRules.TireLabel(j, d), body, false, new List<Choice>
            {
                C("Завершить заказ", AskCompletion, f.CanComplete, j.Serviced),
                C("Позже", Close)
            });
        }

        // ---------- Диагностика (режим осмотра) ----------

        private void OnAction(ActionButton a)
        {
            if (window != Window.None || Time.unscaledTime < nextActionTime) return;
            PlaySfx(Sfx.Click);
            if (a.Repair) AskRepair(a.Id); else RunCheck(a.Id);
        }

        private void RunCheck(string checkId)
        {
            window = Window.Observation;   // блокирует авто-показ итога до закрытия окна наблюдения
            Observation o = Flow.TryCheck(checkId, SelectedId);
            if (o == null) { window = Window.None; return; }
            string body = "Узел: " + o.NodeName + "\n" + o.Text;
            if (!string.IsNullOrEmpty(o.Check.hint)) body += "\n\n" + o.Check.hint;
            body += "\n\nПотрачено " + o.Check.cost + " мин. Осталось " + Mathf.Max(0, Flow.State.TimeLeft) + " мин.";
            ShowWindow(Window.Observation, o.Check.windowTitle, o.Headline, body, false, new List<Choice> { C("Закрыть", Close, true, true) });
        }

        private void AskRepair(string repairId)
        {
            if (!Flow.CanRepair(repairId, SelectedId)) return;
            RepairDef r = Flow.Repair(repairId);
            pendingRepair = repairId;
            ShowWindow(Window.ConfirmRepair, r.resultName, "-" + r.cost + " мин",
                "Останется времени: " + (Flow.State.TimeLeft - r.cost) + " мин.\n\n" +
                "После ремонта прежние результаты сбрасываются - проведите контрольную проверку этого узла.", false,
                new List<Choice>
                {
                    C("Выполнить", () => { Hide(); if (Flow.TryRepair(pendingRepair, SelectedId)) PlaySfx(Sfx.Clunk); }, true, true),
                    C("Отмена", Close)
                });
        }

        private void AskCompletion()
        {
            if (!Flow.CanComplete) return;
            if (window != Window.None && window != Window.TireControl) return;
            string body;
            if (Flow.IsTireOrder)
            {
                TireJob j = Flow.State.Tire; TireServiceDef d = Flow.TireDef;
                body = "Колесо: " + d.wheelName.ToLowerInvariant() + ", шина: " + TireRules.TireLabel(j, d) +
                    ".\nБалансировка: " + (j.Balanced ? "выполнена" : "не выполнена") + ".\n\nПосле завершения результат изменить нельзя.";
            }
            else
                body = "Последний ремонт: " + Flow.Repair(Flow.State.LastRepairId).resultName +
                    ".\nКонтрольная проверка выполнена.\n\nПосле завершения результат изменить нельзя.";
            ShowWindow(Window.ConfirmCompletion, "Завершение заказа", "Отдать машину клиенту?", body, false, new List<Choice>
            {
                C("Завершить", () => { Hide(); Flow.TryComplete(); }, true, true),
                C("Отмена", Close)
            });
        }

        // ---------- Сценарные окна ----------

        private void ShowIntro()
        {
            CatalogSettings s = Catalog.settings;
            string body = s.introText + "\n\nВ мастерской: WASD - ходьба, мышь - обзор, E - действие, Esc - меню." +
                "\nВ осмотре узлов: " + s.controlsText + ".";
            string head = s.gameTitle;
            if (session.Load.Source == CatalogSource.Fallback)
            {
                head = "Резервный каталог";
                body = "Файл orders.json не принят (ошибок: " + session.Load.PrimaryErrors.Count +
                    ", подробности в Console / Player.log). Загружены встроенные заказы.\n\n" + body;
            }
            ShowWindow(Window.Intro, s.introTitle, head, body, false, new List<Choice>
            { C("Начать смену", () => { Hide(); Flow.Begin(); }, true, true) });
        }

        private void ShowBriefing()
        {
            if (director != null && inspectMode) director.ExitInspect();
            OrderFlow f = Flow; OrderDef o = f.Current;
            string goalText = !string.IsNullOrEmpty(o.goal) ? o.goal :
                f.IsTireOrder ? "Заменить шину на колесе и отбалансировать его." : "Найти причину неисправности, устранить её и проверить результат.";
            string body = "Жалоба клиента: " + o.complaint + "\n\nЗадача: " + goalText + "\n\nВремя на заказ: " + o.timeBudget + " мин.";
            if (f.IsTireOrder) body += "\nУсловно обслуживается одно колесо - учебное упрощение, а не инструкция шиномонтажа.";
            ShowWindow(Window.Briefing, o.title, "Заказ " + (f.State.CaseIndex + 1) + " из " + f.OrderCount, body, false,
                new List<Choice> { C("Принять заказ", () => { Close(); PlaySfx(Sfx.Confirm); }, true, true) });
        }

        private string TireSummary(OrderState s)
        {
            TireJob j = s.Tire; TireServiceDef d = Flow.TireDef;
            var missing = new List<string>();
            if (!j.NewTireOn) missing.Add("шина не заменена");
            if (j.NewTireOn && !j.Balanced) missing.Add("колесо не отбалансировано");
            if (j.Place != WheelPlace.Car) missing.Add("колесо не установлено на автомобиль");
            return missing.Count == 0 ? "" : "Не сделано: " + string.Join(", ", missing) + ".";
        }

        private void ShowResult()
        {
            OrderState s = Flow.State;
            OrderDef c = Flow.Current;
            string used = "Потрачено " + s.TimeSpent + " из " + c.timeBudget + " мин";
            var retry = C("Повторить заказ", () => { Hide(); Flow.TryRetry(); });
            if (s.Outcome == OrderOutcome.Success)
            {
                bool last = !Flow.HasNextOrder;
                string done = Flow.IsTireOrder
                    ? "Шина заменена на " + Flow.TireDef.toTire + ", колесо отбалансировано, установлено и проверено.\nОбслужено одно колесо: " + Flow.TireDef.wheelName.ToLowerInvariant() + "."
                    : "Неисправность устранена. Контрольная проверка подтвердила результат.\nВыполнено: " + Flow.Repair(s.LastRepairId).resultName + ".";
                PlaySfx(Sfx.Confirm);
                ShowWindow(Window.Result, "Заказ выполнен", used, done + "\nДействий: " + s.Actions + ".\n\n" +
                    (last ? "Это был последний заказ смены." : "Открыт следующий заказ."), false, new List<Choice>
                    {
                        C(last ? "Итоги смены" : "Следующий заказ", () => { Hide(); Flow.TryNext(); if (last) ShowFinal(); }, true, true),
                        retry
                    });
                return;
            }
            PlaySfx(Sfx.Error);
            if (s.Outcome == OrderOutcome.WrongFix)
                ShowWindow(Window.Result, "Клиент вернулся", "Работа не принята", (Flow.IsTireOrder
                    ? TireSummary(s) + "\n" + used + ".\n\nПовторите заказ: замените шину, отбалансируйте колесо и проверьте установку."
                    : "Выполненный ремонт не устранил причину неисправности.\n" + used + ".\n\nПовторите заказ: сопоставьте наблюдения и выберите другой ремонт."),
                    false, new List<Choice> { WithPrimary(retry) });
            else
                ShowWindow(Window.Result, "Время заказа вышло", used, (Flow.IsTireOrder ? TireSummary(s) + "\n\n" :
                    "Не хватило времени, чтобы закончить ремонт и контрольную проверку.\n\n") +
                    "Повторите заказ и избегайте лишних действий.", false, new List<Choice> { WithPrimary(retry) });
        }

        private void ShowFinal()
        {
            CatalogSettings st = Catalog.settings;
            var body = new StringBuilder();
            if (!string.IsNullOrEmpty(st.finalText)) body.Append(st.finalText).Append("\n\n");
            int total = 0, done = 0;
            foreach (OrderResult r in Flow.Results)
            {
                if (r.Completed) { done++; total += r.TimeSpent; }
                body.Append(r.Title).Append(" - ").Append(r.TimeSpent).Append(" из ").Append(r.Budget)
                    .Append(" мин, попыток: ").Append(r.Attempts).Append('\n');
            }
            body.Append("Всего потрачено: ").Append(total).Append(" мин.");
            ShowWindow(Window.Final, st.finalTitle, "Выполнено заказов: " + done + " из " + Flow.OrderCount,
                body.ToString(), false, new List<Choice> { C("Новая смена", NewShift, true, true) });
        }

        public void ShowPause()
        {
            if (window != Window.None || Flow == null || Flow.Phase != FlowPhase.Order) return;
            OrderFlow f = Flow;
            ShowWindow(Window.Pause, "Пауза", "Заказ " + (f.State.CaseIndex + 1) + " из " + f.OrderCount,
                "Повтор заказа сбрасывает время и состояние колеса: предмет из рук возвращается на место.\n" +
                "Новая смена начинает всё с первого заказа.\n\nWASD - ходьба, мышь - обзор, E - действие, Esc - закрыть меню.", false,
                new List<Choice>
                {
                    C("Продолжить", Close, true, true),
                    C("Начать заказ заново", () => { Hide(); if (director != null) director.ExitInspect(); f.TryRestartCurrent(); }),
                    C("Новая смена", NewShift),
                    C("Выйти из игры", Application.Quit)
                });
        }

        private void NewShift()
        {
            Hide();
            Flow.Restart();
            if (director != null) director.ResetForNewShift();
            briefed = null;
            ShowIntro();
        }

        private void ShowFatal()
        {
            var body = new StringBuilder("Не удалось загрузить ни основной, ни встроенный каталог заказов. Игра не может начаться.\n");
            var all = new List<string>(session.Load != null ? session.Load.PrimaryErrors : new List<string>());
            if (session.Load != null) all.AddRange(session.Load.FallbackErrors);
            for (int i = 0; i < all.Count && i < 3; i++) body.Append("\n- ").Append(all[i]);
            if (all.Count > 3) body.Append("\n... ещё ").Append(all.Count - 3).Append(" (см. Console / Player.log)");
            ShowWindow(Window.Fatal, "Каталог не загружен", "Ошибка данных", body.ToString(), false,
                new List<Choice> { C("Выход", Application.Quit, true, true) });
        }

        // Esc при открытом окне: закрываются только необязательные окна.
        public void OnEscape()
        {
            if (window == Window.Pause || window == Window.Station || window == Window.TireControl ||
                window == Window.ConfirmRepair || window == Window.ConfirmCompletion || window == Window.Observation) Close();
        }

        // ---------- Окно ----------

        private static Choice C(string label, Action run, bool enabled = true, bool primary = false)
            => new Choice { Label = label, Run = run, Enabled = enabled, Primary = primary };
        private static Choice WithPrimary(Choice c) { c.Primary = true; return c; }

        private void OnChoice(int index)
        {
            if (window == Window.None || Time.unscaledTime < nextActionTime) return;
            Choice c = choices[index];
            if (!c.Enabled || c.Run == null) return;
            nextActionTime = Time.unscaledTime + .3f;
            PlaySfx(Sfx.Click);
            c.Run();
        }

        // Hide — только скрыть окно (дальше действие само вызовет Refresh через Changed); Close — скрыть и обновить.
        private void Hide()
        {
            window = Window.None;
            if (overlay != null) overlay.SetActive(false);
            if (blocker != null) blocker.SetModal(false);
        }

        private void Close() { Hide(); Refresh(); }

        private void ShowWindow(Window type, string heading, string value, string body, bool side, List<Choice> list)
        {
            window = type; modalTitle.text = heading; reading.text = value; modalBody.text = body;
            int n = Mathf.Min(list.Count, MaxChoices);
            float w = side ? SideW : CenterW, h = side ? SideH : CenterH;
            modalCard.anchorMin = modalCard.anchorMax = side ? new Vector2(1,.5f) : new Vector2(.5f,.5f);
            modalCard.pivot = side ? new Vector2(1,.5f) : new Vector2(.5f,.5f);
            modalCard.anchoredPosition = side ? new Vector2(-40, 0) : Vector2.zero;
            modalCard.sizeDelta = new Vector2(w, h);
            overlay.GetComponent<Image>().color = side ? new Color(.02f,.025f,.03f,.25f) : new Color(.02f,.025f,.03f,.78f);
            int cols = side ? 1 : n <= 3 ? Mathf.Max(1, n) : n == 4 ? 2 : 3;
            int rows = Mathf.CeilToInt(n / (float)cols);
            float bh = side ? 42 : 44, gap = 10;
            float bw = (w - 60 - (cols - 1) * 12) / cols;
            float top = h - 30 - rows * bh - (rows - 1) * gap;
            for (int i = 0; i < MaxChoices; i++)
            {
                bool on = i < n;
                choiceButtons[i].gameObject.SetActive(on);
                choices[i] = on ? list[i] : default(Choice);
                if (!on) continue;
                int r = i / cols, col = i % cols;
                RectTransform rt = choiceButtons[i].GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(30 + col * (bw + 12), -(top + r * (bh + gap)));
                rt.sizeDelta = new Vector2(bw, bh);
                RectTransform lr = choiceLabels[i].rectTransform; lr.sizeDelta = new Vector2(bw - 16, bh);
                choiceLabels[i].text = list[i].Label;
                Style(choiceButtons[i], list[i].Primary);
                SetButton(choiceButtons[i], list[i].Enabled);
            }
            modalTitle.rectTransform.sizeDelta = new Vector2(w - 60, 40);
            reading.rectTransform.sizeDelta = new Vector2(w - 60, side ? 40 : 56);
            reading.fontSize = reading.fontSizeMax = side ? 24 : 30;
            float bodyTop = side ? 124 : 140;
            modalBody.rectTransform.anchoredPosition = new Vector2(30, -bodyTop);
            modalBody.rectTransform.sizeDelta = new Vector2(w - 60, Mathf.Max(60, top - bodyTop - 14));
            overlay.SetActive(true); blocker.SetModal(true);
            if (prompt != null) { prompt.SetActive(false); lastPrompt = null; }
            if (crosshair != null) crosshair.SetActive(false);
        }

        // ---------- Элементы ----------

        private static void Style(Button b, bool primary)
        {
            Image img = (Image)b.targetGraphic;
            img.color = primary ? Accent : new Color(.19f,.21f,.22f);
            TMP_Text label = b.GetComponentInChildren<TMP_Text>();
            label.color = primary ? new Color(.08f,.08f,.08f) : Ink;
            label.fontStyle = primary ? FontStyles.Bold : FontStyles.Normal;
        }

        private static void SetButton(Button b, bool enabled)
        {
            b.interactable = enabled;
            TMP_Text label = b.GetComponentInChildren<TMP_Text>();
            if (label != null) label.alpha = enabled ? 1f : .45f;
        }

        private static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, Color color)
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = item.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            Image img = item.GetComponent<Image>(); img.color = color;
            return rect;
        }

        private TMP_Text Text(string name, Transform parent, string value, float fontSize, float x, float y,
            float width, float height, Color? color = null, bool bold = false)
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = item.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1);
            rect.anchoredPosition = new Vector2(x,-y); rect.sizeDelta = new Vector2(width,height);
            TextMeshProUGUI label = item.GetComponent<TextMeshProUGUI>();
            label.font = font; label.text = value; label.fontSize = fontSize;
            label.color = color ?? Ink; label.raycastTarget = false;
            if (bold) label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true; label.fontSizeMin = fontSize * .82f; label.fontSizeMax = fontSize;
            return label;
        }

        private Button Button(string name, Transform parent, string value, float x, float y, float width, float height, bool primary)
        {
            RectTransform rect = Panel(name, parent, new Vector2(0,1), new Vector2(0,1), new Vector2(x,-y), new Vector2(width,height), Color.white);
            Image image = rect.GetComponent<Image>();
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.15f,1.15f,1.15f); colors.pressedColor = new Color(.85f,.85f,.85f);
            colors.selectedColor = Color.white; colors.disabledColor = new Color(.55f,.55f,.55f,.6f); colors.colorMultiplier = 1.2f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            TMP_Text label = Text("Label", rect, value, 16, 8, 0, width - 16, height);
            label.alignment = TextAlignmentOptions.Center;
            Style(button, primary);
            return button;
        }
    }
}
