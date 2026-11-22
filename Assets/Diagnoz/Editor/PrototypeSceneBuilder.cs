using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CarDiagnosis.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Diagnoz.Editor
{
    // RC2: генератор мастерской. Всё создаётся в Assets/Diagnoz/Generated/Workshop_RC2 (сцена, материалы, меши, текстуры).
    // Повторный запуск: «Открыть» существующую сцену или «Пересоздать» — удаляется ТОЛЬКО эта папка.
    // Настройки URP лежат отдельно в Generated/Rendering_RC2 и при пересоздании переиспользуются.
    public static class PrototypeSceneBuilder
    {
        private const string BasePath = "Assets/Diagnoz/Generated";
        private const string ScenePath = BasePath + "/Workshop_RC2";
        private const string RenderingPath = BasePath + "/Rendering_RC2";
        private const string SceneFile = ScenePath + "/Diagnoz_Workshop.unity";
        private const string LayerName = "DiagnozInteractive";
        private const int PropLayer = 2;   // Ignore Raycast: блокирует ходьбу, но не перекрывает луч взаимодействия
        private static string output;
        private static Dictionary<string, Material> M;
        private static TMP_FontAsset uiFont;
        private static int meshCounter;

        [Serializable] private sealed class ModelData { public MaterialData[] materials; public PartData[] parts; }
        [Serializable] private sealed class MaterialData { public string name; public float[] color; }
        [Serializable] private sealed class PartData
        {
            public string name, group, material, variant;
            public bool collider;
            public Vector3[] vertices;
            public int[] triangles;
        }

        private sealed class Refs
        {
            public Dictionary<string, Transform> groups;
            public Transform car, wheelRoot, carSlot, changerSlot, balancerSlot, changerTable, balancerShaft;
            public GameObject summer, winter, stand;
            public TMP_Text readout, tag;
            public List<Mesh> spareWheel = new List<Mesh>();
            public List<Material> spareWheelMats = new List<Material>();
        }

        [MenuItem("Tools/Diagnoz/Create Workshop Scene (RC2)")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Dialog("Сначала останови Play Mode."); return; }
            if (AssetDatabase.IsValidFolder(ScenePath))
            {
                int choice = EditorUtility.DisplayDialogComplex("Сцена мастерской уже есть",
                    "Папка " + ScenePath + " уже создана.\n\nОткрыть — открыть сохранённую сцену без изменений.\n" +
                    "Пересоздать — удалить только эту папку и собрать сцену заново (остальные файлы проекта не трогаются).",
                    "Открыть", "Отмена", "Пересоздать");
                if (choice == 1) return;
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (choice == 0)
                {
                    if (File.Exists(SceneFile)) EditorSceneManager.OpenScene(SceneFile);
                    else Dialog("В папке нет сцены (возможно, прошлая генерация прервалась). Выберите «Пересоздать».");
                    return;
                }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RemoveFromBuild(SceneFile);
                if (!AssetDatabase.DeleteAsset(ScenePath)) { Dialog("Не удалось удалить " + ScenePath + ". Закройте файлы из неё и повторите."); return; }
            }
            else if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (Application.unityVersion != "6000.3.28f1" && !EditorUtility.DisplayDialog("Версия Unity отличается",
                "Цель: 6000.3.28f1. Сейчас: " + Application.unityVersion + ". Версию проекта не меняем. Продолжить?",
                "Продолжить", "Отмена")) return;
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Diagnoz/Art/Vehicle/SedanModel.json");
            Font rawFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Diagnoz/Art/Fonts/NotoSans-Regular.ttf");
            if (source == null || rawFont == null || TMP_Settings.instance == null)
            { Dialog("Не найдены модель, шрифт или TMP Settings. Проверь полную распаковку комплекта и окончание импорта."); return; }
            if (Shader.Find("Universal Render Pipeline/Lit") == null || Shader.Find("TextMeshPro/Distance Field") == null)
            { Dialog("Дождись импорта URP и TMP. Если есть ошибки, пришли первую ошибку Console."); return; }
            try
            {
                ModelData model = JsonUtility.FromJson<ModelData>(source.text);
                if (model == null || model.parts == null || model.parts.Length < 100)
                    throw new InvalidDataException("Повреждены данные модели SedanModel.json.");
                int layer = EnsureLayer(); if (layer < 0) return;
                EnsureFolder(BasePath);
                PreparePipeline();
                output = ScenePath; meshCounter = 0;
                EnsureFolder(output); EnsureFolder(output + "/Materials"); EnsureFolder(output + "/Meshes"); EnsureFolder(output + "/Textures");
                uiFont = PrepareFont(rawFont);
                M = MakeMaterials(model);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Transform root = new GameObject("Diagnoz_Workshop").transform;
                root.gameObject.SetActive(false);
                var refs = new Refs();
                BuildVehicle(root, model, layer, refs);
                BuildRoom(root);
                BuildLighting(root);
                BuildBench(root);
                BuildStorage(root, refs);
                BuildLift(root);
                BuildTireChanger(root, refs);
                BuildBalancer(root, refs);
                BuildSystems(root, scene, layer, refs);
                EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene, SceneFile)) throw new IOException("Сцена не сохранена.");
                List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.Where(x => x.path != SceneFile).ToList();
                scenes.Insert(0, new EditorBuildSettingsScene(SceneFile, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                AssetDatabase.SaveAssets();
                Dialog("Создана и сохранена сцена: " + SceneFile + "\nОткрой Game и нажми Play. Порядок проверки - Documentation/USER_CHECK.md.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Dialog("Генерация остановлена. Частичный результат - в " + ScenePath +
                    ". Повторный запуск предложит «Пересоздать». Пришли первую ошибку Console.\n" + exception.Message);
            }
        }

        private static void RemoveFromBuild(string path)
        { EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(x => x.path != path).ToArray(); }

        // URP-ассеты создаются штатным SetupPipeline (как в RC1) один раз и затем переиспользуются.
        private static void PreparePipeline()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderingPath + "/URP.asset");
            if (existing == null)
            {
                EnsureFolder(RenderingPath);
                output = RenderingPath;
                SetupPipeline();
                existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderingPath + "/URP.asset");
            }
            else
            {
                GraphicsSettings.defaultRenderPipeline = existing;
                QualitySettings.renderPipeline = existing;
            }
            TuneRendering(existing);
        }

        // Мягкая настройка через SerializedObject: отсутствующие в версии URP поля пропускаются.
        private static void TuneRendering(UniversalRenderPipelineAsset pipeline)
        {
            if (pipeline == null) return;
            var so = new SerializedObject(pipeline);
            void Int(string name, int value) { SerializedProperty p = so.FindProperty(name); if (p != null) p.intValue = value; }
            void Bool(string name, bool value) { SerializedProperty p = so.FindProperty(name); if (p != null) p.boolValue = value; }
            void Float(string name, float value) { SerializedProperty p = so.FindProperty(name); if (p != null) p.floatValue = value; }
            Int("m_AdditionalLightsRenderingMode", 1);   // per pixel
            Int("m_AdditionalLightsPerObjectLimit", 8);
            Bool("m_AdditionalLightShadowsSupported", true);
            Int("m_AdditionalLightsShadowmapResolution", 2048);
            Bool("m_MainLightShadowsSupported", true);
            Int("m_MainLightShadowmapResolution", 2048);
            Bool("m_SoftShadowsSupported", true);
            Float("m_ShadowDistance", 18f);
            Int("m_ShadowCascadeCount", 2);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
        }

        private static void Dialog(string message) { EditorUtility.DisplayDialog("Diagnoz Workshop RC2", message, "OK"); }

        // ---------- Материалы и процедурные текстуры (без внешних файлов) ----------

        private static Dictionary<string, Material> MakeMaterials(ModelData model)
        {
            var all = new Dictionary<string, Material>();
            // Материалы автомобиля: цвет из модели, свойства поверхности - по типу.
            var surface = new Dictionary<string, Vector2>   // x = metallic, y = smoothness
            {
                {"Paint", new Vector2(.05f,.58f)}, {"Chrome", new Vector2(.92f,.78f)}, {"Rubber", new Vector2(0,.12f)},
                {"Plastic", new Vector2(0,.28f)}, {"Glass", new Vector2(0,.92f)}, {"Seat", new Vector2(0,.18f)},
                {"Headlight", new Vector2(0,.85f)}, {"Amber", new Vector2(0,.65f)}, {"RedLamp", new Vector2(0,.65f)},
                {"BatteryCase", new Vector2(0,.32f)}, {"BatteryTop", new Vector2(0,.3f)}, {"RedWire", new Vector2(0,.35f)},
                {"BlackWire", new Vector2(0,.3f)}, {"EngineMetal", new Vector2(.55f,.38f)}, {"Blue", new Vector2(0,.35f)},
                {"Coolant", new Vector2(0,.4f)}, {"Rim", new Vector2(.6f,.46f)}, {"Liner", new Vector2(0,.08f)},
                {"Plate", new Vector2(0,.35f)}, {"Marking", new Vector2(0,.2f)}, {"InteriorDark", new Vector2(0,.1f)},
                {"BrakeDisc", new Vector2(.75f,.42f)}
            };
            foreach (MaterialData d in model.materials)
            {
                Vector2 s = surface.TryGetValue(d.name, out Vector2 v) ? v : new Vector2(0,.3f);
                Material m = Mat(all, d.name, new Color(d.color[0], d.color[1], d.color[2], d.color[3]), s.x, s.y);
                if (d.name == "Glass") { m.SetColor("_BaseColor", new Color(.10f,.14f,.16f,.38f)); Transparent(m); }
                if (d.name == "Headlight") Emit(m, new Color(.25f,.24f,.2f));
            }
            Texture2D concrete = Tex("Concrete", 512, false, (u, w) =>
            {
                float big = Fbm(u, w, 4, 5, 11), fine = Fbm(u, w, 64, 2, 23), grain = Hash(Mathf.FloorToInt(u * 512), Mathf.FloorToInt(w * 512), 5);
                float v = .50f + (big - .5f) * .22f + (fine - .5f) * .10f + (grain - .5f) * .05f;
                float wet = Mathf.Clamp01((Fbm(u, w, 3, 4, 41) - .45f) * 2.2f);
                return new Color(v * .985f, v, v * 1.01f, .35f + wet * .5f);
            });
            Texture2D blocks = Tex("WallBlocks", 512, false, (u, w) =>
            {
                float bx = u * 4f, by = w * 8f; int row = Mathf.FloorToInt(by);
                if (row % 2 == 1) bx += .5f;
                float fx = bx - Mathf.Floor(bx), fy = by - Mathf.Floor(by);
                bool mortar = fx < .018f || fy < .036f;
                float n = (Fbm(u, w, 32, 3, 7) - .5f) * .08f + (Hash(Mathf.FloorToInt(bx), row, 3) - .5f) * .04f;
                float v = mortar ? .70f : .86f + n;
                return new Color(v, v, v * .99f, .25f);
            });
            Texture2D peg = Tex("Pegboard", 256, false, (u, w) =>
            {
                float fx = u * 10f - Mathf.Floor(u * 10f) - .5f, fy = w * 10f - Mathf.Floor(w * 10f) - .5f;
                bool hole = fx * fx + fy * fy < .022f;
                float v = hole ? .12f : .82f + (Fbm(u, w, 16, 2, 9) - .5f) * .06f;
                return new Color(v, v, v, .2f);
            });
            Texture2D stain = Tex("OilStain", 256, false, (u, w) =>
            {
                float dx = u - .5f, dy = w - .5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float edge = .62f + (Fbm(u, w, 6, 4, 17) - .5f) * .55f;
                float a = Mathf.Clamp01((edge - r) * 3.2f) * (.55f + Fbm(u, w, 12, 2, 5) * .3f);
                return new Color(.05f, .045f, .04f, a);
            }, TextureWrapMode.Clamp);
            Texture2D hazard = Tex("Hazard", 128, false, (u, w) =>
            {
                bool amber = Mathf.Repeat((u + w) * 4f, 1f) < .5f;
                return amber ? new Color(.86f,.58f,.16f,.4f) : new Color(.07f,.07f,.07f,.4f);
            });

            Material floor = Mat(all, "Concrete", new Color(.66f,.66f,.65f), 0, .55f, concrete, .4f); SmoothFromAlpha(floor);
            Material epoxy = Mat(all, "Epoxy", new Color(.40f,.43f,.45f), 0, .75f, concrete, .5f); SmoothFromAlpha(epoxy);
            Mat(all, "WallUpper", new Color(.60f,.61f,.60f), 0, .2f, blocks, .625f);
            Mat(all, "WallLower", new Color(.30f,.33f,.34f), 0, .25f, blocks, .625f);
            Mat(all, "Plinth", new Color(.13f,.14f,.15f), 0, .3f);
            Mat(all, "Ceiling", new Color(.36f,.37f,.37f), 0, .1f);
            Mat(all, "Steel", new Color(.52f,.54f,.55f), .7f, .48f);
            Mat(all, "DarkSteel", new Color(.20f,.215f,.225f), .45f, .38f);
            Mat(all, "MachineBody", new Color(.24f,.26f,.27f), .2f, .42f);
            Mat(all, "Accent", new Color(.86f,.56f,.16f), .1f, .5f);
            Mat(all, "Stripe", new Color(.80f,.55f,.18f), 0, .35f);
            Mat(all, "Hazard", Color.white, .1f, .4f, hazard, 2f);
            Mat(all, "Wood", new Color(.42f,.31f,.20f), 0, .25f);
            Mat(all, "Pegboard", new Color(.42f,.44f,.44f), 0, .2f, peg, 2f);
            Mat(all, "Cardboard", new Color(.50f,.39f,.26f), 0, .1f);
            Mat(all, "BinPlastic", new Color(.17f,.24f,.30f), 0, .38f);
            Mat(all, "CanRed", new Color(.42f,.12f,.08f), 0, .45f);
            Mat(all, "CanOlive", new Color(.27f,.30f,.22f), 0, .4f);
            Mat(all, "DoorPanel", new Color(.33f,.36f,.37f), .3f, .4f);
            Mat(all, "GatePanel", new Color(.55f,.57f,.57f), .35f, .45f);
            Mat(all, "SignPlate", new Color(.07f,.075f,.08f), 0, .35f);
            Mat(all, "Screen", new Color(.02f,.025f,.03f), 0, .85f);
            Mat(all, "SafetyRed", new Color(.60f,.07f,.05f), 0, .55f);
            Mat(all, "Selected", new Color(.99f,.74f,.13f), 0, .4f);
            Emit(Mat(all, "Lamp", new Color(.95f,.92f,.84f), 0, .5f), new Color(1.6f,1.45f,1.2f));
            Emit(Mat(all, "Daylight", new Color(.55f,.62f,.68f), 0, .9f), new Color(.55f,.62f,.70f));
            Material oil = Mat(all, "OilStain", Color.white, 0, .85f, stain, 1f); Transparent(oil);
            return all;
        }

        private static Material Mat(Dictionary<string, Material> all, string name, Color color, float metallic, float smooth,
            Texture2D tex = null, float tilesPerMeter = 1f)
        {
            Material m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(tilesPerMeter, tilesPerMeter)); }
            AssetDatabase.CreateAsset(m, output + "/Materials/" + name + ".mat");
            all[name] = m;
            return m;
        }

        private static void SmoothFromAlpha(Material m)
        { m.SetFloat("_SmoothnessTextureChannel", 1); m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"); }

        private static Material Emit(Material m, Color c)
        {
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        private static void Transparent(Material mat)
        {
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0); mat.SetInt("_Cull", (int)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Texture2D Tex(string name, int size, bool linear, Func<float, float, Color> f,
            TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
            { name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) px[y * size + x] = f((x + .5f) / size, (y + .5f) / size);
            t.SetPixels(px); t.Apply(true, false);
            AssetDatabase.CreateAsset(t, output + "/Textures/" + name + ".asset");
            return t;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144269504);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }

        // Бесшовный value noise: решётка с периодом period по обеим осям.
        private static float Noise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int Wrap(int a) => ((a % period) + period) % period;
            float a00 = Hash(Wrap(x0), Wrap(y0), seed), a10 = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float a01 = Hash(Wrap(x0), Wrap(y0 + 1), seed), a11 = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
        }

        private static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0, amp = .5f, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += Noise(u, v, period << i, seed + i * 31) * amp; norm += amp; amp *= .5f; }
            return sum / norm;
        }

        // ---------- Простая сборка мешей: плоские грани у коробок, гладкие стороны у цилиндров ----------

        private sealed class Kit
        {
            private sealed class Buf { public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> t = new List<int>(); }
            private readonly Dictionary<Material, Buf> bufs = new Dictionary<Material, Buf>();
            private Vector3 origin = Vector3.zero; private Quaternion rotation = Quaternion.identity;

            public Kit At(Vector3 position, float yaw = 0) { origin = position; rotation = Quaternion.Euler(0, yaw, 0); return this; }
            public Kit At(Vector3 position, Quaternion rot) { origin = position; rotation = rot; return this; }
            private Vector3 P(Vector3 local) => origin + rotation * local;
            private Buf B(Material m) { if (!bufs.TryGetValue(m, out Buf b)) bufs[m] = b = new Buf(); return b; }

            private void Tri(Buf b, Vector3 a, Vector3 c, Vector3 d, Vector3 na, Vector3 nc, Vector3 nd, Vector2 ua, Vector2 uc, Vector2 ud, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(c - a, d - a), outward) < 0) { (c, d) = (d, c); (nc, nd) = (nd, nc); (uc, ud) = (ud, uc); }
                int i = b.v.Count;
                b.v.Add(a); b.v.Add(c); b.v.Add(d); b.n.Add(na); b.n.Add(nc); b.n.Add(nd); b.uv.Add(ua); b.uv.Add(uc); b.uv.Add(ud);
                b.t.Add(i); b.t.Add(i + 1); b.t.Add(i + 2);
            }

            public void Quad(Material m, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normalLocal, Vector2 size)
            {
                Buf b = B(m); Vector3 n = rotation * normalLocal;
                Vector3 a = P(p0), c = P(p1), d = P(p2), e = P(p3);
                Vector2 u0 = Vector2.zero, u1 = new Vector2(size.x, 0), u2 = size, u3 = new Vector2(0, size.y);
                Tri(b, a, c, d, n, n, n, u0, u1, u2, n); Tri(b, a, d, e, n, n, n, u0, u2, u3, n);
            }

            // Коробка в локальных координатах объекта; euler - дополнительный поворот самой коробки.
            public void Box(Material m, Vector3 c, Vector3 s, Vector3 euler = default(Vector3))
            {
                Quaternion q = Quaternion.Euler(euler); Vector3 h = s * .5f;
                Vector3 X = q * Vector3.right, Y = q * Vector3.up, Z = q * Vector3.forward;
                void Face(Vector3 n, Vector3 u, Vector3 v, float hn, float hu, float hv, float su, float sv)
                {
                    Vector3 o = c + n * hn;
                    Quad(m, o - u * hu - v * hv, o + u * hu - v * hv, o + u * hu + v * hv, o - u * hu + v * hv, n, new Vector2(su, sv));
                }
                Face(X, Z, Y, h.x, h.z, h.y, s.z, s.y); Face(-X, Z, Y, h.x, h.z, h.y, s.z, s.y);
                Face(Y, X, Z, h.y, h.x, h.z, s.x, s.z); Face(-Y, X, Z, h.y, h.x, h.z, s.x, s.z);
                Face(Z, X, Y, h.z, h.x, h.y, s.x, s.y); Face(-Z, X, Y, h.z, h.x, h.y, s.x, s.y);
            }

            public void Cyl(Material m, Vector3 a, Vector3 bEnd, float r, int seg = 20, bool caps = true, float r2 = -1)
            {
                if (r2 < 0) r2 = r;
                Buf b = B(m);
                Vector3 axis = (bEnd - a).normalized, tmp = Mathf.Abs(axis.y) < .95f ? Vector3.up : Vector3.right;
                Vector3 u = Vector3.Cross(axis, tmp).normalized, w = Vector3.Cross(axis, u);
                float len = (bEnd - a).magnitude;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                    Vector3 d0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
                    Vector3 p0 = P(a + d0 * r), p1 = P(a + d1 * r), p2 = P(bEnd + d1 * r2), p3 = P(bEnd + d0 * r2);
                    Vector3 n0 = rotation * d0, n1 = rotation * d1, mid = rotation * (d0 + d1).normalized;
                    float s0 = i / (float)seg * 2 * Mathf.PI * r, s1 = (i + 1) / (float)seg * 2 * Mathf.PI * r;
                    Tri(b, p0, p1, p2, n0, n1, n1, new Vector2(s0, 0), new Vector2(s1, 0), new Vector2(s1, len), mid);
                    Tri(b, p0, p2, p3, n0, n1, n0, new Vector2(s0, 0), new Vector2(s1, len), new Vector2(s0, len), mid);
                    if (!caps) continue;
                    Vector3 na = rotation * -axis, nb = rotation * axis;
                    Tri(b, P(a), p1, p0, na, na, na, Vector2.zero, (Vector2)d1 * r, (Vector2)d0 * r, na);
                    Tri(b, P(bEnd), p3, p2, nb, nb, nb, Vector2.zero, (Vector2)d0 * r2, (Vector2)d1 * r2, nb);
                }
            }

            // Объект на каждый материал. shadows=false для стен/потолка (через них светит основной свет).
            public GameObject Flush(Transform parent, string name, bool shadows = true)
            {
                GameObject root = new GameObject(name); root.transform.SetParent(parent, false);
                foreach (KeyValuePair<Material, Buf> kv in bufs)
                {
                    Mesh mesh = new Mesh { name = name + "_" + kv.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.SetVertices(kv.Value.v); mesh.SetNormals(kv.Value.n); mesh.SetUVs(0, kv.Value.uv); mesh.SetTriangles(kv.Value.t, 0);
                    mesh.RecalculateBounds(); mesh.RecalculateTangents();
                    SaveMesh(mesh);
                    GameObject g = new GameObject(kv.Key.name, typeof(MeshFilter), typeof(MeshRenderer));
                    g.transform.SetParent(root.transform, false);
                    g.GetComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = kv.Key;
                    mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                }
                bufs.Clear();
                return root;
            }
        }

        private static void SaveMesh(Mesh mesh)
        {
            meshCounter++;
            AssetDatabase.CreateAsset(mesh, output + "/Meshes/" + meshCounter.ToString("0000") + "_" + Safe(mesh.name) + ".asset");
        }

        private static string Safe(string s)
        {
            var b = new StringBuilder();
            foreach (char c in s) b.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            return b.ToString();
        }

        // Невидимое препятствие для ходьбы (слой Ignore Raycast, не мешает лучу взаимодействия).
        private static void Blocker(Transform parent, string name, Vector3 center, Vector3 size, float yaw = 0)
        {
            GameObject g = new GameObject(name); g.layer = PropLayer;
            g.transform.SetParent(parent, false); g.transform.localPosition = center; g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            g.AddComponent<BoxCollider>().size = size;
        }

        private static WorkshopInteractable Point(Transform parent, string name, WorkshopPoint kind, Vector3 center, Vector3 size)
        {
            GameObject g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = center;
            BoxCollider box = g.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = size;
            WorkshopInteractable item = g.AddComponent<WorkshopInteractable>(); item.Configure(kind);
            return item;
        }

        private static TextMeshPro Label(Transform parent, string name, string text, Vector3 pos, Quaternion rot, float size, Color color, Vector2 box)
        {
            GameObject g = new GameObject(name, typeof(RectTransform), typeof(TextMeshPro));
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = rot;
            TextMeshPro t = g.GetComponent<TextMeshPro>();
            t.font = uiFont; t.text = text; t.fontSize = size; t.color = color; t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.rectTransform.sizeDelta = box;
            return t;
        }

        private static Light AddLight(Transform parent, string name, LightType type, Vector3 pos, Vector3 euler, Color color,
            float intensity, float range, float angle, LightShadows shadows)
        {
            GameObject g = new GameObject(name, typeof(Light)); g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(euler);
            Light l = g.GetComponent<Light>(); l.type = type; l.color = color; l.intensity = intensity; l.shadows = shadows;
            if (type != LightType.Directional) l.range = range;
            if (type == LightType.Spot) { l.spotAngle = angle; l.innerSpotAngle = angle * .55f; }
            if (shadows != LightShadows.None) { l.shadowStrength = .8f; l.shadowBias = .04f; l.shadowNormalBias = .3f; }
            return l;
        }

        // ---------- Автомобиль: детали модели объединяются по (группа, вариант, материал), нормали по углу ----------

        private sealed class Acc { public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<int> t = new List<int>(); }

        private static Vector3 GroupPivot(string group)
        {
            if (group == "Node_Battery") return new Vector3(-.405f,.96f,-1.49f);
            if (group == "Hood") return new Vector3(0,1.015f,-.47f);
            if (group.StartsWith("Wheel_"))
                return new Vector3(group.Contains("_L_") ? -.78f : .78f, .302f, group.EndsWith("Front") ? -1.212f : 1.212f);
            return Vector3.zero;
        }

        // Разваривает вершины детали и усредняет нормали только у граней с углом меньше ~50 градусов:
        // коробки получаются с чёткими гранями, цилиндры и шины - гладкими.
        private static void AppendSmooth(Acc acc, Vector3[] verts, int[] tris, Vector3 offset)
        {
            const float cosLimit = .64f;
            int faces = tris.Length / 3;
            var fn = new Vector3[faces];
            var incident = new List<int>[verts.Length];
            for (int f = 0; f < faces; f++)
            {
                Vector3 a = verts[tris[3 * f]], b = verts[tris[3 * f + 1]], c = verts[tris[3 * f + 2]];
                fn[f] = Vector3.Cross(b - a, c - a).normalized;
                for (int k = 0; k < 3; k++) { int vi = tris[3 * f + k]; (incident[vi] ?? (incident[vi] = new List<int>())).Add(f); }
            }
            for (int f = 0; f < faces; f++)
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[3 * f + k];
                    Vector3 sum = Vector3.zero;
                    foreach (int g in incident[vi]) if (Vector3.Dot(fn[g], fn[f]) >= cosLimit) sum += fn[g];
                    acc.n.Add(sum.sqrMagnitude > 1e-10f ? sum.normalized : fn[f]);
                    acc.v.Add(verts[vi] - offset);
                    acc.t.Add(acc.v.Count - 1);
                }
        }

        private static Mesh ToMesh(Acc acc, string name, bool normals)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(acc.v);
            if (normals) mesh.SetNormals(acc.n);
            mesh.SetTriangles(acc.t, 0);
            if (!normals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh);
            return mesh;
        }

        private static void BuildVehicle(Transform root, ModelData model, int layer, Refs refs)
        {
            Transform car = new GameObject("Car_SedanStudy").transform; car.SetParent(root, false);
            refs.car = car;
            var groups = new Dictionary<string, Transform>();
            var visual = new Dictionary<string, Acc>();         // ключ: group|variant|material
            var colliders = new Dictionary<string, Acc>();      // ключ: group
            foreach (PartData part in model.parts)
            {
                if (!groups.TryGetValue(part.group, out Transform group))
                {
                    group = new GameObject(part.group).transform; group.SetParent(car, false);
                    group.localPosition = GroupPivot(part.group);
                    groups.Add(part.group, group);
                }
                string key = part.group + "|" + (part.variant ?? "") + "|" + part.material;
                if (!visual.TryGetValue(key, out Acc acc)) visual[key] = acc = new Acc();
                AppendSmooth(acc, part.vertices, part.triangles, group.localPosition);
                bool wantsCollider = part.collider && part.group != "Node_Battery" && !part.group.StartsWith("Wheel_");
                if (wantsCollider)
                {
                    if (!colliders.TryGetValue(part.group, out Acc col)) colliders[part.group] = col = new Acc();
                    int start = col.v.Count;
                    foreach (Vector3 v in part.vertices) col.v.Add(v - group.localPosition);
                    foreach (int t in part.triangles) col.t.Add(start + t);
                }
            }
            foreach (KeyValuePair<string, Acc> kv in visual)
            {
                string[] k = kv.Key.Split('|');
                Transform parent = groups[k[0]];
                if (k[1].Length > 0)
                {
                    string holder = "Tire_" + k[1];
                    Transform h = parent.Find(holder);
                    if (h == null) { h = new GameObject(holder).transform; h.SetParent(parent, false); }
                    parent = h;
                }
                Mesh mesh = ToMesh(kv.Value, Safe(kv.Key), true);
                GameObject g = new GameObject(k[2], typeof(MeshFilter), typeof(MeshRenderer));
                g.transform.SetParent(parent, false);
                g.GetComponent<MeshFilter>().sharedMesh = mesh;
                g.GetComponent<MeshRenderer>().sharedMaterial = M[k[2]];
                if (k[0].StartsWith("Node_")) g.layer = layer;
                if (k[0] == "Wheel_R_Rear" && k[1] != "winter") { refs.spareWheel.Add(mesh); refs.spareWheelMats.Add(M[k[2]]); }
            }
            foreach (KeyValuePair<string, Acc> kv in colliders)
            {
                GameObject c = new GameObject("Collider"); c.transform.SetParent(groups[kv.Key], false);
                if (kv.Key.StartsWith("Node_")) c.layer = layer;
                c.AddComponent<MeshCollider>().sharedMesh = ToMesh(kv.Value, "Collider_" + kv.Key, false);
            }
            string[] names = { "Node_Battery", "Node_Alternator", "Node_Engine", "Node_Brakes" };
            string[] labels = { "Аккумулятор", "Генератор", "Двигатель", "Тормозной узел" };
            NodeKind[] kinds = { NodeKind.Battery, NodeKind.Alternator, NodeKind.Engine, NodeKind.Brakes };
            for (int i = 0; i < names.Length; i++)
            {
                Transform node = groups[names[i]]; node.gameObject.layer = layer;
                node.gameObject.AddComponent<DiagnosticNode>().Configure(kinds[i], labels[i],
                    node.GetComponentsInChildren<Renderer>(), M["Selected"]);
            }
            BoxCollider battery = groups["Node_Battery"].gameObject.AddComponent<BoxCollider>();
            battery.center = Vector3.zero; battery.size = new Vector3(.32f,.33f,.42f);
            // Габарит машины для ходьбы: игрок не проходит сквозь кузов и не залезает под него.
            Blocker(car, "CarBlocker", new Vector3(0,.75f,0), new Vector3(1.9f,1.5f,4.4f));

            // Обслуживаемое колесо: фиксированная точка на машине, само колесо переставляется WheelServiceView.
            Transform wheel = groups["Wheel_L_Front"];
            Transform slot = new GameObject("WheelSlot_FL").transform; slot.SetParent(car, false);
            slot.localPosition = wheel.localPosition;
            wheel.SetParent(slot, false); wheel.localPosition = Vector3.zero;
            refs.wheelRoot = wheel; refs.carSlot = slot;
            refs.summer = wheel.Find("Tire_summer").gameObject;
            refs.winter = wheel.Find("Tire_winter").gameObject; refs.winter.SetActive(false);
            Point(car, "Point_Wheel", WorkshopPoint.WheelSlot, slot.localPosition + new Vector3(-.06f,.02f,0), new Vector3(.40f,.66f,.72f));
            Point(car, "Point_InspectCar", WorkshopPoint.InspectCar, new Vector3(0,1.08f,-1.35f), new Vector3(1.66f,.62f,1.5f));
            // Подставка под порог, видна только когда колесо снято.
            var k2 = new Kit().At(new Vector3(-.77f,0,-.75f));
            k2.Box(M["Accent"], new Vector3(0,.012f,0), new Vector3(.26f,.024f,.26f));
            k2.Cyl(M["Accent"], new Vector3(0,.02f,0), new Vector3(0,.25f,0), .05f, 12, true, .035f);
            k2.Cyl(M["Steel"], new Vector3(0,.25f,0), new Vector3(0,.30f,0), .025f, 10);
            k2.Box(M["Rubber"], new Vector3(0,.305f,0), new Vector3(.09f,.012f,.09f));
            refs.stand = k2.Flush(car, "AxleStand");
            refs.stand.SetActive(false);
            refs.groups = groups;
        }

        // ---------- Помещение: 12 x 11 м, высота 4.2 м ----------
        private const float RoomX = 6f, RoomZ = 5.5f, RoomH = 4.2f;

        private static void BuildRoom(Transform root)
        {
            Transform room = new GameObject("Room").transform; room.SetParent(root, false);
            var shell = new Kit();
            // Пол: бетон + эпоксидная зона поста под машиной.
            shell.Box(M["Concrete"], new Vector3(0,-.1f,0), new Vector3(RoomX * 2, .2f, RoomZ * 2));
            shell.Box(M["Epoxy"], new Vector3(0,.002f,0), new Vector3(4.4f,.004f,6.4f));
            Blocker(room, "FloorCollider", new Vector3(0,-.1f,0), new Vector3(RoomX * 2 + 1, .2f, RoomZ * 2 + 1));
            // Стены: нижняя тёмная панель 1.2 м, верх светлый, плинтус. Проёмы: ворота спереди (-z), дверь сзади.
            void Wall(Vector3 c, Vector3 s, Vector3 inward)
            {
                shell.Box(M["WallUpper"], c, s);
                Blocker(room, "Wall", c, s + new Vector3(.02f,0,.02f));
            }
            float t = .2f;
            Wall(new Vector3(-RoomX - t / 2, RoomH / 2, 0), new Vector3(t, RoomH, RoomZ * 2 + t * 2), Vector3.right);
            Wall(new Vector3(RoomX + t / 2, RoomH / 2, 0), new Vector3(t, RoomH, RoomZ * 2 + t * 2), Vector3.left);
            // Задняя стена с дверью (x 1.15..2.05, высота 2.15).
            Wall(new Vector3((-RoomX + 1.15f) / 2, RoomH / 2, RoomZ + t / 2), new Vector3(RoomX + 1.15f, RoomH, t), Vector3.back);
            Wall(new Vector3((2.05f + RoomX) / 2, RoomH / 2, RoomZ + t / 2), new Vector3(RoomX - 2.05f, RoomH, t), Vector3.back);
            Wall(new Vector3(1.6f, (2.15f + RoomH) / 2, RoomZ + t / 2), new Vector3(.9f, RoomH - 2.15f, t), Vector3.back);
            // Передняя стена с воротами (x -1.9..1.9, высота 3.2).
            Wall(new Vector3((-RoomX - 1.9f) / 2, RoomH / 2, -RoomZ - t / 2), new Vector3(RoomX - 1.9f, RoomH, t), Vector3.forward);
            Wall(new Vector3((RoomX + 1.9f) / 2, RoomH / 2, -RoomZ - t / 2), new Vector3(RoomX - 1.9f, RoomH, t), Vector3.forward);
            Wall(new Vector3(0, (3.2f + RoomH) / 2, -RoomZ - t / 2), new Vector3(3.8f, RoomH - 3.2f, t), Vector3.forward);
            // Нижние панели и плинтус (тонкий слой на стене, с разрывами у проёмов).
            void Wainscot(Vector3 a, Vector3 b, Vector3 n)
            {
                Vector3 c = (a + b) / 2; float len = (b - a).magnitude; bool alongX = Mathf.Abs(n.z) > .5f;
                Vector3 size = alongX ? new Vector3(len, 1.2f, .02f) : new Vector3(.02f, 1.2f, len);
                Vector3 plinth = alongX ? new Vector3(len, .12f, .04f) : new Vector3(.04f, .12f, len);
                Vector3 line = alongX ? new Vector3(len, .03f, .03f) : new Vector3(.03f, .03f, len);
                shell.Box(M["WallLower"], c + n * .01f + Vector3.up * .6f, size);
                shell.Box(M["Plinth"], c + n * .02f + Vector3.up * .06f, plinth);
                shell.Box(M["DarkSteel"], c + n * .015f + Vector3.up * 1.2f, line);
            }
            Wainscot(new Vector3(-RoomX, 0, -RoomZ), new Vector3(-RoomX, 0, RoomZ), Vector3.right);
            Wainscot(new Vector3(RoomX, 0, -RoomZ), new Vector3(RoomX, 0, RoomZ), Vector3.left);
            Wainscot(new Vector3(-RoomX, 0, RoomZ), new Vector3(1.15f, 0, RoomZ), Vector3.back);
            Wainscot(new Vector3(2.05f, 0, RoomZ), new Vector3(RoomX, 0, RoomZ), Vector3.back);
            Wainscot(new Vector3(-RoomX, 0, -RoomZ), new Vector3(-1.9f, 0, -RoomZ), Vector3.forward);
            Wainscot(new Vector3(1.9f, 0, -RoomZ), new Vector3(RoomX, 0, -RoomZ), Vector3.forward);
            shell.Box(M["Ceiling"], new Vector3(0, RoomH + .1f, 0), new Vector3(RoomX * 2 + .4f, .2f, RoomZ * 2 + .4f));
            // Двутавровые балки перекрытия поперёк помещения.
            foreach (float z in new[] { -3.6f, 0f, 3.6f })
            {
                shell.Box(M["DarkSteel"], new Vector3(0, RoomH - .01f, z), new Vector3(RoomX * 2, .02f, .18f));
                shell.Box(M["DarkSteel"], new Vector3(0, RoomH - .13f, z), new Vector3(RoomX * 2, .22f, .012f));
                shell.Box(M["DarkSteel"], new Vector3(0, RoomH - .25f, z), new Vector3(RoomX * 2, .02f, .18f));
            }
            shell.Flush(room, "Shell", false);

            // Ворота: секционные панели, ряд окон, направляющие, ручка. Порог с разметкой.
            var gate = new Kit().At(new Vector3(0, 0, -RoomZ + .04f));
            for (int i = 0; i < 5; i++)
            {
                float y = .32f + i * .64f;
                if (i == 3)
                {
                    for (int w = 0; w < 4; w++)
                    {
                        float x = -1.42f + w * .95f;
                        gate.Box(M["GatePanel"], new Vector3(x, y, 0), new Vector3(.95f, .6f, .045f));
                        gate.Box(M["Daylight"], new Vector3(x, y + .02f, .026f), new Vector3(.7f, .32f, .01f));
                    }
                }
                else gate.Box(M["GatePanel"], new Vector3(0, y, 0), new Vector3(3.8f, .6f, .045f));
                gate.Box(M["DarkSteel"], new Vector3(0, y + .3f, .03f), new Vector3(3.8f, .02f, .02f));
                for (int r = -1; r <= 1; r += 2) gate.Box(M["Steel"], new Vector3(0, y + r * .14f, .028f), new Vector3(3.76f, .015f, .01f));
            }
            foreach (float x in new[] { -1.97f, 1.97f })
                gate.Box(M["DarkSteel"], new Vector3(x, 1.6f, .06f), new Vector3(.08f, 3.2f, .1f));
            foreach (float x in new[] { -1.97f, 1.97f })
                gate.Box(M["DarkSteel"], new Vector3(x, 3.32f, 1.2f), new Vector3(.06f, .06f, 2.4f));
            gate.Box(M["Steel"], new Vector3(0, .55f, .07f), new Vector3(.35f, .04f, .04f));
            gate.Box(M["Hazard"], new Vector3(0, .003f, .3f), new Vector3(3.8f, .006f, .18f));
            gate.Flush(room, "Gate");
            Blocker(room, "GateBlocker", new Vector3(0, 1.6f, -RoomZ), new Vector3(4f, 3.2f, .3f));

            // Дверь в задней стене: коробка, полотно, ручка, табличка.
            var door = new Kit().At(new Vector3(1.6f, 0, RoomZ - .03f));
            door.Box(M["DarkSteel"], new Vector3(-.47f, 1.1f, 0), new Vector3(.06f, 2.2f, .1f));
            door.Box(M["DarkSteel"], new Vector3(.47f, 1.1f, 0), new Vector3(.06f, 2.2f, .1f));
            door.Box(M["DarkSteel"], new Vector3(0, 2.18f, 0), new Vector3(1f, .06f, .1f));
            door.Box(M["DoorPanel"], new Vector3(0, 1.07f, .01f), new Vector3(.86f, 2.1f, .04f));
            door.Box(M["DarkSteel"], new Vector3(0, 1.6f, -.012f), new Vector3(.6f, .5f, .01f));
            door.Box(M["Steel"], new Vector3(.32f, 1.0f, -.04f), new Vector3(.14f, .025f, .04f));
            door.Cyl(M["Steel"], new Vector3(.32f, 1.0f, -.012f), new Vector3(.32f, 1.0f, -.03f), .03f, 12);
            door.Flush(room, "Door");
            Blocker(room, "DoorBlocker", new Vector3(1.6f, 1.1f, RoomZ), new Vector3(1f, 2.2f, .3f));
            Label(room, "DoorSign", "ВЫХОД", new Vector3(1.6f, 2.4f, RoomZ - .02f), Quaternion.identity, 1.6f,
                new Color(.80f,.82f,.80f), new Vector2(1.2f, .3f));

            // Разметка: рамка поста, зона шиномонтажа, места стоянки у станков; пятна масла; трап.
            var marks = new Kit();
            void Rect(float x0, float x1, float z0, float z1, float w)
            {
                marks.Box(M["Stripe"], new Vector3((x0 + x1) / 2, .005f, z0), new Vector3(x1 - x0, .002f, w));
                marks.Box(M["Stripe"], new Vector3((x0 + x1) / 2, .005f, z1), new Vector3(x1 - x0, .002f, w));
                marks.Box(M["Stripe"], new Vector3(x0, .005f, (z0 + z1) / 2), new Vector3(w, .002f, z1 - z0));
                marks.Box(M["Stripe"], new Vector3(x1, .005f, (z0 + z1) / 2), new Vector3(w, .002f, z1 - z0));
            }
            Rect(-2.2f, 2.2f, -3.2f, 3.2f, .07f);
            Rect(-5.85f, -3.3f, -3.6f, 1.0f, .05f);
            foreach (float z in new[] { -2.4f, -.2f })
                for (int i = 0; i < 4; i++) marks.Box(M["Stripe"], new Vector3(-3.75f, .005f, z - .45f + i * .3f), new Vector3(.03f, .002f, .16f));
            marks.Box(M["DarkSteel"], new Vector3(0, .004f, -3.75f), new Vector3(1.4f, .006f, .2f));
            for (int i = 0; i < 18; i++) marks.Box(M["Plinth"], new Vector3(-.66f + i * .078f, .0075f, -3.75f), new Vector3(.03f, .002f, .16f));
            marks.Flush(room, "FloorMarks", false);
            var stains = new Kit();
            void Stain(float x, float z, float s, float yaw)
            {
                stains.At(new Vector3(x, .006f, z), yaw);
                stains.Quad(M["OilStain"], new Vector3(-s, 0, -s), new Vector3(s, 0, -s), new Vector3(s, 0, s), new Vector3(-s, 0, s), Vector3.up, new Vector2(1, 1));
            }
            Stain(.3f, -1.1f, .45f, 20); Stain(-.4f, .9f, .32f, 70); Stain(-4.2f, -1.4f, .3f, 10); Stain(3.4f, 3.6f, .25f, 40);
            stains.Flush(room, "OilStains", false);

            // Таблички зон (рельефные буквы на тёмной плашке).
            var signs = new Kit();
            signs.Box(M["SignPlate"], new Vector3(-RoomX + .03f, 2.75f, -1.3f), new Vector3(.03f, .42f, 2.1f));
            signs.Box(M["SignPlate"], new Vector3(.2f, 3.0f, RoomZ - .03f), new Vector3(2.3f, .42f, .03f));
            signs.Box(M["SignPlate"], new Vector3(RoomX - .03f, 2.75f, .6f), new Vector3(.03f, .36f, 1.6f));
            signs.Flush(room, "SignPlates");
            Color accent = new Color(.92f,.62f,.2f);
            Label(room, "Sign_Tire", "ШИНОМОНТАЖ", new Vector3(-RoomX + .05f, 2.75f, -1.3f), Quaternion.Euler(0, -90, 0), 2.6f, accent, new Vector2(2f, .4f));
            Label(room, "Sign_Diag", "ДИАГНОСТИКА", new Vector3(.2f, 3.0f, RoomZ - .05f), Quaternion.identity, 2.6f, accent, new Vector2(2.2f, .4f));
            Label(room, "Sign_Store", "СКЛАД", new Vector3(RoomX - .05f, 2.75f, .6f), Quaternion.Euler(0, 90, 0), 2.2f, accent, new Vector2(1.5f, .35f));
        }

        private static void BuildLighting(Transform root)
        {
            Transform lights = new GameObject("Lighting").transform; lights.SetParent(root, false);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.34f,.35f,.37f);
            RenderSettings.ambientEquatorColor = new Color(.25f,.245f,.235f);
            RenderSettings.ambientGroundColor = new Color(.13f,.125f,.12f);
            RenderSettings.fog = false;
            Color warm = new Color(1f,.88f,.74f);
            // Основной свет: мягкий направленный сверху (стены и потолок тени не отбрасывают).
            Light main = AddLight(lights, "Main Light", LightType.Directional, Vector3.zero, new Vector3(64, -28, 0),
                new Color(1f,.95f,.88f), .55f, 0, 0, LightShadows.Soft);
            main.shadowStrength = .7f;
            RenderSettings.sun = main;
            // Светильники: корпус + светящийся рассеиватель + источник света.
            var fixtures = new Kit();
            void Fixture(Vector3 p, bool alongZ)
            {
                Vector3 body = alongZ ? new Vector3(.22f,.07f,1.3f) : new Vector3(1.3f,.07f,.22f);
                Vector3 lens = alongZ ? new Vector3(.17f,.012f,1.24f) : new Vector3(1.24f,.012f,.17f);
                fixtures.Box(M["DarkSteel"], p, body);
                fixtures.Box(M["Lamp"], p + Vector3.down * .04f, lens);
                foreach (float s in new[] { -.5f, .5f })
                {
                    Vector3 off = alongZ ? new Vector3(0, 0, s) : new Vector3(s, 0, 0);
                    fixtures.Cyl(M["DarkSteel"], p + off, p + off + Vector3.up * (RoomH - p.y), .006f, 6, false);
                }
            }
            Vector3[] carLamps = { new Vector3(-1.3f,3.7f,-1.6f), new Vector3(1.3f,3.7f,1.6f) };
            foreach (Vector3 p in carLamps)
            {
                Fixture(p, true);
                AddLight(lights, "CarSpot", LightType.Spot, p + Vector3.down * .06f, new Vector3(90, 0, 0), warm, 9f, 8f, 115f, LightShadows.Soft);
            }
            Fixture(new Vector3(1.3f,3.7f,-1.6f), true); Fixture(new Vector3(-1.3f,3.7f,1.6f), true);
            AddLight(lights, "CarFill", LightType.Point, new Vector3(0,3.2f,0), Vector3.zero, warm, 2.2f, 6f, 0, LightShadows.None);
            Fixture(new Vector3(-4.4f,3.7f,-1.2f), true);
            AddLight(lights, "TireZoneSpot", LightType.Spot, new Vector3(-4.4f,3.64f,-1.2f), new Vector3(90,0,0), warm, 7f, 7f, 120f, LightShadows.Soft);
            Fixture(new Vector3(4.5f,3.7f,1.0f), true);
            AddLight(lights, "StorageSpot", LightType.Spot, new Vector3(4.5f,3.64f,1.0f), new Vector3(90,0,0), warm, 5.5f, 7f, 120f, LightShadows.None);
            Fixture(new Vector3(-2.6f,3.7f,4.2f), false);
            AddLight(lights, "BenchSpot", LightType.Spot, new Vector3(-2.6f,3.64f,4.2f), new Vector3(90,0,0), warm, 5f, 6f, 115f, LightShadows.None);
            AddLight(lights, "EngineBayFill", LightType.Point, new Vector3(-.2f,2.2f,-2.6f), Vector3.zero, new Color(1f,.93f,.84f), 1.4f, 3.2f, 0, LightShadows.None);
            fixtures.Flush(lights, "Fixtures", false);
            // Отражения: один realtime-зонд на всё помещение, рендер один раз при запуске.
            GameObject probeObject = new GameObject("ReflectionProbe"); probeObject.transform.SetParent(lights, false);
            probeObject.transform.localPosition = new Vector3(0, 1.8f, 0);
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(RoomX * 2, RoomH, RoomZ * 2); probe.boxProjection = true; probe.resolution = 128;
            probe.intensity = .9f;
            // Умеренная цветокоррекция без DOF, виньетки и сильного bloom.
            GameObject volumeObject = new GameObject("PostVolume"); volumeObject.transform.SetParent(lights, false);
            Volume volume = volumeObject.AddComponent<Volume>(); volume.isGlobal = true;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>(); profile.name = "WorkshopProfile";
            AssetDatabase.CreateAsset(profile, output + "/WorkshopProfile.asset");
            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(8f); color.saturation.Override(-6f); color.postExposure.Override(.1f);
            WhiteBalance white = profile.Add<WhiteBalance>(true); white.temperature.Override(4f);
            foreach (VolumeComponent c in profile.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile);
            volume.sharedProfile = profile;
        }

        // ---------- Зоны мастерской. Каждая группа: свой Transform (позиция + поворот), фронт смотрит в локальный +Z ----------

        private static Transform Group(Transform root, string name, Vector3 pos, float yaw)
        {
            Transform g = new GameObject(name).transform; g.SetParent(root, false);
            g.localPosition = pos; g.localRotation = Quaternion.Euler(0, yaw, 0);
            return g;
        }

        private static void Wrench(Kit k, Vector3 p, float len, float tilt)
        {
            k.Box(M["Steel"], p, new Vector3(.022f, len, .008f), new Vector3(0, 0, tilt));
            k.Cyl(M["Steel"], p + Quaternion.Euler(0, 0, tilt) * Vector3.up * len * .5f + new Vector3(0, 0, -.004f),
                p + Quaternion.Euler(0, 0, tilt) * Vector3.up * len * .5f + new Vector3(0, 0, .004f), .02f, 10);
        }

        private static void BuildBench(Transform root)
        {
            Transform g = Group(root, "Zone_Workbench", new Vector3(-3.2f, 0, RoomZ - .4f), 180);
            var k = new Kit();
            // Каркас, столешница из бруса, тумба с ящиками, нижняя полка.
            foreach (float x in new[] { -1.05f, 1.05f }) foreach (float z in new[] { -.3f, .3f })
                k.Box(M["DarkSteel"], new Vector3(x, .43f, z), new Vector3(.05f, .86f, .05f));
            k.Box(M["DarkSteel"], new Vector3(0, .84f, 0), new Vector3(2.15f, .04f, .64f));
            k.Box(M["Wood"], new Vector3(0, .885f, 0), new Vector3(2.2f, .05f, .7f));
            k.Box(M["DarkSteel"], new Vector3(0, .16f, 0), new Vector3(2.1f, .025f, .6f));
            k.Box(M["MachineBody"], new Vector3(.62f, .45f, .02f), new Vector3(.72f, .74f, .62f));
            for (int i = 0; i < 4; i++)
            {
                float y = .2f + i * .175f;
                k.Box(M["DarkSteel"], new Vector3(.62f, y, .335f), new Vector3(.68f, .15f, .012f));
                k.Box(M["Steel"], new Vector3(.62f, y + .03f, .345f), new Vector3(.22f, .02f, .02f));
            }
            // Ящики с деталями на нижней полке.
            k.Box(M["Cardboard"], new Vector3(-.65f, .29f, .02f), new Vector3(.5f, .24f, .4f));
            k.Box(M["BinPlastic"], new Vector3(-.15f, .25f, .05f), new Vector3(.34f, .16f, .3f));
            // Тиски на левом краю.
            k.Box(M["Accent"], new Vector3(-.85f, .94f, .22f), new Vector3(.16f, .07f, .22f));
            k.Box(M["Accent"], new Vector3(-.85f, 1.02f, .27f), new Vector3(.18f, .1f, .07f));
            k.Box(M["Accent"], new Vector3(-.85f, 1.02f, .14f), new Vector3(.18f, .1f, .06f));
            k.Box(M["Steel"], new Vector3(-.85f, 1.06f, .205f), new Vector3(.17f, .02f, .03f));
            k.Cyl(M["Steel"], new Vector3(-.85f, .99f, .31f), new Vector3(-.85f, .99f, .44f), .012f, 8);
            k.Cyl(M["Steel"], new Vector3(-.93f, .99f, .44f), new Vector3(-.77f, .99f, .44f), .008f, 8);
            // Мультиметр, кейс, ветошь, кружка.
            k.Box(M["Accent"], new Vector3(.1f, .93f, .12f), new Vector3(.1f, .035f, .18f));
            k.Box(M["Screen"], new Vector3(.1f, .949f, .08f), new Vector3(.07f, .004f, .05f));
            k.Cyl(M["Rubber"], new Vector3(.16f, .915f, .05f), new Vector3(.36f, .915f, .02f), .004f, 6, false);
            k.Box(M["MachineBody"], new Vector3(.55f, .955f, -.05f), new Vector3(.48f, .09f, .3f));
            k.Box(M["Steel"], new Vector3(.55f, 1.0f, .1f), new Vector3(.12f, .02f, .02f));
            k.Box(M["Cardboard"], new Vector3(-.35f, .918f, .05f), new Vector3(.26f, .016f, .2f), new Vector3(0, 18, 0));
            k.Cyl(M["CanOlive"], new Vector3(.95f, .91f, .15f), new Vector3(.95f, 1.0f, .15f), .04f, 14);
            // Перфопанель с инструментом и лампа.
            k.Box(M["Pegboard"], new Vector3(0, 1.55f, -.33f), new Vector3(2.1f, .95f, .02f));
            k.Box(M["DarkSteel"], new Vector3(0, 2.04f, -.31f), new Vector3(2.14f, .03f, .04f));
            for (int i = 0; i < 6; i++) Wrench(k, new Vector3(-.85f + i * .07f, 1.55f - i * .02f, -.31f), .16f + i * .025f, 0);
            for (int i = 0; i < 5; i++)
            {
                float x = -.25f + i * .06f;
                k.Cyl(M["Steel"], new Vector3(x, 1.38f, -.31f), new Vector3(x, 1.55f, -.31f), .004f, 6);
                k.Cyl(i % 2 == 0 ? M["Accent"] : M["Rubber"], new Vector3(x, 1.55f, -.31f), new Vector3(x, 1.67f, -.31f), .013f, 10);
            }
            k.Box(M["Wood"], new Vector3(.35f, 1.5f, -.31f), new Vector3(.03f, .3f, .03f));
            k.Box(M["DarkSteel"], new Vector3(.35f, 1.66f, -.31f), new Vector3(.12f, .04f, .04f));
            k.Box(M["Accent"], new Vector3(.6f, 1.55f, -.31f), new Vector3(.04f, .26f, .025f), new Vector3(0, 0, 12));
            k.Box(M["Accent"], new Vector3(.68f, 1.55f, -.31f), new Vector3(.04f, .26f, .025f), new Vector3(0, 0, -12));
            k.Box(M["DarkSteel"], new Vector3(.9f, 1.85f, -.29f), new Vector3(.3f, .06f, .06f));
            for (int i = 0; i < 3; i++) k.Box(M["BinPlastic"], new Vector3(.82f + i * .09f, 1.25f, -.27f), new Vector3(.08f, .08f, .1f));
            k.Cyl(M["DarkSteel"], new Vector3(1.0f, .91f, -.22f), new Vector3(1.0f, 1.32f, -.05f), .01f, 8);
            k.Cyl(M["DarkSteel"], new Vector3(1.0f, 1.32f, -.05f), new Vector3(.8f, 1.38f, .12f), .01f, 8);
            k.Cyl(M["DarkSteel"], new Vector3(.8f, 1.38f, .12f), new Vector3(.76f, 1.3f, .15f), .05f, 14, true, .075f);
            k.Cyl(M["Lamp"], new Vector3(.76f, 1.299f, .15f), new Vector3(.758f, 1.296f, .152f), .07f, 14);
            k.Flush(g, "Workbench");
            Blocker(g, "BenchBlocker", new Vector3(0, .5f, 0), new Vector3(2.25f, 1f, .75f));
            AddLight(g, "BenchLamp", LightType.Point, new Vector3(.76f, 1.22f, .16f), Vector3.zero, new Color(1f,.86f,.68f), 1.1f, 1.8f, 0, LightShadows.None);

            // Компрессор с ресивером и огнетушитель у двери.
            Transform c = Group(root, "Compressor", new Vector3(-5.35f, 0, 4.6f), 90);
            var kc = new Kit();
            kc.Cyl(M["Accent"], new Vector3(-.45f, .32f, 0), new Vector3(.45f, .32f, 0), .22f, 22);
            foreach (float x in new[] { -.3f, .3f }) kc.Box(M["DarkSteel"], new Vector3(x, .07f, 0), new Vector3(.06f, .14f, .4f));
            kc.Box(M["MachineBody"], new Vector3(.05f, .64f, 0), new Vector3(.36f, .22f, .26f));
            kc.Cyl(M["DarkSteel"], new Vector3(-.25f, .58f, 0), new Vector3(-.25f, .8f, 0), .08f, 14);
            kc.Cyl(M["Steel"], new Vector3(.3f, .62f, .1f), new Vector3(.3f, .62f, .14f), .04f, 14);
            kc.Cyl(M["Rubber"], new Vector3(.3f, .55f, .12f), new Vector3(.36f, .05f, .3f), .008f, 6, false);
            kc.Flush(c, "Unit");
            Blocker(c, "Blocker", new Vector3(0, .45f, 0), new Vector3(1f, .9f, .5f));
            Transform e = Group(root, "Extinguisher", new Vector3(.9f, 0, RoomZ - .12f), 180);
            var ke = new Kit();
            ke.Cyl(M["SafetyRed"], new Vector3(0, .02f, 0), new Vector3(0, .5f, 0), .085f, 18);
            ke.Cyl(M["DarkSteel"], new Vector3(0, .5f, 0), new Vector3(0, .58f, 0), .03f, 10);
            ke.Box(M["DarkSteel"], new Vector3(.04f, .6f, 0), new Vector3(.12f, .02f, .03f));
            ke.Cyl(M["Rubber"], new Vector3(-.03f, .57f, 0), new Vector3(-.09f, .2f, .02f), .01f, 8, false);
            ke.Box(M["SignPlate"], new Vector3(0, 1.1f, .09f), new Vector3(.22f, .22f, .01f));
            ke.Flush(e, "Unit");
            Blocker(e, "Blocker", new Vector3(0, .3f, 0), new Vector3(.24f, .6f, .24f));
        }

        private static void SpareWheel(Transform parent, Refs refs, Vector3 pos, Quaternion rot)
        {
            GameObject w = new GameObject("SpareWheel"); w.transform.SetParent(parent, false);
            w.transform.localPosition = pos; w.transform.localRotation = rot;
            for (int i = 0; i < refs.spareWheel.Count; i++)
            {
                GameObject p = new GameObject(refs.spareWheelMats[i].name, typeof(MeshFilter), typeof(MeshRenderer));
                p.transform.SetParent(w.transform, false);
                p.GetComponent<MeshFilter>().sharedMesh = refs.spareWheel[i];
                p.GetComponent<MeshRenderer>().sharedMaterial = refs.spareWheelMats[i];
            }
        }

        private static void BuildStorage(Transform root, Refs refs)
        {
            // Стеллаж с канистрами и коробками у правой стены.
            Transform g = Group(root, "Zone_Storage_Rack", new Vector3(RoomX - .35f, 0, 2.3f), -90);
            var k = new Kit();
            foreach (float x in new[] { -.95f, .95f }) foreach (float z in new[] { -.25f, .25f })
                k.Box(M["Accent"], new Vector3(x, 1.0f, z), new Vector3(.05f, 2f, .05f));
            float[] shelves = { .12f, .75f, 1.35f, 1.95f };
            foreach (float y in shelves)
            {
                k.Box(M["DarkSteel"], new Vector3(0, y, 0), new Vector3(1.95f, .03f, .52f));
                k.Box(M["Accent"], new Vector3(0, y - .03f, .25f), new Vector3(1.95f, .04f, .02f));
            }
            void Canister(Vector3 p, Material m)
            {
                k.Box(m, p + new Vector3(0, .16f, 0), new Vector3(.17f, .32f, .3f));
                k.Box(m, p + new Vector3(0, .345f, -.05f), new Vector3(.05f, .05f, .14f));
                k.Cyl(M["DarkSteel"], p + new Vector3(0, .32f, .1f), p + new Vector3(0, .37f, .1f), .025f, 10);
            }
            Canister(new Vector3(-.75f, .135f, 0), M["CanRed"]); Canister(new Vector3(-.53f, .135f, 0), M["CanRed"]);
            Canister(new Vector3(-.3f, .135f, 0), M["CanOlive"]);
            for (int i = 0; i < 5; i++)
                k.Cyl(i == 2 ? M["Accent"] : M["MachineBody"], new Vector3(.1f + i * .14f, .135f, .05f), new Vector3(.1f + i * .14f, .37f, .05f), .055f, 14);
            k.Box(M["Cardboard"], new Vector3(-.5f, .9f, 0), new Vector3(.7f, .28f, .42f));
            k.Box(M["Cardboard"], new Vector3(-.55f, 1.12f, .02f), new Vector3(.5f, .16f, .36f), new Vector3(0, 6, 0));
            k.Box(M["Cardboard"], new Vector3(.35f, .88f, 0), new Vector3(.5f, .24f, .4f));
            for (int i = 0; i < 4; i++) k.Box(M["BinPlastic"], new Vector3(-.7f + i * .45f, 1.46f, .03f), new Vector3(.38f, .2f, .42f));
            for (int i = 0; i < 4; i++)
                k.Cyl(M["CanOlive"], new Vector3(-.6f + i * .14f, 1.965f, 0), new Vector3(-.6f + i * .14f, 2.2f, 0), .05f, 12);
            k.Box(M["Cardboard"], new Vector3(.5f, 2.08f, 0), new Vector3(.55f, .24f, .4f));
            k.Flush(g, "Rack");
            Blocker(g, "Blocker", new Vector3(0, 1.1f, 0), new Vector3(2f, 2.2f, .58f));

            // Стойка с запасными колёсами того же вида, что на машине.
            Transform t = Group(root, "Zone_Storage_Tires", new Vector3(RoomX - .42f, 0, -1.4f), -90);
            var kt = new Kit();
            foreach (float x in new[] { -.95f, .95f })
            {
                kt.Box(M["DarkSteel"], new Vector3(x, .6f, -.2f), new Vector3(.05f, 1.2f, .05f));
                kt.Box(M["DarkSteel"], new Vector3(x, .6f, .2f), new Vector3(.05f, 1.2f, .05f));
                kt.Box(M["DarkSteel"], new Vector3(x, .02f, 0), new Vector3(.08f, .04f, .5f));
            }
            foreach (float z in new[] { -.13f, .13f }) kt.Cyl(M["Steel"], new Vector3(-.95f, .4f, z), new Vector3(.95f, .4f, z), .02f, 10);
            foreach (float z in new[] { -.2f, .2f }) kt.Cyl(M["DarkSteel"], new Vector3(-.95f, 1.15f, z), new Vector3(.95f, 1.15f, z), .016f, 8);
            kt.Flush(t, "Rack");
            for (int i = 0; i < 4; i++) SpareWheel(t, refs, new Vector3(-.66f + i * .44f, .66f, 0), Quaternion.identity);
            Label(t, "Tag", "ЗИМА 205/55 R16", new Vector3(0, 1.3f, .23f), Quaternion.Euler(0, 180, 0), 1.1f, new Color(.78f,.8f,.78f), new Vector2(1.2f, .2f));
            Blocker(t, "Blocker", new Vector3(0, .65f, 0), new Vector3(2f, 1.3f, .55f));

            // Инструментальная тележка и шкаф в углу.
            Transform cart = Group(root, "ToolCart", new Vector3(3.65f, 0, 4.4f), 200);
            var kc = new Kit();
            kc.Box(M["Accent"], new Vector3(0, .5f, 0), new Vector3(.75f, .72f, .45f));
            kc.Box(M["Rubber"], new Vector3(0, .875f, 0), new Vector3(.78f, .03f, .48f));
            for (int i = 0; i < 5; i++)
            {
                float y = .23f + i * .13f;
                kc.Box(M["DarkSteel"], new Vector3(0, y + .065f, .226f), new Vector3(.71f, .006f, .004f));
                kc.Box(M["Steel"], new Vector3(0, y + .02f, .232f), new Vector3(.4f, .018f, .012f));
            }
            foreach (float x in new[] { -.32f, .32f }) foreach (float z in new[] { -.17f, .17f })
            {
                kc.Box(M["DarkSteel"], new Vector3(x, .1f, z), new Vector3(.03f, .04f, .03f));
                kc.Cyl(M["Rubber"], new Vector3(x - .02f, .05f, z), new Vector3(x + .02f, .05f, z), .05f, 12);
            }
            kc.Cyl(M["Steel"], new Vector3(-.42f, .8f, -.15f), new Vector3(-.42f, .8f, .15f), .012f, 8);
            Wrench(kc, new Vector3(.1f, .895f, 0), .24f, 90);
            kc.Box(M["BinPlastic"], new Vector3(-.15f, .92f, .05f), new Vector3(.22f, .06f, .14f));
            kc.Flush(cart, "Cart");
            Blocker(cart, "Blocker", new Vector3(0, .45f, 0), new Vector3(.85f, .9f, .52f));
            Transform cab = Group(root, "Cabinet", new Vector3(RoomX - .3f, 0, 4.55f), -90);
            var kb = new Kit();
            kb.Box(M["MachineBody"], new Vector3(0, .95f, 0), new Vector3(.9f, 1.9f, .5f));
            kb.Box(M["DarkSteel"], new Vector3(0, .95f, .252f), new Vector3(.006f, 1.82f, .004f));
            foreach (float x in new[] { -.06f, .06f }) kb.Box(M["Steel"], new Vector3(x, 1.05f, .265f), new Vector3(.02f, .18f, .025f));
            kb.Box(M["SignPlate"], new Vector3(-.22f, 1.5f, .253f), new Vector3(.2f, .12f, .004f));
            kb.Flush(cab, "Cabinet");
            Blocker(cab, "Blocker", new Vector3(0, .95f, 0), new Vector3(.92f, 1.9f, .52f));
        }

        private static void BuildLift(Transform root)
        {
            Transform g = Group(root, "Lift_2Post", Vector3.zero, 0);
            var k = new Kit();
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * 1.85f;
                k.Box(M["DarkSteel"], new Vector3(x, .01f, 0), new Vector3(.5f, .02f, .55f));
                k.Box(M["Accent"], new Vector3(x, 1.5f, 0), new Vector3(.26f, 3f, .3f));
                k.Box(M["Hazard"], new Vector3(x - s * .131f, .35f, 0), new Vector3(.004f, .5f, .3f));
                k.Box(M["DarkSteel"], new Vector3(x - s * .135f, 1.5f, 0), new Vector3(.01f, 2.6f, .08f));
                k.Box(M["Steel"], new Vector3(x - s * .2f, .22f, 0), new Vector3(.14f, .12f, .26f));
                // Сложенные лапы вдоль порога.
                foreach (float z in new[] { -1f, 1f })
                {
                    k.Box(M["DarkSteel"], new Vector3(x - s * .3f, .16f, z * .5f), new Vector3(.08f, .06f, 1f), new Vector3(0, -s * z * 8f, 0));
                    k.Cyl(M["Rubber"], new Vector3(x - s * .36f, .19f, z * .96f), new Vector3(x - s * .36f, .23f, z * .96f), .06f, 14);
                }
            }
            k.Box(M["DarkSteel"], new Vector3(0, 3.06f, 0), new Vector3(3.96f, .12f, .2f));
            k.Cyl(M["Rubber"], new Vector3(-1.85f, 2.9f, .14f), new Vector3(1.85f, 2.9f, .14f), .012f, 6, false);
            k.Box(M["MachineBody"], new Vector3(2.08f, 1.3f, .1f), new Vector3(.2f, .3f, .14f));
            k.Box(M["SafetyRed"], new Vector3(2.18f, 1.34f, .1f), new Vector3(.012f, .05f, .05f));
            k.Flush(g, "Lift");
            foreach (float s in new[] { -1f, 1f }) Blocker(g, "PostBlocker", new Vector3(s * 1.88f, 1.5f, 0), new Vector3(.36f, 3f, .4f));
        }

        private static void BuildTireChanger(Transform root, Refs refs)
        {
            Transform g = Group(root, "Zone_TireChanger", new Vector3(-4.75f, 0, -2.4f), 90);
            var k = new Kit();
            k.Box(M["MachineBody"], new Vector3(0, .36f, 0), new Vector3(.62f, .72f, .78f));
            k.Box(M["Accent"], new Vector3(0, .69f, 0), new Vector3(.64f, .05f, .8f));
            k.Box(M["DarkSteel"], new Vector3(0, .05f, 0), new Vector3(.7f, .1f, .86f));
            foreach (float x in new[] { -.15f, 0f, .15f })
            {
                k.Box(M["DarkSteel"], new Vector3(x, .04f, .48f), new Vector3(.08f, .025f, .14f));
                k.Box(M["Rubber"], new Vector3(x, .056f, .5f), new Vector3(.07f, .006f, .1f));
            }
            // Колонна, рука и монтажная головка.
            k.Box(M["Accent"], new Vector3(0, 1.15f, -.33f), new Vector3(.18f, .9f, .18f));
            k.Box(M["DarkSteel"], new Vector3(0, 1.62f, -.33f), new Vector3(.22f, .08f, .22f));
            k.Box(M["Steel"], new Vector3(0, 1.58f, -.06f), new Vector3(.08f, .08f, .58f));
            k.Cyl(M["Steel"], new Vector3(0, 1.58f, .18f), new Vector3(0, 1.08f, .18f), .028f, 12);
            k.Box(M["DarkSteel"], new Vector3(0, 1.05f, .2f), new Vector3(.06f, .07f, .1f));
            // Отжимная лопатка сбоку.
            k.Box(M["DarkSteel"], new Vector3(.36f, .55f, -.2f), new Vector3(.06f, .5f, .06f), new Vector3(-25, 0, 0));
            k.Box(M["Steel"], new Vector3(.36f, .6f, .1f), new Vector3(.05f, .05f, .5f));
            k.Box(M["Rubber"], new Vector3(.4f, .3f, .2f), new Vector3(.03f, .2f, .16f));
            k.Box(M["Screen"], new Vector3(-.2f, .5f, .392f), new Vector3(.18f, .1f, .004f));
            k.Flush(g, "Body");
            // Поворотный стол (вращается при демонтаже/монтаже), на нём точка для колеса.
            Transform table = new GameObject("Turntable").transform; table.SetParent(g, false); table.localPosition = new Vector3(0, .72f, .07f);
            var kt = new Kit();
            kt.Cyl(M["Steel"], Vector3.zero, new Vector3(0, .04f, 0), .3f, 28);
            for (int i = 0; i < 4; i++)
            {
                Quaternion q = Quaternion.Euler(0, i * 90, 0);
                kt.Box(M["DarkSteel"], q * new Vector3(0, .055f, .22f), new Vector3(.05f, .03f, .1f), new Vector3(0, i * 90, 0));
            }
            kt.Cyl(M["DarkSteel"], new Vector3(0, .04f, 0), new Vector3(0, .1f, 0), .04f, 12);
            kt.Flush(table, "Table");
            Transform slot = new GameObject("WheelSlot_Changer").transform; slot.SetParent(table, false);
            slot.localPosition = new Vector3(0, .16f, 0); slot.localRotation = Quaternion.Euler(0, 0, 90);
            refs.changerTable = table; refs.changerSlot = slot;
            TextMeshPro tag = Label(g, "TireTag", "", new Vector3(0, 1.3f, -.235f), Quaternion.Euler(0, 180, 0), 1.2f, new Color(.92f,.62f,.2f), new Vector2(1f, .2f));
            refs.tag = tag;
            Blocker(g, "Blocker", new Vector3(0, .6f, -.05f), new Vector3(.75f, 1.2f, .9f));
            Point(g, "Point_TireChanger", WorkshopPoint.TireChanger, new Vector3(0, .95f, .12f), new Vector3(1.1f, 1.1f, 1.0f));
        }

        private static void BuildBalancer(Transform root, Refs refs)
        {
            Transform g = Group(root, "Zone_Balancer", new Vector3(-4.85f, 0, .25f), 90);
            var k = new Kit();
            k.Box(M["MachineBody"], new Vector3(-.15f, .42f, -.05f), new Vector3(.6f, .84f, .5f));
            k.Box(M["Accent"], new Vector3(-.15f, .86f, -.05f), new Vector3(.62f, .04f, .52f));
            k.Box(M["DarkSteel"], new Vector3(-.15f, .04f, -.05f), new Vector3(.66f, .08f, .56f));
            // Лоток с грузиками: три ячейки - как варианты массы в заказе.
            k.Box(M["DarkSteel"], new Vector3(-.15f, .9f, .05f), new Vector3(.5f, .04f, .26f));
            for (int i = 0; i < 3; i++)
            {
                k.Box(M["Steel"], new Vector3(-.32f + i * .17f, .93f, .05f), new Vector3(.1f, .02f, .03f + i * .012f));
                k.Box(M["Steel"], new Vector3(-.32f + i * .17f, .93f, .11f), new Vector3(.1f, .02f, .03f + i * .012f));
            }
            // Стойка с дисплеем.
            k.Box(M["DarkSteel"], new Vector3(-.15f, 1.15f, -.27f), new Vector3(.08f, .55f, .06f));
            k.Box(M["MachineBody"], new Vector3(-.15f, 1.45f, -.24f), new Vector3(.5f, .26f, .08f), new Vector3(-12, 0, 0));
            k.Box(M["Screen"], new Vector3(-.15f, 1.455f, -.195f), new Vector3(.42f, .18f, .004f), new Vector3(-12, 0, 0));
            // Защитный кожух над колесом (поднят).
            k.Box(M["Accent"], new Vector3(.38f, 1.2f, -.28f), new Vector3(.5f, .06f, .3f), new Vector3(-50, 0, 0));
            k.Cyl(M["DarkSteel"], new Vector3(.15f, .62f, 0), new Vector3(.2f, .62f, 0), .07f, 16);
            k.Flush(g, "Body");
            Transform shaft = new GameObject("Shaft").transform; shaft.SetParent(g, false); shaft.localPosition = new Vector3(.2f, .62f, 0);
            var ks = new Kit();
            ks.Cyl(M["Steel"], Vector3.zero, new Vector3(.36f, 0, 0), .02f, 12);
            ks.Cyl(M["DarkSteel"], new Vector3(.05f, 0, 0), new Vector3(.08f, 0, 0), .09f, 18);
            ks.Cyl(M["DarkSteel"], new Vector3(.3f, 0, 0), new Vector3(.36f, 0, 0), .05f, 10, true, .035f);
            ks.Flush(shaft, "Shaft");
            Transform slot = new GameObject("WheelSlot_Balancer").transform; slot.SetParent(shaft, false); slot.localPosition = new Vector3(.2f, 0, 0);
            refs.balancerShaft = shaft; refs.balancerSlot = slot;
            TextMeshPro readout = Label(g, "Readout", "", new Vector3(-.15f, 1.46f, -.188f), Quaternion.Euler(12, 180, 0), 1.5f, new Color(.55f,.95f,.62f), new Vector2(.4f, .16f));
            refs.readout = readout;
            Blocker(g, "Blocker", new Vector3(-.05f, .6f, -.05f), new Vector3(.85f, 1.2f, .6f));
            Point(g, "Point_Balancer", WorkshopPoint.Balancer, new Vector3(.1f, .95f, .15f), new Vector3(1.1f, 1.1f, 1.0f));
        }

        private static void SetupPipeline()
{
    UniversalRendererData renderer =
        ScriptableObject.CreateInstance<UniversalRendererData>();

    renderer.name = "Diagnoz_Renderer";

    AssetDatabase.CreateAsset(
        renderer,
        output + "/Renderer.asset");

    UniversalRenderPipelineAsset pipeline =
        UniversalRenderPipelineAsset.Create(renderer);

    if (pipeline == null)
    {
        throw new InvalidOperationException(
            "Не удалось создать UniversalRenderPipelineAsset.");
    }

    pipeline.name = "Diagnoz_URP";
    pipeline.msaaSampleCount = 4;
    pipeline.supportsHDR = false;
    pipeline.shadowDistance = 24f;

    AssetDatabase.CreateAsset(
        pipeline,
        output + "/URP.asset");

    AssetDatabase.SaveAssets();

    // Используем штатную инициализацию глобальных настроек URP.
    // Метод protected, поэтому вызывается через reflection:
    // прямой зависимости от внутреннего класса GlobalSettings здесь нет.
    System.Reflection.MethodInfo ensureGlobalSettings =
        typeof(UniversalRenderPipelineAsset).GetMethod(
            "EnsureGlobalSettings",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

    if (ensureGlobalSettings == null)
    {
        throw new InvalidOperationException(
            "В установленной версии URP не найден метод " +
            "EnsureGlobalSettings. Настройка остановлена.");
    }

    try
    {
        ensureGlobalSettings.Invoke(pipeline, null);
    }
    catch (System.Reflection.TargetInvocationException exception)
    {
        throw new InvalidOperationException(
            "URP не удалось подготовить глобальные настройки. " +
            "Подробности находятся во вложенном исключении.",
            exception.InnerException ?? exception);
    }

    GraphicsSettings.defaultRenderPipeline = pipeline;
    QualitySettings.renderPipeline = pipeline;

    EditorUtility.SetDirty(renderer);
    EditorUtility.SetDirty(pipeline);

    AssetDatabase.SaveAssets();
}

        private static int EnsureLayer()
        {
            int existing = LayerMask.NameToLayer(LayerName); if (existing >= 0) return existing;
            for (int i = 8; i < 32; i++)
            {
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(i))) continue;
                SerializedObject settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                settings.FindProperty("layers").GetArrayElementAtIndex(i).stringValue = LayerName;
                settings.ApplyModifiedProperties(); return i;
            }
            Dialog("Нет свободного пользовательского слоя."); return -1;
        }

        private static TMP_FontAsset PrepareFont(Font source)
        {
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, false);
            if (font == null) throw new InvalidOperationException("Не создан TMP-шрифт.");
            StringBuilder chars = new StringBuilder();
            for (int i=32; i<=126; i++) chars.Append((char)i);
            for (int i=0x410; i<=0x44f; i++) chars.Append((char)i);
            chars.Append("Ёё—·№«»");
            if (!font.TryAddCharacters(chars.ToString(), out string missing))
                throw new InvalidOperationException("Нет символов шрифта: " + missing);
            font.name = "Diagnoz_Russian"; font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, output + "/Diagnoz_Russian.asset");
            foreach (Texture2D texture in font.atlasTextures)
            { texture.hideFlags = HideFlags.None; AssetDatabase.AddObjectToAsset(texture, font); EditorUtility.SetDirty(texture); }
            font.material.hideFlags = HideFlags.None; AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font.material); EditorUtility.SetDirty(font);
            return font;
        }

        private static EventSystem BuildEventSystem(Transform root)
        {
            InputActionAsset actions=ScriptableObject.CreateInstance<InputActionAsset>(); actions.name="DiagnozUI";
            InputActionMap map=actions.AddActionMap("UI");
            InputAction point=map.AddAction("Point",InputActionType.PassThrough); point.expectedControlType="Vector2"; point.AddBinding("<Mouse>/position");
            InputAction click=map.AddAction("Click",InputActionType.PassThrough); click.expectedControlType="Button"; click.AddBinding("<Mouse>/leftButton");
            InputAction right=map.AddAction("RightClick",InputActionType.PassThrough); right.expectedControlType="Button"; right.AddBinding("<Mouse>/rightButton");
            InputAction scroll=map.AddAction("Scroll",InputActionType.PassThrough); scroll.expectedControlType="Vector2"; scroll.AddBinding("<Mouse>/scroll");
            AssetDatabase.CreateAsset(actions,output+"/UIActions.asset");
            GameObject item=new GameObject("EventSystem"); item.transform.SetParent(root,false);
            EventSystem events=item.AddComponent<EventSystem>(); events.sendNavigationEvents=false;
            InputSystemUIInputModule module=item.AddComponent<InputSystemUIInputModule>();
            module.actionsAsset=actions; module.point=Reference(actions,point); module.leftClick=Reference(actions,click);
            module.rightClick=Reference(actions,right); module.scrollWheel=Reference(actions,scroll);
            EditorUtility.SetDirty(actions); return events;
        }
        private static InputActionReference Reference(InputActionAsset asset, InputAction action)
        {
            InputActionReference reference=InputActionReference.Create(action); reference.name=action.name;
            AssetDatabase.AddObjectToAsset(reference,asset); return reference;
        }

        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            int slash=path.LastIndexOf('/'); string parent=path.Substring(0,slash); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent,path.Substring(slash+1));
        }

        // ---------- Игрок, единственная камера, UI, логика мастерской, звук, проверка сцены ----------

        private static void BuildSystems(Transform root, Scene scene, int layer, Refs refs)
        {
            GameObject playerObject = new GameObject("Player", typeof(CharacterController));
            playerObject.transform.SetParent(root, false);
            playerObject.transform.localPosition = new Vector3(2.6f, .02f, -3.6f);
            playerObject.transform.localRotation = Quaternion.Euler(0, -36, 0);
            CharacterController body = playerObject.GetComponent<CharacterController>();
            body.height = 1.75f; body.radius = .3f; body.center = new Vector3(0, .875f, 0); body.stepOffset = .25f; body.skinWidth = .03f;
            playerObject.layer = PropLayer;
            Transform head = new GameObject("Head").transform; head.SetParent(playerObject.transform, false); head.localPosition = new Vector3(0, 1.62f, 0);
            PlayerController player = playerObject.AddComponent<PlayerController>();

            // Одна камера на всю сцену: в ходьбе её ставит PlayerController, в осмотре - OrbitCamera.
            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
            cameraObject.transform.SetParent(root, false); cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(playerObject.transform.position + Vector3.up * 1.62f, playerObject.transform.rotation);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 62; camera.nearClipPlane = .05f; camera.farClipPlane = 60;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f,.075f,.08f);
            UniversalAdditionalCameraData cameraData = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true; cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            player.Configure(camera, head);
            Transform hands = new GameObject("HandsSlot").transform; hands.SetParent(cameraObject.transform, false);
            hands.localPosition = new Vector3(.30f, -.50f, .95f); hands.localRotation = Quaternion.Euler(0, 60, 0);
            Transform target = new GameObject("CameraTarget").transform; target.SetParent(root, false); target.localPosition = new Vector3(0, .9f, -.3f);
            OrbitCamera orbit = cameraObject.AddComponent<OrbitCamera>();
            orbit.enabled = false;

            GameObject systems = new GameObject("Interaction"); systems.transform.SetParent(root, false);
            InteractionController interaction = systems.AddComponent<InteractionController>();
            interaction.enabled = false;
            EventSystem events = BuildEventSystem(root);
            GameObject ui = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ui.transform.SetParent(root, false); ui.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = ui.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 810); scaler.matchWidthOrHeight = .5f;
            UiInputBlocker blocker = ui.AddComponent<UiInputBlocker>();
            blocker.Configure(ui.GetComponent<GraphicRaycaster>(), events);
            orbit.Configure(target, blocker);
            orbit.SetLimits(2.4f, 4.6f, 3.9f, 36f, 30f);
            interaction.Configure(camera, blocker, 1 << layer, Physics.DefaultRaycastLayers);
            BatteryOrderDefinition definition = ScriptableObject.CreateInstance<BatteryOrderDefinition>();
            AssetDatabase.CreateAsset(definition, output + "/BatteryOrder.asset");
            BatteryOrderSession session = ui.AddComponent<BatteryOrderSession>();
            session.Configure(definition, refs.groups["Node_Battery"].GetComponent<DiagnosticNode>());
            BatteryOrderUI orderUI = ui.AddComponent<BatteryOrderUI>();
            orderUI.Build(session, interaction, blocker, orbit, uiFont);

            GameObject workshop = new GameObject("Workshop"); workshop.transform.SetParent(root, false);
            WorkshopDirector director = workshop.AddComponent<WorkshopDirector>();
            director.Configure(session, orderUI, player, orbit, interaction, blocker);
            WheelServiceView wheelView = workshop.AddComponent<WheelServiceView>();
            wheelView.Configure(session, refs.wheelRoot, refs.carSlot, hands, refs.changerSlot, refs.balancerSlot,
                refs.summer, refs.winter, refs.stand, refs.changerTable, refs.balancerShaft, refs.readout, refs.tag);
            GameObject audioObject = new GameObject("Audio"); audioObject.transform.SetParent(root, false);
            WorkshopAudio audio = audioObject.AddComponent<WorkshopAudio>();
            orderUI.BindWorkshop(director, wheelView, audio);

            root.gameObject.SetActive(true);
            Physics.SyncTransforms();
            ValidateScene(scene, refs, playerObject.transform.position);
        }

        private static void ValidateScene(Scene scene, Refs refs, Vector3 spawn)
        {
            Transform[] all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            if (all.Count(t => t.GetComponent<Camera>() != null) != 1 || all.Count(t => t.GetComponent<EventSystem>() != null) != 1)
                throw new InvalidOperationException("Неверное число камер или EventSystem.");
            if (all.Count(t => t.GetComponent<AudioListener>() != null) != 1) throw new InvalidOperationException("Должен быть ровно один AudioListener.");
            if (all.Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0))
                throw new InvalidOperationException("Обнаружен Missing Script.");
            if (all.SelectMany(t => t.GetComponents<Renderer>()).Any(r => r.sharedMaterials.Any(m => m == null)))
                throw new InvalidOperationException("Отсутствует материал.");
            if (all.SelectMany(t => t.GetComponents<MeshFilter>()).Any(f => f.sharedMesh == null))
                throw new InvalidOperationException("Отсутствует меш.");
            if (refs.wheelRoot == null || refs.changerSlot == null || refs.balancerSlot == null || refs.summer == null || refs.winter == null)
                throw new InvalidOperationException("Не созданы точки колеса или варианты шины.");
            if (all.Count(t => t.name == "Wheel_L_Front") != 1) throw new InvalidOperationException("Обслуживаемое колесо должно быть одно.");
            // Точка появления: капсула игрока не пересекает стены, машину и оборудование.
            CharacterController body = all.Select(t => t.GetComponent<CharacterController>()).First(c => c != null);
            bool wasEnabled = body.enabled; body.enabled = false;
            bool blocked = Physics.CheckCapsule(spawn + Vector3.up * .35f, spawn + Vector3.up * 1.45f, .3f, ~0, QueryTriggerInteraction.Ignore);
            body.enabled = wasEnabled;
            if (blocked) throw new InvalidOperationException("Точка появления игрока перекрыта объектом.");
            // Доступность точек взаимодействия с типичных мест стояния (только предупреждение).
            var stands = new Dictionary<WorkshopPoint, Vector3>
            {
                { WorkshopPoint.InspectCar, new Vector3(0, 1.62f, -3.3f) },
                { WorkshopPoint.WheelSlot, new Vector3(-1.75f, 1.62f, -1.7f) },
                { WorkshopPoint.TireChanger, new Vector3(-3.55f, 1.62f, -2.4f) },
                { WorkshopPoint.Balancer, new Vector3(-3.55f, 1.62f, .25f) },
            };
            var buffer = new RaycastHit[16];
            foreach (WorkshopInteractable point in all.Select(t => t.GetComponent<WorkshopInteractable>()).Where(p => p != null))
            {
                Vector3 from = stands[point.Point];
                Vector3 to = point.GetComponent<Collider>().bounds.center;
                int count = Physics.RaycastNonAlloc(from, (to - from).normalized, buffer, 2.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                Array.Sort(buffer, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
                bool reached = false;
                for (int i = 0; i < count; i++)
                {
                    if (buffer[i].collider.GetComponent<WorkshopInteractable>() == point) { reached = true; break; }
                    if (!buffer[i].collider.isTrigger) break;
                }
                if (!reached) Debug.LogWarning("Diagnoz: точка " + point.Point + " может быть недоступна с обычного места. Проверь в Play Mode.");
            }
            Vector3 battery = refs.groups["Node_Battery"].position, origin = new Vector3(-.55f, 2.1f, -3.4f);
            if (!Physics.Raycast(origin, (battery - origin).normalized, out RaycastHit hit, 10, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                hit.collider.GetComponentInParent<DiagnosticNode>() != refs.groups["Node_Battery"].GetComponent<DiagnosticNode>())
                Debug.LogWarning("Diagnoz: автоматический тест луча к аккумулятору не прошёл. Проверь видимость в режиме осмотра.");
        }


    }
}
