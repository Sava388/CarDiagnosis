using System;
using System.Collections.Generic;
using System.IO;
using CarDiagnosis.Core;
using TMPro;
using UnityEngine;

namespace Diagnoz
{
    // Связка сцены с ядром: загружает и проверяет каталог, хранит OrderFlow (состояние смены).
    // Правил здесь нет — они в CarDiagnosis.Core. Имя класса и Configure сохранены для генератора сцены.
    [DefaultExecutionOrder(-100)]
    public sealed class BatteryOrderSession : MonoBehaviour
    {
        public const string CatalogFolder = "CarDiagnosis";
        public const string CatalogFile = "orders.json";
        public const string FallbackResource = "CarDiagnosis/DefaultCatalog";

        [SerializeField] private BatteryOrderDefinition definition;   // устаревшая настройка, не используется правилами
        [SerializeField] private DiagnosticNode batteryNode;          // сохранено для совместимости со сценой

        private readonly Dictionary<string, DiagnosticNode> sceneNodes = new Dictionary<string, DiagnosticNode>();
        public OrderFlow Flow { get; private set; }
        public CatalogLoadResult Load { get; private set; }
        public string PrimaryPath { get; private set; }
        public event Action Changed;

        public void Configure(BatteryOrderDefinition settings, DiagnosticNode node)
        { definition = settings; batteryNode = node; }

        public static string CatalogPath => Path.Combine(Application.streamingAssetsPath, CatalogFolder, CatalogFile);

        private void Awake()
        {
            sceneNodes.Clear();
            foreach (DiagnosticNode n in FindObjectsByType<DiagnosticNode>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!sceneNodes.ContainsKey(n.Id)) sceneNodes.Add(n.Id, n);
                else Debug.LogError("Car Diagnosis: в сцене два узла с id «" + n.Id + "».", n);

            PrimaryPath = CatalogPath;
            string primary = null, readError = null;
            try { if (File.Exists(PrimaryPath)) primary = File.ReadAllText(PrimaryPath); }
            catch (Exception ex) { readError = ex.Message; }
            TextAsset fallback = Resources.Load<TextAsset>(FallbackResource);
            Load = CatalogLoader.Load(primary, CatalogFile, fallback != null ? fallback.text : null,
                sceneNodes.Keys, MissingGlyphs);
            if (readError != null) Load.PrimaryErrors.Insert(0, CatalogFile + ": ошибка чтения (" + readError + ").");

            if (Load.Source == CatalogSource.Primary)
                Debug.Log("Car Diagnosis: каталог загружен из " + PrimaryPath + ", заказов: " + Load.Catalog.orders.Length + ".");
            foreach (string e in Load.PrimaryErrors) Debug.LogError("Car Diagnosis, " + PrimaryPath + ": " + e);
            if (Load.Source == CatalogSource.Fallback)
                Debug.LogWarning("Car Diagnosis: основной каталог не принят, используется встроенный резервный каталог.");
            foreach (string e in Load.FallbackErrors) Debug.LogError("Car Diagnosis, резервный каталог: " + e);

            if (Load.Catalog == null) { Flow = null; return; }
            Flow = new OrderFlow(Load.Catalog);
            Flow.Changed += () => Changed?.Invoke();
        }

        public DiagnosticNode SceneNode(string id) => id != null && sceneNodes.TryGetValue(id, out DiagnosticNode n) ? n : null;

        // Символы, которых нет в статическом шрифте интерфейса (тексты каталога проверяются до применения).
        private string MissingGlyphs(string text)
        {
            BatteryOrderUI ui = GetComponent<BatteryOrderUI>();
            TMP_FontAsset font = ui != null ? ui.Font : null;
            if (font == null || string.IsNullOrEmpty(text)) return null;
            var printable = new System.Text.StringBuilder();
            foreach (char ch in text) if (!char.IsControl(ch)) printable.Append(ch);
            if (font.HasCharacters(printable.ToString(), out List<char> missing) || missing == null || missing.Count == 0) return null;
            return new string(missing.ToArray());
        }
    }
}
