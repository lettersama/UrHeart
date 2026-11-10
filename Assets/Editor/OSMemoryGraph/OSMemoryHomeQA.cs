using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MrTGame.EditorTools
{
    public static class OSMemoryHomeQA
    {
        static readonly FieldInfo SaveField=typeof(GameDataManager).GetField("saveData",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly List<string> checks=new List<string>();
        static OSMemoryPicController controller;
        static string status="idle";
        static bool finished;
        public static string Status => status;

        static bool LayerVisible(string id) => controller.sceneRoot
            .GetComponentInChildren<OSMemoryPicSceneView>().layers
            .Single(layer=>layer.id==id).graphic.gameObject.activeInHierarchy;

        static void CheckCableGeometry(OSMemoryPicWiringView wiring)
        {
            var positionsField=typeof(OSUICableGraphic).GetField("positions",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach(var cable in wiring.cables)
            {
                Vector2 oldEnd=cable.end.anchoredPosition;
                foreach(Vector2 offset in new[]{new Vector2(1000,600),new Vector2(-1000,-600),
                    Vector2.zero,new Vector2(0,-30),new Vector2(50,20),cable.socket-cable.start.anchoredPosition})
                {
                    cable.end.anchoredPosition=OSMemoryPicWiringView.ConstrainEndpoint(cable,cable.start.anchoredPosition+offset);
                    cable.graphic.ResetSimulation();
                    var points=(Vector2[])positionsField.GetValue(cable.graphic);
                    float arc=0;
                    for(int i=1;i<points.Length;i++)arc+=Vector2.Distance(points[i-1],points[i]);
                    Check(points.All(p=>float.IsFinite(p.x)&&float.IsFinite(p.y)),"finite cable geometry: "+cable.index+" / "+offset);
                    Check(Vector2.Distance(points[0],points[points.Length-1])<=cable.length+.01f
                        &&arc<=cable.length+.02f&&arc>=cable.length-1.5f,"cable keeps its length without stretching: "+cable.index+" / "+offset);
                }
                cable.end.anchoredPosition=oldEnd;
                cable.graphic.ResetSimulation();
            }
        }

        public static string Start(bool entryOnly = false)
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Play Mode required.");
            if(GameDataManager.Instance==null)throw new InvalidOperationException("Wait for GameDataManager initialization before running QA.");
            if(status=="running")return status;
            GameAutosaveService.Instance?.EnterTestSession();
            checks.Clear();finished=false;status="running";
            Directory.CreateDirectory("Temp/MemoryHomeQA");
            GameDataManager.Instance.StartCoroutine(Run(entryOnly));
            return status;
        }
        static void Check(bool ok,string message)
        {
            if(!ok)throw new InvalidOperationException("[Memory Home QA] "+message);
            checks.Add(message);
        }
        static IEnumerator Ready()
        {
            float until=Time.realtimeSinceStartup+15f;
            while((controller==null||!controller.CanInteract)&&Time.realtimeSinceStartup<until)yield return null;
            Check(controller!=null&&controller.CanInteract,"interaction ready");
        }
        static IEnumerator Click(string id,bool interruptPresentation=false,string direction=null)
        {
            yield return Ready();
            var hotspot=controller.GetComponentsInChildren<OSMemoryPicHotspot>()
                .Where(x=>x.hotspotId==id&&(direction==null||(x.navigationOnly&&x.name==direction)))
                .OrderBy(x=>x.navigationOnly).First();
            var previousLine=controller.memory.TextUnits.FirstOrDefault(x=>x.IsPlaying);
            var previousData=previousLine!=null?previousLine.BoundData:null;
            var rect=(RectTransform)hotspot.transform;
            var canvas=rect.GetComponentInParent<Canvas>();
            var pointer=new PointerEventData(EventSystem.current)
            {
                button=PointerEventData.InputButton.Left,
                position=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,rect.TransformPoint(rect.rect.center)),
                pointerCurrentRaycast=new RaycastResult{gameObject=hotspot.gameObject},
                pointerPressRaycast=new RaycastResult{gameObject=hotspot.gameObject},
                rawPointerPress=hotspot.gameObject
            };
            var hitResults=new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer,hitResults);
            var firstHit=hitResults.FirstOrDefault().gameObject;
            Check(firstHit!=null&&firstHit.GetComponentInParent<OSMemoryPicHotspot>()==hotspot,"frontmost clickable hotspot: "+id);
            ExecuteEvents.Execute(hotspot.gameObject,pointer,ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(hotspot.gameObject,pointer,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(hotspot.gameObject,pointer,ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(hotspot.gameObject,pointer,ExecuteEvents.pointerClickHandler);
            if(id=="enter")
                Check(controller.audioSource!=null&&controller.audioSource.isPlaying
                    &&controller.scenario.FindSound("door")!=null
                    &&controller.audioSource.outputAudioMixerGroup==AudioManager.Instance.SFXMixerGroup,
                    "entry click plays the door clip through the game SFX mixer");
            if(previousLine!=null&&previousLine.BoundData==previousData)
                Check(previousLine.IsTextComplete&&previousLine.Label.text==previousData.Text,"previous Memory line completed: "+id);
            Check(!controller.TryInteract(id),"same-frame duplicate blocked: "+id);
            yield return null;
            if(interruptPresentation)
            {
                Check(controller.IsBusy,"presentation running before close: "+id);
                controller.memory.GetComponent<OSWindowUnit>().CloseSilently();
                yield return null;
                OS_Handle.Instance.OpenMemoryWindow();
                yield return null;
                yield return Ready();
                Check(!controller.blackout.gameObject.activeSelf&&controller.sceneRoot.anchoredPosition==Vector2.zero,"cancelled presentation restores cleanly: "+id);
            }
            yield return Ready();
            Check(controller.memory.CurrentData!=null&&controller.scenario.OwnsMemory(controller.memory.CurrentData.DataIndex),"Memory action: "+id);
        }
        static IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes("Temp/MemoryHomeQA/"+name+".png",texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
        }
        static IEnumerator DragWire(string plugId, string socketId = null)
        {
            yield return Ready();
            var plug = controller.GetComponentsInChildren<OSMemoryPicHotspot>().Single(x=>x.hotspotId==plugId);
            var socket = controller.GetComponentsInChildren<OSMemoryPicHotspot>().SingleOrDefault(x=>x.hotspotId==socketId);
            var canvas = plug.GetComponentInParent<Canvas>();
            var start = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,plug.transform.position);
            var end = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,
                socket!=null ? socket.transform.position : controller.sceneRoot.TransformPoint(new Vector3(0,135,0)));
            var pointer = new PointerEventData(EventSystem.current)
            {
                button=PointerEventData.InputButton.Left, position=start, pressPosition=start,
                pointerDrag=plug.gameObject, pointerPress=plug.gameObject, eligibleForClick=true,
                pointerCurrentRaycast=new RaycastResult{gameObject=plug.gameObject}
            };
            ExecuteEvents.Execute(plug.gameObject,pointer,ExecuteEvents.pointerDownHandler);
            pointer.dragging=true;
            ExecuteEvents.Execute(plug.gameObject,pointer,ExecuteEvents.beginDragHandler);
            Check(!pointer.eligibleForClick,"wire drag consumes the click: "+plugId);
            bool poweredOff=controller.State.Has("power_off");
            var cable=controller.sceneRoot.GetComponentInChildren<OSMemoryPicWiringView>().cables.Single(x=>x.index==plug.Data.socketIndex);
            Check(poweredOff ? controller.SelectedWire==plug.Data.socketIndex&&cable.graphic.gameObject.activeSelf
                : controller.SelectedWire==-1&&!cable.graphic.gameObject.activeSelf,"drag starts a cable only with power off: "+plugId);
            pointer.position=end;
            pointer.delta=end-start;
            var hits=new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer,hits);
            pointer.pointerCurrentRaycast=hits.FirstOrDefault();
            if(socket!=null)Check(pointer.pointerCurrentRaycast.gameObject!=null
                &&pointer.pointerCurrentRaycast.gameObject.GetComponentInParent<OSMemoryPicHotspot>()==socket,"drag destination is raycast reachable: "+socketId);
            ExecuteEvents.Execute(plug.gameObject,pointer,ExecuteEvents.dragHandler);
            yield return null;
            ExecuteEvents.Execute(plug.gameObject,pointer,ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(plug.gameObject,pointer,ExecuteEvents.endDragHandler);
            int reading=controller.memory.CurrentData.DataIndex;
            pointer.dragging=false;
            ExecuteEvents.Execute(plug.gameObject,pointer,ExecuteEvents.pointerClickHandler);
            Check(controller.memory.CurrentData.DataIndex==reading,"release does not replay the plug click: "+plugId);
            yield return null;
            yield return Ready();
        }

        static void CheckTooltipLayout(string key)
        {
            controller.tooltipLabel.ForceMeshUpdate();
            var textRect=controller.tooltipLabel.rectTransform.rect;
            var preferred=controller.tooltipLabel.GetPreferredValues(controller.tooltipLabel.text,textRect.width,Mathf.Infinity);
            Check(!controller.tooltipLabel.isTextOverflowing&&preferred.x<=textRect.width+.1f&&preferred.y<=textRect.height+.1f,
                "translated label fits its background: "+GameDataManager.Instance.CurrentLanguageState+" / "+key);
            var background=controller.tooltip.rect;
            Check(background.width>=textRect.width+11.9f&&background.height>=textRect.height+5.9f,"caption keeps padding: "+key);
            var area=(RectTransform)controller.tooltip.parent;
            var corners=new Vector3[4];controller.tooltip.GetWorldCorners(corners);
            Check(corners.All(p=>area.rect.Contains((Vector2)area.InverseTransformPoint(p))),"caption remains inside picture: "+key);
        }

        static IEnumerator CheckLanguages(GameDataManager manager)
        {
            var manifest=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Tools/MemoryHome/content.json"));
            var keys=new HashSet<string>(manifest["localization"].Select(x=>(string)x["key"]));
            var entries=manager.LocalizationData.Entries.Where(x=>keys.Contains(x.Key)).ToArray();
            Check(entries.Length==keys.Count,"all authored localization keys imported");
            var enter=controller.GetComponentsInChildren<OSMemoryPicHotspot>().Single(x=>x.hotspotId=="enter"&&!x.navigationOnly);
            var labels=entries.Where(x=>x.Key.StartsWith("os.memory.pic.")).ToArray();
            foreach(LanguageState language in Enum.GetValues(typeof(LanguageState)))
            {
                manager.SetCurrentLanguage(language);
                yield return null;
                var values=entries.Select(x=>x.Values.SingleOrDefault(v=>v.Language==language)).ToArray();
                Check(values.All(x=>x!=null&&!string.IsNullOrWhiteSpace(x.Text)),"no locale fallback: "+language);
                Check(entries.All(x=>x.Values.Single(v=>v.Language==language).Text.Split(new[]{"<color=red>"},StringSplitOptions.None).Length
                    ==x.Values.Single(v=>v.Language==LanguageState.zh_CN).Text.Split(new[]{"<color=red>"},StringSplitOptions.None).Length),"translated red hints match Chinese: "+language);
                string characters=new string(string.Concat(values.Select(x=>x.Text)).Distinct().ToArray());
                uint[] missing;
                bool covered=controller.tooltipLabel.font.HasCharacters(characters,out missing,true,true);
                Check(covered,"font coverage: "+language+(covered?"":" / "+string.Join(",",missing)));
                controller.tooltipLabel.ForceMeshUpdate();
                Check(controller.tooltipLabel.text==manager.GetLocalizedText("os.memory.pic.enter"),"live hover translation: "+language);
                foreach(var label in labels)
                {
                    controller.ShowTip(enter,label.Key,(RectTransform)enter.transform);
                    CheckTooltipLayout(label.Key);
                }
                controller.ShowTip(enter,"os.memory.pic.parts",(RectTransform)enter.transform);
                if(language==LanguageState.en||language==LanguageState.ko||language==LanguageState.ru||language==LanguageState.de)
                    yield return Shot("locale-"+language);
                controller.ShowHotspotTip(enter);
            }
        }
        static void CheckRobotHintRichText()
        {
            var unit=controller.memory.TextUnits.Last(x=>x.DataIndex==2720&&x.IsPlaying);
            var tween=(DG.Tweening.Tween)typeof(OSMemoryTextUnit)
                .GetField("textTween",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(unit);
            Check(unit.Label.richText,"Memory TMP label enables rich text");
            DG.Tweening.TweenExtensions.Pause(tween);
            float duration=DG.Tweening.TweenExtensions.Duration(tween,false);
            string plain=System.Text.RegularExpressions.Regex.Replace(unit.BoundData.Text,"<[^>]*>","");
            for(int i=1;i<40;i++)
            {
                DG.Tweening.TweenExtensions.Goto(tween,duration*i/40f,false);
                unit.Label.ForceMeshUpdate();
                string visible=unit.Label.GetParsedText();
                Check(plain.StartsWith(visible,StringComparison.Ordinal)&&!visible.Contains("<")&&!visible.Contains(">"),
                    "typewriter shows only visible text at sample "+i);
            }
            unit.CompleteImmediately();
            unit.Label.ForceMeshUpdate();
            Check(unit.Label.GetParsedText()==plain,"completed hint strips both color tags");
            Check(unit.Label.textInfo.characterInfo.Take(unit.Label.textInfo.characterCount)
                .Any(c=>c.isVisible&&c.color.r==255&&c.color.g==0&&c.color.b==0),"hint contains visible red characters");
        }

        static IEnumerator Run(bool entryOnly)
        {
            GameDataManager manager=GameDataManager.Instance;
            GameSaveData original=manager.SaveData;
            LanguageState originalLanguage=manager.CurrentLanguageState;
            bool originalBackground=Application.runInBackground;
            Application.runInBackground=true;
            try
            {
                var isolated=JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original));
                SaveField.SetValue(manager,isolated);
                var osData=manager.OSProcessData;
                osData.installedToolProgramIds.Add("memory-repair-1");
                osData.installedToolProgramIds.Add("memory-repair-2");
                osData.memory.pictureScenario=new OSMemoryPicSaveData();
                osData.memory.currentDataIndex=2670;
                GameTest.Instance.JumpToMemoryIndex(2680);
                float until=Time.realtimeSinceStartup+30f;
                while(Time.realtimeSinceStartup<until)
                {
                    controller=UnityEngine.Object.FindAnyObjectByType<OSMemoryPicController>();
                    if(controller!=null&&controller.CanInteract)break;
                    yield return null;
                }
                yield return Ready();
                foreach(var window in UnityEngine.Object.FindObjectsByType<OSWindowUnit>())
                    if(window.IsOpen&&window!=controller.pictureWindow&&window!=controller.memory.GetComponent<OSWindowUnit>())
                        window.CloseSilently();
                Check(controller.State.currentSceneId=="entrance","entry starts outside");
                yield return Shot("01-entrance");
                var enter=controller.GetComponentsInChildren<OSMemoryPicHotspot>().Single(x=>x.hotspotId=="enter"&&!x.navigationOnly);
                var camera=enter.GetComponentInParent<Canvas>().worldCamera;
                var p=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(camera,enter.transform.position)};
                var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(p,hits);
                File.WriteAllText("Temp/MemoryHomeQA/raycast.json",Newtonsoft.Json.JsonConvert.SerializeObject(hits.Select(x=>x.gameObject.name).ToArray()));
                Check(hits.Any(x=>x.gameObject==enter.gameObject),"door hotspot is raycast reachable");
                ExecuteEvents.Execute(enter.gameObject,p,ExecuteEvents.pointerEnterHandler);
                yield return null;
                Check(controller.tooltip.gameObject.activeSelf,"hover name is visible");
                yield return Shot("02-door-hover");
                if (!entryOnly) yield return CheckLanguages(manager);
                manager.SetCurrentLanguage(originalLanguage);
                yield return Click("enter",true);
                Check(controller.State.currentSceneId=="hall","door transition reaches hall");
                var hallDown=controller.GetComponentsInChildren<OSMemoryPicHotspot>().Single(x=>x.navigationOnly&&x.name=="down");
                Check(!controller.HasNavigationTarget(hallDown.Data)&&!controller.TryNavigate("back.hall")
                    &&!controller.TryInteract("back.hall"),"hall cannot return outside after entering");
                Check(Mathf.Approximately(hallDown.highlightedGraphics[0].color.a,.25f),"hall down arrow stays visibly unavailable");
                controller.FocusPicture();
                int reading=controller.memory.CurrentData.DataIndex;
                controller.memory.TryAdvance();
                Check(controller.memory.CurrentData.DataIndex==reading,"E completes text without bypassing hotspot");
                foreach(string blocked in new[]{"to_kitchen","to_living"})
                {
                    var direction=controller.GetComponentsInChildren<OSMemoryPicHotspot>(true).Single(x=>x.navigationOnly&&x.hotspotId==blocked);
                    Check(direction.gameObject.activeSelf&&!controller.HasNavigationTarget(direction.Data),"unavailable direction remains visible: "+blocked);
                    Check(Mathf.Approximately(direction.highlightedGraphics[0].color.a,.25f),"unavailable direction has 0.25 opacity: "+blocked);
                    yield return Click(blocked);
                    Check(ColorUtility.ToHtmlStringRGB(direction.highlightedGraphics[0].material.GetColor("_InkColor"))=="6F6867","unavailable click dims arrow: "+blocked);
                    Check(controller.memory.CurrentData.DataIndex==2720,"blocked arrow explains the upstairs robot task: "+blocked);
                    Check(controller.State.currentSceneId=="hall"&&!controller.State.Has("robot_placed")&&controller.State.inventory.Contains("robot"),"robot gate does not navigate or change inventory: "+blocked);
                    if(blocked=="to_kitchen") CheckRobotHintRichText();
                }
                yield return Shot("02b-robot-gate");
                if (entryOnly) { finished=true; yield break; }
                yield return Click("hall_drawer");yield return Click("drawer_letters");yield return Click("back.drawer");
                yield return Click("hall_picture");
                yield return Click("upstairs");
                yield return Click("to_daughter");yield return Click("daughter_handle");
                Check(controller.State.currentSceneId=="daughter","daughter's door stays locked");
                yield return Click("back.daughter");
                yield return Click("to_workshop");
                int glassEvents=0;
                controller.onGlassBreak.AddListener(()=>glassEvents++);
                yield return Click("robot",true);
                yield return Ready();
                Check(controller.State.Has("robot_placed")&&!controller.State.inventory.Contains("robot"),"robot placed once");
                Check(controller.State.Has("glass_broken"),"glass event completed");
                Check(glassEvents==1,"interrupted glass event resumes exactly once");
                yield return Shot("03-workbench");
                yield return Click("robot");
                Check(controller.State.currentSceneId=="workshop","repair blocked before inspection");
                yield return Click("back.workshop",direction:"right");
                Check(controller.State.currentSceneId=="upstairs","workshop right arrow returns upstairs");
                yield return Click("back.upstairs");
                var kitchenArrow=controller.GetComponentsInChildren<OSMemoryPicHotspot>()
                    .Single(x=>x.navigationOnly&&x.hotspotId=="to_kitchen");
                Check(!controller.HasNavigationTarget(kitchenArrow.Data)
                    &&Mathf.Approximately(kitchenArrow.highlightedGraphics[0].color.a,.25f),"kitchen remains dimmed after glass breaks");
                yield return Click("to_kitchen");
                Check(controller.State.currentSceneId=="hall"&&!controller.State.Has("window_inspected"),"kitchen prompt does not navigate or inspect window");
                Check(controller.memory.CurrentData.DataIndex==3630
                    &&controller.memory.CurrentData.Text==manager.GetLocalizedText("os.memory.3630.text")
                    &&controller.memory.CurrentData.Text.Contains("<color=red>"),"blocked kitchen plays localized living room prompt with red emphasis");
                controller.memory.TryAdvance();
                yield return Shot("03a-kitchen-blocked");
                controller.memory.GetComponent<OSWindowUnit>().CloseSilently();
                yield return null;
                OS_Handle.Instance.OpenMemoryWindow();
                yield return null;
                yield return Ready();
                Check(!controller.HasNavigationTarget(controller.CurrentScene.FindHotspot("to_kitchen")),"kitchen gate persists after reopening Memory");
                yield return Click("to_living");
                Check(!controller.State.Has("window_inspected"),"entering living room alone does not inspect window");
                yield return Click("back.living");
                yield return Click("to_kitchen");
                Check(controller.State.currentSceneId=="hall"&&controller.memory.CurrentData.DataIndex==3630,"kitchen stays blocked until window is clicked");
                yield return Click("to_living");yield return Click("window");
                Check(controller.State.Has("window_inspected"),"window inspection records clue");
                yield return Shot("04-broken-window");
                yield return Click("place_board");
                Check(!controller.State.Has("board_0"),"cannot place missing wood");
                yield return Click("back.window");yield return Click("back.living");
                kitchenArrow=controller.GetComponentsInChildren<OSMemoryPicHotspot>()
                    .Single(x=>x.navigationOnly&&x.hotspotId=="to_kitchen");
                Check(controller.HasNavigationTarget(kitchenArrow.Data)
                    &&Mathf.Approximately(kitchenArrow.highlightedGraphics[0].color.a,1f),"window inspection unlocks kitchen at full opacity");
                yield return Click("to_kitchen");
                Check(controller.State.currentSceneId=="kitchen","kitchen is accessible immediately after window inspection");
                yield return Click("back.kitchen");yield return Click("upstairs");yield return Click("to_workshop");
                yield return Click("planks");yield return Click("toolbox");
                yield return Click("take_hammer");yield return Click("take_nails");yield return Click("take_fuse");
                yield return Click("back.toolbox");yield return Click("back.workshop",direction:"right");yield return Click("to_bedroom");
                Check(controller.CurrentScene.FindHotspot("take_tape")==null&&!LayerVisible("tape"),"bedside tape and pickup removed");
                yield return Shot("06a-bedroom-no-tape");
                yield return Click("bedroom_photo");yield return Click("back.bedroom",direction:"left");
                Check(controller.State.currentSceneId=="upstairs","bedroom left arrow returns upstairs");
                yield return Click("back.upstairs");
                yield return Click("to_kitchen");yield return Click("take_broom");yield return Click("to_sink");yield return Click("take_cloth");
                Check(controller.State.inventory.Count==6&&!controller.State.inventory.Contains("tape"),"six usable items and no tape");
                Check(controller.inventory.PoolSize==6,"inventory uses exactly six pooled cells");
                controller.inventory.Next();controller.inventory.Previous();
                Check(controller.inventory.FirstVisible==0,"inventory pages both ways");
                var scroll=new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-1)};
                ExecuteEvents.Execute(controller.inventory.gameObject,scroll,ExecuteEvents.scrollHandler);
                Check(controller.inventory.FirstVisible==0,"inventory wheel stays in range with six items");
                foreach(var cell in controller.inventory.slots)
                    if(cell.icon.sprite!=null)
                        Check(cell.icon.rectTransform.sizeDelta==cell.icon.sprite.rect.size,"inventory icon uses native pixel size");
                float scale=controller.inventory.GetComponentInParent<Canvas>().scaleFactor;
                var drag=new PointerEventData(EventSystem.current){position=new Vector2(100,100)};
                ExecuteEvents.Execute(controller.inventory.gameObject,drag,ExecuteEvents.beginDragHandler);
                drag.position+=new Vector2(30*scale,0);
                ExecuteEvents.Execute(controller.inventory.gameObject,drag,ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(controller.inventory.gameObject,drag,ExecuteEvents.endDragHandler);
                Check(controller.inventory.FirstVisible==0,"inventory drag pages horizontally");
                controller.inventory.Next();
                yield return Shot("05-inventory-scroll");
                yield return Click("dishes");
                var dishHotspot=controller.GetComponentsInChildren<OSMemoryPicHotspot>().Single(x=>x.hotspotId=="dishes");
                controller.ShowHotspotTip(dishHotspot);
                Check(controller.tooltip.anchoredPosition.x>100f&&Mathf.Abs(controller.tooltip.anchoredPosition.y-31f)<1f,"clean-dish caption follows visible dishes on the right");
                yield return Shot("05a-clean-dishes-caption");
                yield return Click("back.sink");yield return Click("back.kitchen");
                yield return Click("to_living");yield return Click("to_table");yield return Click("books");yield return Click("family_photo");
                yield return Click("back.table");yield return Click("window");
                Check(!controller.State.Has("board_0")&&controller.State.inventory.Contains("wood"),"entering the window close-up does not place or consume owned planks");
                yield return Click("place_board");
                Check(controller.State.Has("board_0")&&!controller.State.Has("board_1")&&controller.memory.CurrentData.DataIndex==3040,"first plank is placed only by clicking inside the close-up");
                var snapshot=JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(manager.SaveData));
                SaveField.SetValue(manager,snapshot);yield return null;yield return Ready();
                Check(controller.State.Has("board_0"),"plank survives full SaveData JSON roundtrip");
                var memoryWindow=controller.memory.GetComponent<OSWindowUnit>();
                memoryWindow.CloseSilently();yield return null;
                OS_Handle.Instance.OpenMemoryWindow();yield return null;yield return Ready();
                Check(controller.State.currentSceneId=="window"&&controller.State.Has("board_0"),"close/reopen restores picture checkpoint");
                memoryWindow.Minimize();yield return null;
                OS_Handle.Instance.OpenMemoryWindow();yield return null;yield return Ready();
                Check(controller.State.Has("board_0"),"minimize/reopen keeps inventory and boards");
                yield return Click("back.window");
                Check(LayerVisible("window_boards_1")&&!LayerVisible("window_boarded"),"room displays exactly one plank");
                yield return Shot("05b-room-one-plank");
                yield return Click("window");
                Check(controller.State.Has("board_0")&&!controller.State.Has("board_1")&&controller.State.inventory.Contains("wood"),"reentering with one plank does not place the second");
                yield return Click("place_board");
                Check(controller.State.Has("board_1")&&!controller.State.Has("board_2")&&controller.memory.CurrentData.DataIndex==3680,"second plank has its own one-plank-left text");
                yield return Click("back.window");
                Check(LayerVisible("window_boards_2")&&!LayerVisible("window_boarded"),"room displays exactly two planks");
                yield return Shot("05c-room-two-planks");
                yield return Click("window");
                Check(controller.State.Has("board_1")&&!controller.State.Has("board_2")&&controller.State.inventory.Contains("wood"),"reentering with two planks does not place the last or consume wood");
                yield return Click("place_board");
                Check(controller.memory.CurrentData.DataIndex==3050,"third plank preserves its original text");
                Check(controller.State.Has("boards_placed")&&!controller.State.inventory.Contains("wood"),"three boards consume the wood once");
                Check(Enumerable.Range(0,6).All(i=>LayerVisible("nail_marker_"+i)&&!LayerVisible("nail_"+i)),"six red rings appear before any nails are placed");
                yield return Shot("05d-six-nail-markers");
                yield return Click("back.window");
                Check(LayerVisible("window_boarded")&&!controller.State.Has("window_fixed"),"room displays three planks before hammering");
                yield return Click("window");
                for(int i=0;i<6;i++)
                {
                    var nailHotspot=controller.GetComponentsInChildren<OSMemoryPicHotspot>().Single(x=>x.hotspotId=="nail_"+i);
                    controller.ShowHotspotTip(nailHotspot);
                    Check(controller.tooltipLabel.text==manager.GetLocalizedText("os.memory.pic.nail_"+i),"empty nail position shows place-nail hover label");
                    if(i==0)yield return Shot("05d1-place-nail-caption");
                    yield return Click("nail_"+i);
                    Check(controller.State.Has("nail_"+i+"_hit")&&!controller.State.Has("nail_"+i+"_done")&&LayerVisible("nail_"+i)&&!LayerVisible("nail_marker_"+i),"first click replaces ring with raised nail");
                    Check(controller.tooltipLabel.text==manager.GetLocalizedText("os.memory.pic.hammer_nail"),"placed nail immediately shows hammer-nail hover label");
                    if(i==0)yield return Shot("05e-first-nail-placed");
                    yield return Click("nail_"+i);
                    Check(LayerVisible("nail_done_"+i)&&!LayerVisible("nail_"+i),"second click drives this nail flush");
                    if(i==0)Check(!controller.State.Has("nail_1_hit"),"one nail can be hammered before placing the others");
                }
                Check(controller.memory.CurrentData.DataIndex==3710,"last nail confirms all six are secure");
                Check(controller.State.Has("window_fixed")&&!controller.State.inventory.Contains("nails"),"all six nails secure the window");
                yield return Shot("06-boarded-window");
                yield return Click("back.window");
                Check(controller.memory.CurrentData.DataIndex==3720,"repaired window return prompts glass cleanup");
                yield return Click("back.living");yield return Click("to_living");
                Check(controller.memory.CurrentData.DataIndex==3720,"later living-room entry still prompts unswept glass");
                yield return Click("sweep_glass");yield return Click("back.living");yield return Click("to_living");
                Check(controller.memory.CurrentData.DataIndex==3730,"clean living room prompts upstairs robot repair");
                yield return Click("back.living");yield return Click("upstairs");
                Check(controller.memory.CurrentData.DataIndex==3740,"stairs after repair no longer mention noises behind the player");
                yield return Click("to_workshop");yield return Click("robot");
                Check(controller.State.currentSceneId=="wiring"&&!controller.State.Has("cushion_up"),"window and glass cleanup unlock repair without optional tidying");
                yield return Click("plug_0");
                Check(controller.SelectedWire==-1,"power must be disconnected");
                yield return DragWire("plug_0","socket_1");
                Check(!controller.State.Has("wire_0"),"drag cannot bypass disconnecting the power");
                var wiring=controller.sceneRoot.GetComponentInChildren<OSMemoryPicWiringView>();
                Check(wiring!=null&&wiring.cables.All(c=>!c.graphic.gameObject.activeSelf),"no cable appears before selecting a disconnected plug");
                Check(controller.CurrentScene.FindHotspot("insulate")==null,"wire insulation action removed");
                yield return Click("power");
                yield return Click("plug_1");
                Check(wiring.cables[1].graphic.gameObject.activeSelf,"clicking the left plug immediately shows a cable");
                CheckCableGeometry(wiring);
                yield return Shot("07a-held-cable");
                yield return Click("socket_0");
                Check(!controller.State.Has("wire_1"),"wrong shape does not connect");
                Check(controller.SelectedWire==1&&wiring.cables[1].graphic.gameObject.activeSelf,"wrong socket keeps the held cable");
                yield return Click("socket_3");
                Check(controller.State.Has("wire_1"),"correct triangle connects");
                yield return DragWire("plug_0");
                Check(controller.SelectedWire==0&&!controller.State.Has("wire_0"),"dropping on empty space keeps the loose cable held");
                yield return Shot("07-wire-selected");yield return Click("socket_1");
                yield return DragWire("plug_2","socket_2");
                Check(controller.SelectedWire==2&&!controller.State.Has("wire_2"),"wrong drag destination does not connect");
                yield return Click("socket_0");
                yield return DragWire("plug_3","socket_2");
                Check(controller.State.Has("wire_3"),"dragging onto the matching socket connects on release");
                Check(controller.State.Has("wires_connected"),"four distinct shapes all connected");
                yield return null;
                Check(wiring.cables.All(c=>c.graphic.gameObject.activeInHierarchy&&Vector2.Distance(c.end.anchoredPosition,c.socket)<.01f),"all four cables attach to their matching socket");
                yield return Shot("07b-four-hanging-cables");
                yield return Click("back.wiring");yield return Click("robot");
                wiring=controller.sceneRoot.GetComponentInChildren<OSMemoryPicWiringView>();
                Check(wiring.cables.All(c=>c.graphic.gameObject.activeInHierarchy),"connected cables survive leaving and reentering");
                yield return Click("test_power");
                Check(!controller.State.Has("robot_repaired")&&controller.memory.CurrentData.DataIndex==3450,"test without a fuse points to the spare fuse");
                yield return Click("replace_fuse");
                Check(!controller.State.Has("insulated")&&!controller.State.inventory.Contains("tape"),"repair test has no tape or insulation prerequisite");
                yield return Click("test_power");yield return Ready();
                Check(controller.State.Has("robot_repaired")&&controller.State.Has("knock_heard"),"repair ends at knock");
                Check(controller.memory.CurrentData.DataIndex==controller.scenario.knockMemoryIndex,"daughter reaction is normal Memory data");
                Check(!controller.State.Has("power_off")&&LayerVisible("power")&&!LayerVisible("power_off"),"successful test restores original powered sprite");
                yield return Shot("08-repair-complete");
                // Simulate an older repaired checkpoint that still has power_off saved.
                controller.State.Set("power_off");
                manager.OSProcessData.memory.pictureScenario=JsonUtility.FromJson<OSMemoryPicSaveData>(JsonUtility.ToJson(controller.State));
                yield return null;yield return Ready();
                Check(!controller.State.Has("power_off")&&LayerVisible("power")&&!LayerVisible("power_off"),"loading an older repaired save restores the powered sprite");
                yield return Click("power");
                Check(controller.memory.CurrentData.DataIndex==3540&&!controller.State.Has("power_off"),"rechecking a repaired robot does not disconnect it again");
                yield return Click("back.wiring");
                Check(controller.State.currentSceneId=="workshop"&&controller.memory.CurrentData.DataIndex==3770,"repair return gives the front-door prompt");
                string[] flags=controller.State.flags.ToArray();
                Check(controller.ExitScenario(2660),"explicit scenario exit accepts next memory");
                Check(manager.OSProcessData.memory.pictureScenario.inventory.Count==0,"chapter exit clears local inventory");
                Check(manager.OSProcessData.memory.pictureScenario.Has("robot_repaired"),"chapter exit retains completion checkpoint");
                finished=true;
            }
            finally
            {
                SaveField.SetValue(manager,original);
                manager.SetCurrentLanguage(originalLanguage);
                Application.runInBackground=originalBackground;
                status=finished?"passed":"failed";
                File.WriteAllText("Temp/MemoryHomeQA/result.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{status,checks,saveRestored=ReferenceEquals(manager.SaveData,original)},Newtonsoft.Json.Formatting.Indented));
            }
        }
    }
}
