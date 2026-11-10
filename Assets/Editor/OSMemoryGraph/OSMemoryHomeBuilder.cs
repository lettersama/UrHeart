using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MrTGame.Narrative;
using MrTGame.EditorTools.OSMemoryGraph;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MrTGame.EditorTools
{
    /// <summary>Reproducible authoring, using Unity serialization/prefab APIs rather than scene YAML edits.</summary>
    public static class OSMemoryHomeBuilder
    {
        const string Art = "Assets/Art/SketchSrouce/MemoryHome";
        const string Data = "Assets/Data/OSMemoryPictures/Chapter01Home";
        const string Prefabs = "Assets/Prefab/OSMemoryPictures/Chapter01Home";
        static JObject content;
        static Dictionary<string, JObject> art;
        static Material feedback;

        [MenuItem("Tools/MrTGame/Memory Home/Rebuild Authored Content")]
        static void BuildMenu() => Debug.Log(Build(true));

        [MenuItem("Tools/MrTGame/Memory Home/Apply Direction Navigation")]
        public static void ApplyDirectionNavigation()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Apply in Edit Mode.");
            var source = JObject.Parse(File.ReadAllText("Tools/MemoryHome/content.json"));
            feedback = AssetDatabase.LoadAssetAtPath<Material>(Data + "/HotspotFeedback.mat");
            foreach (JObject scene in source["scenes"])
            {
                string path = Prefabs + "/" + (string)scene["sceneId"] + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var view = root.GetComponent<OSMemoryPicSceneView>();
                    var navigationIds = NavigationIds(scene);
                    var arrowOnlyIds = new HashSet<string>(scene["hotspots"]
                        .Where(h => !h["art"].Any() && navigationIds.Contains((string)h["hotspotId"]))
                        .Select(h => (string)h["hotspotId"]));
                    var remaining = new List<OSMemoryPicHotspot>();
                    foreach (var hotspot in view.hotspots)
                    {
                        if (hotspot == null) continue;
                        if (hotspot.navigationOnly || arrowOnlyIds.Contains(hotspot.hotspotId))
                            UnityEngine.Object.DestroyImmediate(hotspot.gameObject);
                        else remaining.Add(hotspot);
                    }
                    var oldNavigation = root.transform.Find("Navigation");
                    if (oldNavigation != null) UnityEngine.Object.DestroyImmediate(oldNavigation.gameObject);
                    AddNavigation(root.GetComponent<RectTransform>(), scene, remaining);
                    view.hotspots = remaining.ToArray();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("Memory Home: four fixed half-size direction arrows; unavailable clicks dim the arrow.");
        }

        [MenuItem("Tools/MrTGame/Memory Home/Apply Sprite Silhouette Fixes")]
        public static void ApplySpriteSilhouetteFixes()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Apply in Edit Mode.");
            var source = JObject.Parse(File.ReadAllText("Tools/MemoryHome/content.json"));
            var manifest = JArray.Parse(File.ReadAllText(Art + "/art-manifest.json"));
            ImportSprites();
            foreach (string id in new[] { "hall", "workshop" })
            {
                string path = Prefabs + "/" + id + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var view = root.GetComponent<OSMemoryPicSceneView>();
                    var layers = view.layers.ToList();
                    var scene = source["scenes"].First(s => (string)s["sceneId"] == id);
                    if (id == "hall")
                    {
                        var potHotspot = view.hotspots.FirstOrDefault(h => h.hotspotId == "pot");
                        if (potHotspot == null) continue;
                        var artwork = manifest.First(s => (string)s["id"] == id);
                        var highlights = new List<Image>();
                        foreach (string name in new[] { "pot_body_down", "pot_body_up" })
                        {
                            var entry = (JObject)artwork["layers"].First(l => (string)l["name"] == name);
                            var existing = layers.FirstOrDefault(l => l.id == name);
                            Image graphic = existing?.graphic;
                            if (graphic == null)
                            {
                                graphic = NewImage(name, root.GetComponent<RectTransform>(), Bounds(entry), Sprite((string)entry["file"]));
                                existing = new OSMemoryPicSceneView.Layer { id = name, graphic = graphic };
                                layers.Add(existing);
                            }
                            existing.visibleWhen = scene["layerConditions"][name].ToObject<OSMemoryPicCondition>();
                            graphic.gameObject.SetActive((bool)entry["visible"]);
                            highlights.Add(graphic);
                        }
                        potHotspot.highlightedGraphics = highlights.ToArray();
                    }
                    else
                    {
                        layers.Single(l => l.id == "bench").visibleWhen =
                            scene["layerConditions"]["bench"].ToObject<OSMemoryPicCondition>();
                    }
                    view.layers = layers.ToArray();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Sprite silhouettes applied to existing interactions.");
        }

        public static string Build(bool requireCompleteLocalization = true)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Build in Edit Mode.");
            content = JObject.Parse(File.ReadAllText("Tools/MemoryHome/content.json"));
            art = JArray.Parse(File.ReadAllText(Art + "/art-manifest.json")).Cast<JObject>()
                .ToDictionary(x => (string)x["id"]);
            foreach (JObject entry in content["localization"])
            {
                var values = (JArray)entry["values"];
                if (values.Count != 10 || (requireCompleteLocalization && values.Any(x => string.IsNullOrWhiteSpace((string)x))))
                    throw new InvalidOperationException("Incomplete localization: " + entry["key"]);
            }
            EnsureFolder(Data); EnsureFolder(Prefabs);
            ImportSprites();
            Shader shader = Shader.Find("MrTGame/UI/Memory Hotspot");
            if (shader == null) throw new InvalidOperationException("Missing hotspot shader.");
            feedback = AssetDatabase.LoadAssetAtPath<Material>(Data + "/HotspotFeedback.mat");
            if (feedback == null) { feedback = new Material(shader); AssetDatabase.CreateAsset(feedback, Data + "/HotspotFeedback.mat"); }
            var scenario = LoadOrCreate<OSMemoryPicScenarioData>(Data + "/Chapter01Home.asset");
            // Scene references and sprites are assigned through Unity API below.
            var scalar = (JObject)content.DeepClone();
            scalar.Remove("scenes"); scalar.Remove("items"); scalar.Remove("messages"); scalar.Remove("localization");
            // Sound clips are assigned in Unity; a content rebuild must retain those references.
            scalar.Remove("sounds");
            JsonConvert.PopulateObject(scalar.ToString(), scenario, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            foreach (JObject sound in content["sounds"])
            {
                string cueId = (string)sound["cueId"];
                if (!scenario.sounds.Any(s => s.cueId == cueId))
                    scenario.sounds.Add(new OSMemoryPicSound { cueId = cueId });
            }
            scenario.scenes.Clear();
            foreach (JObject source in content["scenes"]) scenario.scenes.Add(BuildScene(source));
            scenario.items.Clear();
            foreach (JObject source in content["items"])
            {
                var item = source.ToObject<OSMemoryPicItemData>();
                item.icon = Sprite("icons_" + (string)source["iconName"] + ".png");
                scenario.items.Add(item);
            }
            EditorUtility.SetDirty(scenario);
            WriteMemory(scenario);
            WriteLocalization();
            WireWindow(scenario);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Tools/OSMemoryData.csv", OSMemoryCsvUtility.Serialize(
                AssetDatabase.LoadAssetAtPath<OSMemoryData>("Assets/Data/OSMemoryData.asset")));
            return "Memory Home built: " + scenario.scenes.Count + " scenes, " + content["messages"].Count() + " memory nodes.";
        }

        static void ImportSprites()
        {
            foreach (string path in Directory.GetFiles(Art + "/Sprites", "*.png"))
            {
                string assetPath = path.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) { AssetDatabase.ImportAsset(assetPath); importer = (TextureImporter)AssetImporter.GetAtPath(assetPath); }
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaIsTransparency = true;
                importer.isReadable = true;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }
        }

        static OSMemoryPicSceneData BuildScene(JObject source)
        {
            string id = (string)source["sceneId"];
            var scene = LoadOrCreate<OSMemoryPicSceneData>(Data + "/" + id + ".asset");
            scene.sceneId = id; scene.titleKey = (string)source["titleKey"];
            scene.hotspots = source["hotspots"].ToObject<List<OSMemoryPicHotspotData>>();
            var root = MakeRect("Pic_" + id, null, Vector2.zero, new Vector2(432, 318));
            var view = root.gameObject.AddComponent<OSMemoryPicSceneView>();
            var images = new Dictionary<string, Image>();
            var layerData = new Dictionary<string, JObject>();
            var viewLayers = new List<OSMemoryPicSceneView.Layer>();
            foreach (JObject layer in art[id]["layers"])
            {
                string name = (string)layer["name"];
                var graphic = NewImage(name, root, Bounds(layer), Sprite((string)layer["file"]));
                var when = source["layerConditions"]?[name]?.ToObject<OSMemoryPicCondition>();
                if (when == null)
                {
                    when = new OSMemoryPicCondition();
                    if (!(bool)layer["visible"]) when.allFlags.Add("unused_layer");
                }
                images[name] = graphic; layerData[name] = layer;
                viewLayers.Add(new OSMemoryPicSceneView.Layer { id = name, graphic = graphic, visibleWhen = when });
                graphic.gameObject.SetActive((bool)layer["visible"]);
            }
            var hotspots = new List<OSMemoryPicHotspot>();
            var navigationIds = NavigationIds(source);
            foreach (JObject h in source["hotspots"])
            {
                string name = (string)h["hotspotId"];
                string[] parts = h["art"].ToObject<string[]>();
                if (parts.Length == 0 && navigationIds.Contains(name)) continue;
                var bounds = new Rect(0, 0, 0, 0);
                if (h["rect"] != null)
                {
                    float[] b = h["rect"].ToObject<float[]>();
                    bounds = new Rect(b[0], b[1], b[2], b[3]);
                }
                else
                {
                    foreach (string part in parts)
                    {
                        if (!layerData.ContainsKey(part)) throw new InvalidOperationException(id + "/" + name + " missing art " + part);
                        Rect b = Bounds(layerData[part]);
                        bounds = bounds.width == 0 ? b : Rect.MinMaxRect(Mathf.Min(bounds.xMin,b.xMin),Mathf.Min(bounds.yMin,b.yMin),Mathf.Max(bounds.xMax,b.xMax),Mathf.Max(bounds.yMax,b.yMax));
                    }
                }
                bounds = Expand(bounds, 18f);
                var target = NewImage("Hotspot_" + name, root, bounds, null);
                target.color = Color.clear; target.raycastTarget = true;
                var hotspot = target.gameObject.AddComponent<OSMemoryPicHotspot>();
                hotspot.hotspotId = name; hotspot.glitchHint = (bool)h["glitch"]; hotspot.feedbackMaterial = feedback;
                var highlights = parts.Where(images.ContainsKey).Select(p => images[p]).ToList();
                if (h["arrow"] != null)
                {
                    var arrow = NewImage("Indicator", target.rectTransform, new Rect(0,0,bounds.width,bounds.height),
                        Sprite("icons_" + (string)h["arrow"] + ".png"));
                    arrow.rectTransform.anchoredPosition = Vector2.zero;
                    arrow.SetNativeSize();
                    highlights.Add(arrow);
                }
                hotspot.highlightedGraphics = highlights.ToArray();
                hotspots.Add(hotspot);
            }
            AddNavigation(root, source, hotspots);
            view.layers = viewLayers.ToArray(); view.hotspots = hotspots.ToArray();
            if (id == "wiring") ConfigureWiringCables(view);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, Prefabs + "/" + id + ".prefab");
            scene.contentPrefab = prefab.GetComponent<OSMemoryPicSceneView>();
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            EditorUtility.SetDirty(scene);
            return scene;
        }

        public static void ConfigureWiringCables(OSMemoryPicSceneView view)
        {
            var root = (RectTransform)view.transform;
            foreach (var layer in view.layers.Where(layer => layer.id.StartsWith("wire_")))
                if (layer.graphic != null) UnityEngine.Object.DestroyImmediate(layer.graphic.gameObject);
            view.layers = view.layers.Where(layer => !layer.id.StartsWith("wire_")).ToArray();
            var previous = root.Find("Cables");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var holder = MakeRect("Cables", root, new Vector2(0, 24), new Vector2(432, 270));
            holder.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            // Keep cables below the transparent click targets and navigation.
            int firstHotspot = root.Cast<Transform>().Where(t => t.name.StartsWith("Hotspot_") || t.name == "Navigation")
                .Select(t => t.GetSiblingIndex()).DefaultIfEmpty(root.childCount - 1).Min();
            holder.SetSiblingIndex(firstHotspot);
            var wiring = view.GetComponent<OSMemoryPicWiringView>();
            if (wiring == null) wiring = view.gameObject.AddComponent<OSMemoryPicWiringView>();
            // Centres of the visible symbols, not the full plug sprite (which
            // includes its trailing cable) or the socket's outside frame.
            Vector2[] plugs = {new Vector2(159, 57), new Vector2(158, 95),
                new Vector2(159, 135), new Vector2(158, 175)};
            Vector2[] socketCentres = {new Vector2(301, 56), new Vector2(301, 96),
                new Vector2(301, 136), new Vector2(301, 175)};
            int[] sockets = {1, 3, 0, 2};
            float[] lengths = {156, 174, 174, 156};
            wiring.cables = new OSMemoryPicWiringView.Cable[4];
            for (int i = 0; i < 4; i++)
            {
                var start = MakeRect("Start_" + i, holder, new Vector2(plugs[i].x - 216, 135 - plugs[i].y), Vector2.zero);
                Vector2 target = socketCentres[sockets[i]];
                Vector2 socket = new Vector2(target.x - 216, 135 - target.y);
                var end = MakeRect("End_" + i, holder, socket, Vector2.zero);
                var rect = MakeRect("Cable_" + i, holder, Vector2.zero, new Vector2(432, 270));
                var graphic = rect.gameObject.AddComponent<OSUICableGraphic>();
                graphic.color = new Color32(165, 39, 55, 255);
                graphic.ConfigureFixedLength(start, end, lengths[i]);
                graphic.raycastTarget = false;
                graphic.gameObject.SetActive(false);
                wiring.cables[i] = new OSMemoryPicWiringView.Cable
                    {index = i, start = start, end = end, socket = socket, length = lengths[i], graphic = graphic};
            }
        }

        static HashSet<string> NavigationIds(JObject scene) => new HashSet<string>(
            ((JObject)scene["navigation"]).Properties().Select(p => (string)p.Value).Where(id => !string.IsNullOrEmpty(id)));

        static void AddNavigation(RectTransform root, JObject scene, List<OSMemoryPicHotspot> hotspots)
        {
            // Picture area is 432 x 280; the inventory occupies the remaining 38 pixels.
            var navigation = MakeRect("Navigation", root, Vector2.zero, new Vector2(432, 318));
            var directions = new[] { "up", "down", "left", "right" };
            var centers = new[] { new Vector2(216, 14), new Vector2(216, 266), new Vector2(14, 140), new Vector2(418, 140) };
            for (int i = 0; i < directions.Length; i++)
            {
                string direction = directions[i];
                string id = (string)scene["navigation"][direction] ?? "";
                var hit = NewImage(direction, navigation, new Rect(centers[i] - Vector2.one * 13, Vector2.one * 26), null);
                hit.color = Color.clear; hit.raycastTarget = true;
                var hotspot = hit.gameObject.AddComponent<OSMemoryPicHotspot>();
                hotspot.hotspotId = id; hotspot.navigationOnly = true;
                hotspot.glitchHint = false; hotspot.feedbackMaterial = feedback;
                var arrowRect = MakeRect("Indicator", hit.rectTransform, Vector2.zero, Vector2.zero);
                var arrow = arrowRect.gameObject.AddComponent<UnityEngine.UI.Image>();
                arrow.sprite = Sprite("icons_arrow_" + direction + ".png");
                arrow.color = Color.white; arrow.raycastTarget = false;
                arrow.SetNativeSize(); arrowRect.sizeDelta *= .5f;
                hotspot.highlightedGraphics = new[] { arrow };
                hotspot.customEffect = hotspots.FirstOrDefault(h => h.hotspotId == id)?.customEffect;
                hotspots.Add(hotspot);
                hit.gameObject.SetActive(true);
            }
        }

        static void WriteMemory(OSMemoryPicScenarioData scenario)
        {
            var asset = AssetDatabase.LoadAssetAtPath<OSMemoryData>("Assets/Data/OSMemoryData.asset");
            var serialized = new SerializedObject(asset);
            var nodes = serialized.FindProperty("dataList");
            foreach (JObject message in content["messages"])
            {
                int index = (int)message["index"];
                SerializedProperty node = null;
                for (int i = 0; i < nodes.arraySize; i++)
                {
                    var candidate = nodes.GetArrayElementAtIndex(i);
                    if (candidate.FindPropertyRelative("dataIndex").intValue == index) { node = candidate; break; }
                }
                if (node == null) { nodes.arraySize++; node = nodes.GetArrayElementAtIndex(nodes.arraySize - 1); }
                node.FindPropertyRelative("dataIndex").intValue = index;
                node.FindPropertyRelative("sourceText").stringValue = (string)message["zh"];
                node.FindPropertyRelative("nextDataIndex").intValue = -1;
                node.FindPropertyRelative("links").arraySize = 0;
                node.FindPropertyRelative("options").arraySize = 0;
                node.FindPropertyRelative("onEnterActions").arraySize = 0;
            }
            for (int i = 0; i < nodes.arraySize; i++)
            {
                var node = nodes.GetArrayElementAtIndex(i);
                if (node.FindPropertyRelative("dataIndex").intValue != scenario.entryMemoryIndex) continue;
                var actions = node.FindPropertyRelative("onEnterActions");
                for (int j = 0; j < actions.arraySize; j++)
                {
                    var action = actions.GetArrayElementAtIndex(j);
                    if (action.FindPropertyRelative("actionId").stringValue == OSMemoryActionIds.EnterInteractiveScene)
                        action.FindPropertyRelative("asset").objectReferenceValue = scenario;
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void WriteLocalization()
        {
            var asset = AssetDatabase.LoadAssetAtPath<LocalizationData_SO>("Assets/Data/LocalizationData.asset");
            if (asset == null) throw new InvalidOperationException("Localization asset not found.");
            var serialized = new SerializedObject(asset);
            var entries = serialized.FindProperty("entries");
            var existing = new Dictionary<string,int>();
            for (int i = 0; i < entries.arraySize; i++) existing[entries.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue] = i;
            foreach (JObject source in content["localization"])
            {
                string key = (string)source["key"];
                int position;
                if (!existing.TryGetValue(key, out position)) { position = entries.arraySize++; existing[key] = position; }
                var entry = entries.GetArrayElementAtIndex(position);
                entry.FindPropertyRelative("key").stringValue = key;
                var values = entry.FindPropertyRelative("values");
                values.arraySize = 10;
                for (int j = 0; j < 10; j++)
                {
                    var value = values.GetArrayElementAtIndex(j);
                    value.FindPropertyRelative("language").enumValueIndex = j;
                    value.FindPropertyRelative("text").stringValue = (string)source["values"][j];
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void WireWindow(OSMemoryPicScenarioData scenario)
        {
            var memory = Resources.FindObjectsOfTypeAll<OSMemoryWindowController>()
                .FirstOrDefault(x => x.gameObject.scene.path == "Assets/Scenes/GameUI.unity");
            if (memory == null) throw new InvalidOperationException("Open GameUI before building.");
            var pic = memory.transform.Find("SubWindow_InterActivePicWindow");
            var area = pic.Find("PicArea") as RectTransform;
            var unit = pic.GetComponent<OSWindowUnit>();
            var serialized = new SerializedObject(unit);
            serialized.FindProperty("recordedSize").vector2Value = new Vector2(432,333);
            serialized.FindProperty("minimumSize").vector2Value = new Vector2(432,333);
            serialized.FindProperty("allowResize").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ((RectTransform)pic).sizeDelta = new Vector2(432,333);
            ((RectTransform)pic).anchoredPosition = new Vector2(309,-6.5f);
            area.anchorMin = area.anchorMax = new Vector2(.5f,.5f);
            area.pivot = new Vector2(.5f,.5f);
            area.sizeDelta = new Vector2(432,318); area.anchoredPosition = new Vector2(0,-7.5f);
            var originalImage = area.Find("Image")?.GetComponent<Image>();
            if (originalImage != null) { originalImage.sprite = null; originalImage.color = Color.black; originalImage.raycastTarget = false; }
            var controller = pic.GetComponent<OSMemoryPicController>();
            if (controller == null) controller = Undo.AddComponent<OSMemoryPicController>(pic.gameObject);
            controller.scenario = scenario; controller.memory = memory; controller.pictureWindow = unit;
            controller.windowTitle = pic.Find("TopName").GetComponent<TMP_Text>();
            if (controller.sceneRoot == null) controller.sceneRoot = MakeRect("MemoryHomeScene",area,Vector2.zero,new Vector2(432,318));
            if (controller.inventory == null) controller.inventory = BuildInventory(area);
            if (controller.tooltip == null)
            {
                var tip = MakeRect("MemoryHomeTooltip",area,Vector2.zero,new Vector2(80,18));
                tip.pivot = new Vector2(.5f,0);
                var background = tip.gameObject.AddComponent<Image>(); background.color = Color.white; background.raycastTarget = false;
                var labelRect = MakeRect("Name",tip,Vector2.zero,new Vector2(68,18));
                labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(6,3); labelRect.offsetMax = new Vector2(-6,-3);
                var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
                label.font = controller.windowTitle.font; label.fontSize = 9;
                label.color = Color.black; label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.Normal; label.raycastTarget = false;
                controller.tooltip = tip; controller.tooltipLabel = label;
                tip.gameObject.SetActive(false);
            }
            if (controller.blackout == null)
            {
                var cover = MakeRect("MemoryHomeBlackout",area,Vector2.zero,new Vector2(432,318));
                controller.blackout = cover.gameObject.AddComponent<Image>();
                controller.blackout.color = Color.black; controller.blackout.raycastTarget = true;
                cover.gameObject.SetActive(false);
            }
            if (controller.audioSource == null)
            {
                controller.audioSource = pic.gameObject.AddComponent<AudioSource>();
                controller.audioSource.playOnAwake = false;
            }
            controller.audioSource.outputAudioMixerGroup = AssetDatabase
                .LoadAllAssetsAtPath("Assets/Audio/Mixers/MasterMix.mixer")
                .OfType<UnityEngine.Audio.AudioMixerGroup>().Single(g => g.name == "SFXHandle");
            EditorUtility.SetDirty(controller.audioSource);
            controller.sceneRoot.SetAsFirstSibling();
            if (originalImage != null) originalImage.transform.SetAsFirstSibling();
            controller.inventory.transform.SetAsLastSibling();
            controller.tooltip.SetAsLastSibling(); controller.blackout.transform.SetAsLastSibling();
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(memory.gameObject.scene);
            EditorSceneManager.SaveScene(memory.gameObject.scene);
        }

        static OSMemoryPicInventory BuildInventory(RectTransform area)
        {
            var root = MakeRect("MemoryHomeInventory",area,new Vector2(0,-140),new Vector2(432,38));
            var bg = root.gameObject.AddComponent<Image>(); bg.color=Color.black; bg.raycastTarget=true;
            var inventory = root.gameObject.AddComponent<OSMemoryPicInventory>();
            var top = MakeRect("Separator",root,new Vector2(0,19),new Vector2(432,1));
            var border = top.gameObject.AddComponent<Image>(); border.color=new Color32(165,39,55,255); border.raycastTarget=false;
            inventory.slots = new OSMemoryPicInventorySlot[6];
            for (int i=0;i<6;i++)
            {
                var cell = MakeRect("Unit_"+i,root,new Vector2(-160+i*64,0),new Vector2(60,36));
                var hit = cell.gameObject.AddComponent<Image>(); hit.color=Color.black;hit.raycastTarget=true;
                var slot=cell.gameObject.AddComponent<OSMemoryPicInventorySlot>();
                var bottom=MakeRect("Selected",cell,new Vector2(0,-17),new Vector2(38,1));
                slot.selection=bottom.gameObject.AddComponent<Image>();slot.selection.color=Color.white;slot.selection.raycastTarget=false;
                var icon=MakeRect("Icon",cell,Vector2.zero,new Vector2(32,32));
                slot.icon=icon.gameObject.AddComponent<Image>();slot.icon.raycastTarget=false;
                inventory.slots[i]=slot;
                slot.selection.gameObject.SetActive(false);
                slot.icon.gameObject.SetActive(false);
            }
            inventory.previous=InventoryArrow(root,"Previous",-207,"arrow_left");
            inventory.next=InventoryArrow(root,"Next",207,"arrow_right");
            return inventory;
        }

        static Button InventoryArrow(RectTransform parent,string name,float x,string iconName)
        {
            var rect=MakeRect(name,parent,new Vector2(x,0),new Vector2(18,36));
            var hit=rect.gameObject.AddComponent<Image>();hit.color=Color.black;
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=hit;button.transition=Selectable.Transition.None;
            var imageRect=MakeRect("Arrow",rect,Vector2.zero,new Vector2(16,16));
            var image=imageRect.gameObject.AddComponent<Image>();image.sprite=Sprite("icons_"+iconName+".png");image.raycastTarget=false;
            image.preserveAspect=true;
            return button;
        }

        static RectTransform MakeRect(string name,Transform parent,Vector2 position,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform));
            var rect=(RectTransform)go.transform;
            if(parent!=null)rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            rect.anchoredPosition=position;rect.sizeDelta=size;rect.localScale=Vector3.one;
            return rect;
        }

        static Image NewImage(string name,RectTransform parent,Rect bounds,UnityEngine.Sprite sprite)
        {
            var rect=MakeRect(name,parent,new Vector2(bounds.center.x-216,159-bounds.center.y),bounds.size);
            var image=rect.gameObject.AddComponent<Image>();
            image.sprite=sprite;image.color=Color.white;image.raycastTarget=false;
            return image;
        }

        static Rect Bounds(JObject layer) => new Rect((float)layer["x"],(float)layer["y"],(float)layer["w"],(float)layer["h"]);
        static Rect Expand(Rect rect,float minimum)
        {
            Vector2 center=rect.center;
            rect.width=Mathf.Max(minimum,rect.width);rect.height=Mathf.Max(minimum,rect.height);
            rect.center=center;return rect;
        }
        static UnityEngine.Sprite Sprite(string filename)
        {
            var value=AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(Art+"/Sprites/"+filename);
            if(value==null)throw new InvalidOperationException("Missing sprite "+filename);
            return value;
        }
        static T LoadOrCreate<T>(string path) where T:ScriptableObject
        {
            T value=AssetDatabase.LoadAssetAtPath<T>(path);
            if(value!=null)return value;
            value=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(value,path);return value;
        }
        static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');
            EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
    }
}
