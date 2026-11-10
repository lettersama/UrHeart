using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class OSChapter03StoryDirector
{
    OSHomeLayout appliedLayout;

    public void RebuildHomeState()
    {
        if (!IsActive) return;
        Resolve();
        if (runtimeMemoryBeat?.type == "walk" || runtimeMemoryBeat?.key == "absorb_dong") runtimeMemoryStarted = false;
        rebinding = true;
        try { ApplyWorldState(); }
        finally { rebinding = false; }
    }

    void ApplyWorldState()
    {
        if (view?.Layout == null || State == null) return;
        view.SetDoorLocked("store_door", !Has("store_open"));
        view.SetDoorLocked("bed_door", !Has("bed_open"));
        view.SetDoorLocked("lab_door", !Has("lab_open"));
        view.SetDoorLocked("front_door", true);
        view.SetDoorLocked("yard_house", true);
        view.SetThingVisible("kitchen_screwdriver", Has("drawer_seen") && !Has("screwdriver_taken"));
        view.SetThingVisible("store_key", Has("makeup_seen") && !Has("key_taken"));
        ApplyLabGuide();
        view.SetThingVisible("lab_checklist", false);
        foreach (var room in view.Layout.rooms)
            foreach (var thing in room.things)
                if (thing.id == "west_painting" || thing.id == "bed_pills" || thing.id == "hall_table" || thing.id == "lab_or_lamp"
                    || thing.id == "lab_bench_lamp")
                    thing.interactive = false;
        if (!(runtimeMemoryStarted && runtimeMemoryBeat?.key == "absorb_dong" && !runtimeMemoryBeat.complete))
            view.SetDongVisible(!Has("dong_absorbed"));
        view.SetPlayerVisible(Has("home_revealed"));
        RefreshInteractionVisuals(true);
        view.SetFootprints("yard", State.yardFootprints);
        bool close = Has("yard_zoom") && !Has("yard_zoom_done");
        // Rebinding also cancels an interrupted blink whose target already
        // matches the save but whose displayed crop has not switched yet.
        view.SetZoom(close, true);
        appliedLayout = view.Layout;
        if (items == null) items = view.transform.parent.GetComponentInChildren<OSHomeItemBar>(true);
        if (items == null) items = OSHomeItemBar.CreateBelow(view);
        items.ItemClicked -= OnItemClicked; items.ItemClicked += OnItemClicked;
        items.Clear(); itemSnapshot.Clear();
        foreach (string item in ShownItems()) { AddShownItem(item, false); itemSnapshot.Add(item); }
        MarkPapers(true);
    }

    void RefreshPersistentPresentation()
    {
        if (view?.Layout != null && appliedLayout != view.Layout) ApplyWorldState();
        else MarkPapers(false);
        RefreshInteractionVisuals(false);
        if (items != null && State != null)
        {
            var shown = ShownItems();
            for (int i = itemSnapshot.Count - 1; i >= 0; i--)
                if (!shown.Contains(itemSnapshot[i]))
                { items.Remove(itemSnapshot[i]); itemSnapshot.RemoveAt(i); }
            foreach (var item in shown)
                if (!itemSnapshot.Contains(item)) { AddShownItem(item, true); itemSnapshot.Add(item); }
        }
        RefreshLivePresence();
    }

    void OnFloorClicked(Vector2Int tile)
    {
        if (!IsActive || rebinding || home?.IsRestoringView == true || !State.homeOpened || !Once("first_floor")) return;
        Say("memory.8100", 1.2f); Say("memory.8110");
    }

    void OnRoom(OSHomeRoom room)
    {
        if (!IsActive || rebinding || home?.IsRestoringView == true || room == null || OSSessionPersistence.IsRestoring) return;
        PlayRoomSfx(room);
        ApplyWorldState();
        if (!State.homeOpened || IsCinematic) return;
        if (room.id == "east" && State.liveOffline)
        {
            if (Once("offline_east"))
            { Say("memory.9210"); State.Add("password_ready"); }
            return;
        }
        if (!Once("room_" + room.id)) return;
        switch (room.id)
        {
            case "west": Say("memory.west.enter"); break;
            case "hall": Say("memory.hall.enter"); Say("memory.hall.heartbeat"); break;
            case "kitchen": State.clock = Mathf.Max(State.clock, 1); Say("memory.kitchen.enter"); break;
            case "east": Say("memory.east.enter"); break;
            case "store": Say("memory.store.enter"); Say("memory.store.heartbeat"); break;
            case "bed": State.clock = 3; Say("memory.bed.enter"); break;
            case "bath": Say("memory.9000"); break;
        }
    }

    void OnStep(Vector2Int tile, bool onWater)
    {
        if (!IsActive || rebinding || home?.IsRestoringView == true || OSSessionPersistence.IsRestoring || view?.CurrentRoom == null) return;
        if (!Has("first_floor") && !IsCinematic) OnFloorClicked(tile);
        string room = view.CurrentRoom.id;
        if (room == "yard" && !State.yardFootprints.Contains(tile))
        {
            State.yardFootprints.Add(tile);
            view.SetFootprints("yard", State.yardFootprints);
        }
        if (State.liveOffline && !IsCinematic)
        {
            if (Once("left_mirror")) Say("memory.9200");
            if (room == "east" && Has("offline_east") && Once("offline_east_leak"))
            {
                Live("live.offline.east");
                QueueInitialOfflineOfferAfterCry();
            }
            if (room == "lab" && Vector2Int.Distance(tile, new Vector2Int(7, 4)) <= 3.2f && Once("lab_master_seen"))
                Say("memory.9310");
        }
        PlayHomeStep(onWater);
    }

    void OnThing(OSHomeThing thing)
    {
        if (thing == null || !IsAvailable || BlocksHomeInput || rebinding) return;
        QueueInteraction("thing:" + thing.id, () =>
        {
            string inspection = ThingInspectionKey(thing.id);
            HandleThing(thing);
            RecordThingInspection(inspection);
        });
    }

    void HandleThing(OSHomeThing thing)
    {
        string id = thing.id;
        PlayThingSfx(id);
        if (id.StartsWith("yard_", StringComparison.Ordinal)) { InspectYard(id); return; }
        switch (id)
        {
            case "study_clock": case "hall_clock": Clock(); return;
            case "kitchen_clock":
                if (State.clock >= 3) Clock();
                else Say(State.clock >= 2 ? "interaction.kitchen.clock.after_evidence" : "interaction.kitchen.clock.before_evidence");
                return;
            case "study_desk": Paper("diary_07", "interaction.study.desk"); return;
            case "study_page":
                Say("interaction.study.page"); if (Once("study_page_live")) Live("live.study.unfinished_page"); return;
            case "west_note": Paper("diary_05", "interaction.west.note"); return;
            case "hall_paper": Paper("diary_04", "interaction.hall.paper"); return;
            // Shoes, coat and umbrella are one clue: whichever is looked at first settles it.
            case "hall_shoes": case "hall_coat": case "hall_umbrella":
                Say("interaction." + id.Replace('_', '.'));
                if (Once("shoes_seen") && !CluesReady) Say("memory.evidence.shoes");
                GuideAfterEvidence();
                return;
            case "kitchen_calendar":
                Say("interaction.kitchen.calendar");
                if (State.Add("calendar_seen")) CheckMorningEvidence(false);
                GuideAfterEvidence();
                return;
            case "kitchen_sugar":
                Say("interaction.kitchen.sugar");
                State.Add("medicine_seen");
                GuideAfterEvidence();
                return;
            case "kitchen_coffee_machine":
                State.Add("machine_seen");
                if (!Has("coffee_water")) Say("interaction.kitchen.machine.cold");
                else if (!Has("coffee_beans")) Say("interaction.kitchen.machine.needs_beans");
                else if (Once("coffee_brewed")) { Say("interaction.kitchen.machine.brew"); State.coffeeStep = 4; }
                else Say("interaction.kitchen.machine.ready");
                CheckMorningEvidence(true);
                GuideAfterEvidence();
                return;
            case "kitchen_sink":
                if (Has("machine_seen") && Once("coffee_water"))
                { State.coffeeStep = Mathf.Max(State.coffeeStep, 2); Say("interaction.kitchen.sink.fill"); }
                else
                {
                    Say("interaction.kitchen.sink.inspect");
                    if (Once("sink_live")) Live("live.kitchen.one_cup");
                }
                return;
            case "kitchen_coffee_jar":
                State.Add("coffee_beans"); State.coffeeStep = Mathf.Max(State.coffeeStep, 3);
                Paper("diary_02", "interaction.kitchen.beans"); return;
            case "kitchen_cup":
                if (!Has("coffee_brewed")) { Say("interaction.kitchen.cup.empty"); return; }
                if (Once("coffee_taken")) { AddItem("coffee"); State.coffeeStep = 5; }
                Paper("diary_03", "interaction.kitchen.cup"); return;
            case "kitchen_fridge": Paper("diary_06", "interaction.kitchen.fridge"); return;
            case "kitchen_drawer":
                Say("interaction.kitchen.drawer"); State.Add("drawer_seen");
                view.SetThingVisible("kitchen_screwdriver", !Has("screwdriver_taken")); return;
            case "kitchen_screwdriver":
                if (!Has("store_door_seen")) Say("interaction.kitchen.screwdriver.before_door");
                else if (Once("screwdriver_taken"))
                { Say("interaction.kitchen.screwdriver.take"); AddItem("screwdriver"); view.SetThingVisible(id, false); }
                return;
            case "store_makeup":
                State.Add("makeup_seen"); view.SetThingVisible("store_key", !Has("key_taken"));
                Paper("diary_08", "interaction.store.makeup"); return;
            case "store_key":
                if (Has("yard_returned"))
                {
                    if (Once("key_taken"))
                    { Say("interaction.store.key.take"); AddItem("key"); view.SetThingVisible(id, false); }
                }
                else Say(CluesReady ? "interaction.store.key.before_yard" : "interaction.store.key.before_evidence");
                return;
            case "store_old_leg":
                Say("interaction.store.old_leg"); Art("old-leg", "ui.art.old_leg");
                if (Once("old_leg_live")) Live("live.store.old_leg"); return;
            case "bed_letter": Paper("husband_letter", "interaction.bed.letter"); return;
            case "bed_nightstand":
                if (State.items.Contains("coffee"))
                { Say("interaction.bed.nightstand.coffee"); RemoveItem("coffee"); State.Add("coffee_placed"); }
                else Say(Has("coffee_placed") ? "interaction.bed.nightstand.coffee" : "interaction.bed.nightstand.empty");
                return;
            case "bath_mirror":
                if (!Has("mirror_done") && !Has("paper_requested_note_09"))
                { State.phase = (int)Phase.Mirror; Paper("note_09", "memory.9010", alongside: false); }
                return;
            case "bath_sink": case "bath_towel": return;
            case "east_keypad": ShowPassword(); return;
            case "lab_bed": InspectBed(); return;
            case "lab_mask": Say("interaction.lab.cast"); return;
            case "lab_mask_note": Paper("diary_10", "interaction.lab.cast_note"); return;
            case "lab_log": Paper("repair_11", "interaction.lab.repair_log"); return;
            case "lab_pillow_note": Paper("diary_13", "interaction.lab.pillow_note"); return;
            case "lab_box":
                if (Has("bed_second_done") && !Has("live_returned")) StartReunion();
                return;
            case "lab_hairclip": if (Has("box_open")) ShowHairclip(); return;
            case "lab_letter": if (Has("box_open")) StartFinalLetter(); return;
        }
        string key = "interaction." + id.Replace('_', '.');
        // These room ids contain an underscore in the object, not a hierarchy.
        if (id == "store_old_leg") key = "interaction.store.old_leg";
        if (Content.Find(key) != null) Say(key);
    }

    void OnDoor(OSHomeDoor door)
    {
        if (door == null || !IsAvailable || BlocksHomeInput || rebinding) return;
        QueueInteraction("door:" + door.id, () => HandleDoor(door));
    }

    void HandleDoor(OSHomeDoor door)
    {
        PlayDoorSfx(door);
        switch (door.id)
        {
            case "bed_door":
                if (!door.locked) return;
                if (State.items.Contains("key"))
                {
                    RemoveItem("key"); State.Add("bed_open"); State.clock = 3;
                    view.SetDoorLocked("bed_door", false); return;
                }
                if (Has("deduction_done")) { StartFinalKnock(); return; }
                if (Once("bed_knocked")) { Say("interaction.west.bed_door.first"); return; }
                if (State.items.Contains("coffee") && Once("coffee_knocked")) Say("interaction.west.bed_door.coffee");
                else Say("interaction.west.bed_door.repeat");
                // From the second knock it doubts, and the door names one open question.
                RaiseDoubt();
                SayDoorQuestion();
                return;
            case "store_door":
                if (!door.locked) return;
                State.Add("store_door_seen");
                if (State.items.Contains("screwdriver"))
                {
                    RemoveItem("screwdriver"); State.Add("store_open");
                    Say("interaction.east.store_door.open");
                    view.SetDoorLocked("store_door", false);
                }
                else Say("interaction.east.store_door");
                return;
            case "lab_door": if (door.locked) ShowPassword(); return;
            case "stuck_door": Say("interaction.east.stuck_door"); return;
            case "junk_door": if (Once("junk_door_seen")) Say("interaction.east.junk_door"); return;
            case "front_door":
                Say(Has("yard_returned") ? "interaction.hall.front_door.after_yard" : "interaction.hall.front_door"); return;
            case "yard_house":
                // The authored return is automatic after all six objects.
                door.locked = true; return;
        }
    }

    void Clock()
    {
        string[] times = { "1452", "1500", "1610", "1640" };
        Say("interaction.clock." + times[Mathf.Clamp(State.clock, 0, 3)]);
    }
    // The summary is the coffee machine's to say (pancake 10-24): at the calendar it would bury
    // the guidance. The last clue skips it too: the closing line already says it all.
    void CheckMorningEvidence(bool atMachine)
    {
        if (!Has("machine_seen") || !Has("calendar_seen")) return;
        State.Add("morning_seen");
        if (!atMachine || CluesReady || !Once("morning_said")) return;
        const string summary = "memory.evidence.morning";
        // Older saves predate morning_said; their queued or displayed summary still counts.
        if (State.memoryQueue.Exists(beat => beat.type == "text"
            && (beat.key == summary || beat.key == OSChapter03StoryContent.FullKey(summary)))) return;
        int node = Content.Node(summary);
        foreach (int shown in GameDataManager.Instance.TriggeredOSMemoryDataIndices)
            if (shown == node) return;
        Say(summary);
    }
    // The storage room is no longer evidence (pancake 10-24): the makeup bag, its diary and the
    // key wait until the yard makes it urgent.
    bool CluesReady => Has("shoes_seen") && Has("morning_seen") && Has("medicine_seen");
    void TryQueueDeduction()
    {
        if (!CluesReady || Has("deduction_queued") || IsCinematic) return;
        State.Add("deduction_queued"); State.clock = Mathf.Max(2, State.clock);
        Say("memory.evidence.complete", .9f); Say("memory.evidence.return_to_door");
        Enqueue("flag", "deduction_done");
    }

    // A page found in the home opens at once, with its line written beside it.
    void Paper(string id, string intro, bool alongside = true)
    {
        Say(intro);
        if (!State.papers.Contains(id) && Once("paper_requested_" + id)) Enqueue("paper", id, number: alongside ? 1 : 0);
    }
    string PaperTitle(string id)
    {
        var paper = Content.FindPaper(id);
        return Text(paper?.type == "instruction_note" ? "os.chapter03.ui.paper.note" : paper?.type == "repair_record"
            ? "os.chapter03.ui.paper.record" : paper?.type == "diary" ? "os.chapter03.ui.paper.diary" : "os.chapter03.ui.paper.letter");
    }

    void CollectPaper(string id)
    {
        if (State.papers.Contains(id)) return;
        State.papers.Add(id);
        var paper = Content.FindPaper(id);
        if (paper?.type == "diary")
        {
            var data = GameDataManager.Instance;
            OSStoryRecordUnlocks.RefreshDiary(data, newlyCollected: true);
            string format = data.GetLocalizedText("os.chapter03.ui.diary_record.collected", "日记#{0}已收录记录");
            AdditionHandle.Instance?.ShowTip(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, PaperNumber(id)));
        }
        if (paper?.collectInLetterSlot == true)
        { AddItem("letters"); Say("memory.paper_collected"); }
        switch (id)
        {
            case "note_01":
                State.homeOpened = true; State.phase = (int)Phase.Explore;
                Say("memory.8090"); break;
            case "diary_06": StoryBeat(1.6f); break;
            case "diary_07": Live("live.paper.diary07"); break;
            case "diary_08": if (Once("knows_question")) Live("live.store.knows_question"); break;
            case "husband_letter": Live("live.paper.husband_letter"); break;
            case "note_09": StartMirror(); break;
            case "diary_13":
                Say("memory.lab.diary13.last_page"); Say("memory.lab.diary13.heartbeat");
                Effect("absorb_dong", seconds: 1.2f); Enqueue("live", "live.offline.diary13"); break;
        }
    }

    // ------------------------------------------------------------ papers in the item bar
    // Each paper read is kept in the bar as its own item and can be read again.
    // The bar is derived from State.papers, so saves keep their shape; the old
    // shared "letters" item stays in State.items but is no longer shown.
    const string PaperItemPrefix = "paper:";
    string reopenedPaper;
    readonly List<string> shownItems = new List<string>();
    readonly Dictionary<string, string> paperItemIds = new Dictionary<string, string>(StringComparer.Ordinal);

    List<string> ShownItems()
    {
        var shown = shownItems;
        shown.Clear();
        foreach (var item in State.items) if (item != "letters") shown.Add(item);
        foreach (var id in State.papers)
            if (KeepsPaper(id))
            {
                if (!paperItemIds.TryGetValue(id, out string itemId))
                    paperItemIds[id] = itemId = PaperItemPrefix + id;
                shown.Add(itemId);
            }
        return shown;
    }

    bool KeepsPaper(string id)
    {
        var paper = Content.FindPaper(id);
        // The repair record stays on the lab wall; the final letter goes to Live.
        return paper != null && paper.type != "repair_record" && paper.type != "final_letter";
    }

    bool AddShownItem(string id, bool animate)
    {
        PlayItemStowSfx(id, animate);
        if (!id.StartsWith(PaperItemPrefix, StringComparison.Ordinal)) return items.Add(id, animate);
        string paper = id.Substring(PaperItemPrefix.Length);
        var item = new OSHomeItemBar.Item { id = id, ink = OSHomeInk.Gray, order = PaperOrder(paper) };
        NamePaperItem(item, paper);
        return items.Add(item, animate);
    }

    void NamePaperItem(OSHomeItemBar.Item item, string paper)
    {
        // The word takes the glyph cells; the number follows in small capitals.
        string title = PaperTitle(paper);
        item.glyph = title;
        item.tag = PaperTag(paper);
        item.label = title + item.tag;
        item.width = OSHomeGlyphBook.IsWide(title)
            ? new System.Globalization.StringInfo(title).LengthInTextElements
            : Mathf.Clamp((title.Length + 3) / 4, 1, 2);
    }

    static string PaperTag(string paper)
    {
        int number = PaperNumber(paper);
        return number > 0 ? "#" + number : string.Empty;
    }

    // ------------------------------------------------------------ pages lying in the home
    // A thing holding a page shows the page's red word over its own until the
    // page is picked up; loose pages leave the floor with it.
    static readonly string[,] PaperThings =
    {
        { "study_desk", "diary_07" }, { "west_note", "diary_05" }, { "hall_paper", "diary_04" },
        { "kitchen_coffee_jar", "diary_02" }, { "kitchen_cup", "diary_03" }, { "kitchen_fridge", "diary_06" },
        { "store_makeup", "diary_08" }, { "bed_letter", "husband_letter" },
        { "lab_mask_note", "diary_10" }, { "lab_pillow_note", "diary_13" },
    };
    static readonly HashSet<string> LoosePapers = new HashSet<string>
        { "west_note", "hall_paper", "bed_letter", "lab_mask_note", "lab_pillow_note" };
    readonly Dictionary<string, string> markedPapers = new Dictionary<string, string>();

    void MarkPapers(bool force)
    {
        if (view?.Layout == null || State == null) return;
        if (force) markedPapers.Clear();
        for (int i = 0; i < PaperThings.GetLength(0); i++)
        {
            string thing = PaperThings[i, 0], paper = PaperThings[i, 1];
            bool taken = State.papers.Contains(paper);
            // The page under the cup is only found once the coffee is poured.
            string title = taken || thing == "kitchen_cup" && !Has("coffee_brewed") ? null : PaperTitle(paper);
            markedPapers.TryGetValue(thing, out string shown);
            if (LoosePapers.Contains(thing)) view.SetThingVisible(thing, !taken);
            if (!force && shown == title) continue;
            view.SetThingPaper(thing, title, title == null ? null : title + PaperTag(paper));
            if (title == null) markedPapers.Remove(thing); else markedPapers[thing] = title;
        }
    }

    void RenamePaperItems()
    {
        if (items == null) return;
        foreach (var id in itemSnapshot)
        {
            if (!id.StartsWith(PaperItemPrefix, StringComparison.Ordinal)) continue;
            var item = items.GetItem(id);
            if (item != null) NamePaperItem(item, id.Substring(PaperItemPrefix.Length));
        }
    }

    // note_01 is 便条#1, diary_02 is 日记#2; the letter to her husband has no number.
    static int PaperNumber(string id)
    {
        int split = id.LastIndexOf('_');
        return split >= 0 && int.TryParse(id.Substring(split + 1), out int number) ? number : 0;
    }
    // After the tools, in the order the papers are numbered; the letter was found after #8.
    static int PaperOrder(string id)
    {
        int number = PaperNumber(id);
        return 100 + (number > 0 ? number * 10 : 85);
    }

    static string PaperStyle(string id) =>
        id == "note_01" || id == "note_09" ? "note" : id == "husband_letter" ? "letter" : "diary";

    static bool IsModalBeat(OSChapter03StoryBeat beat) => beat != null
        && (beat.type == "paper" || beat.type == "art" || beat.type == "letter" || beat.type == "keypad");

    void OnItemClicked(string id)
    {
        if (string.IsNullOrEmpty(id) || !id.StartsWith(PaperItemPrefix, StringComparison.Ordinal)) return;
        if (!IsActive || IsCinematic || presentation == null || presentation.IsModalOpen || presentation.IsAnimating) return;
        if (State.memoryQueue.Count > 0 && IsModalBeat(State.memoryQueue[0])) return;
        string paper = id.Substring(PaperItemPrefix.Length);
        if (!State.papers.Contains(paper)) return;
        // Only a reading: nothing is collected, queued or saved, and closing works as before.
        reopenedPaper = paper;
        presentation.ShowPaper(PaperTitle(paper), Content.PaperBody(paper), () => reopenedPaper = null, PaperStyle(paper));
        PlayPaperSfx();
    }

    void AddItem(string id) { if (!State.items.Contains(id)) State.items.Add(id); }
    void RemoveItem(string id) => State.items.Remove(id);

    void InspectYard(string id)
    {
        if (CurrentPhase != Phase.Yard || Has("yard_returned")) return;
        string suffix = id.Substring(5);
        if (suffix == "bedwindow") suffix = "window";
        if (suffix == "clothes") suffix = "clothesline";
        // The last look (usually the window) closes in on 「我」 and stays close until back inside.
        if (!State.yardVisited.Contains(id) && State.yardVisited.Count == 5 && !Has("yard_all_seen")) Effect("zoom_in");
        Say("interaction.yard." + suffix);
        if (!State.yardVisited.Contains(id))
        { State.yardVisited.Add(id); view.SetThingVisited(id, true); }
        if (id != "yard_bedwindow" && !State.yardVisited.Contains("yard_bedwindow"))
            Say("interaction.yard.window_hint");
        if (State.yardVisited.Count >= 6 && Once("yard_all_seen")) StartYardReturn();
    }

    // pancake 10-27: the room is dark but for the bench lamp and its blood trail. The first touch of
    // the bed only looks (the hand close-up waits for the second) and the bed lamp stutters on.
    void InspectBed()
    {
        if (Once("bed_first"))
        {
            Say("interaction.lab.bed.first");
            if (Once("lab_lamp_on")) { view.WarmLight("lab_or_lamp"); PlaySfxCue("Chapter03_LampFlicker", .3f); }
            if (State.items.Contains("coffee"))
            { Say("interaction.lab.bed.place_coffee"); RemoveItem("coffee"); State.Add("coffee_placed"); }
            return;
        }
        if (!State.papers.Contains("diary_13")) { Say("interaction.lab.bed.first"); return; }
        if (!Once("bed_second_started")) return;
        StartSecondBed();
    }

    // Red and a slow breathing light on whatever comes next: the box in her hand, the hairclip, then the envelope.
    void ApplyLabGuide()
    {
        bool lampOn = Has("lab_lamp_on") || Has("bed_first"); // older saves looked at the bed before the lamp existed
        bool boxWaiting = Has("bed_second_done") && !Has("box_open");
        bool hairclipSeen = Has("hairclip_seen") || Has("letter_started");
        bool hairclipWaiting = Has("box_open") && !hairclipSeen;
        bool letterWaiting = Has("box_open") && hairclipSeen && !Has("letter_started");
        view.SetLightOn("lab_or_lamp", lampOn);
        view.SetThingVisible("lab_box", Has("bed_second_done"));
        view.SetThingVisible("lab_hairclip", Has("box_open"));
        view.SetThingVisible("lab_letter", Has("box_open") && !Has("letter_collected"));
        view.SetThingInk("lab_box", boxWaiting ? OSHomeInk.Red : OSHomeInk.Gray);
        view.SetThingInk("lab_hairclip", hairclipWaiting ? OSHomeInk.Red : OSHomeInk.Gray);
        view.SetThingInk("lab_letter", letterWaiting ? OSHomeInk.Red : OSHomeInk.Gray);
        var box = view.FindThing("lab_box");
        if (box != null) box.interactive = !Has("live_returned");
        SetLabGlow("lab_box_glow", boxWaiting);
        SetLabGlow("lab_hairclip_glow", hairclipWaiting);
        SetLabGlow("lab_letter_glow", letterWaiting);
    }

    void SetLabGlow(string lightId, bool on) { view.SetLightOn(lightId, on); view.SetLightPulse(lightId, on); }

    void ShowHairclip()
    {
        Say("interaction.lab.box"); Art("hairclip", "ui.art.hairclip");
        if (!Once("hairclip_seen")) return;
        Enqueue("live", "live.letter.hairclip"); WaitLive();
        Effect("hairclip"); Effect("rebuild_world");
    }

    void ShowPassword()
    {
        if (Has("lab_open")) return;
        if (!Has("lab_door_seen")) { State.Add("lab_door_seen"); Say("interaction.east.lab_door"); }
        Enqueue("keypad");
    }

    bool RunKeypad(OSChapter03StoryBeat beat)
    {
        if (!runtimeMemoryStarted)
        {
            runtimeMemoryStarted = true; int token = generation;
            presentation.ShowKeypad(Text("ui.password.title"), Text("ui.password.format"), input =>
            {
                if (token != generation) return;
                WithInteractionSource(beat.interactionSource, () =>
                {
                    PlayKeypadResultSfx(Has("password_ready") && input == "0710");
                    if (Has("password_ready") && input == "0710")
                    {
                        presentation.HideModal(false); beat.complete = true;
                        State.Add("lab_open"); State.phase = (int)Phase.Lab;
                        view.SetDoorLocked("lab_door", false);
                        Say("memory.9300"); Enqueue("live", "live.offline.lab_open");
                        Enqueue("room", "lab", "1,4");
                        Enqueue("live", "live.offline.lab_offer");
                    }
                    else
                    {
                        presentation.SetKeypadFeedback(Text("ui.password.error"));
                        if (Has("password_ready")) Say("interaction.lab.password_hint");
                    }
                });
            }, () => { if (token == generation) beat.complete = true; });
        }
        return beat.complete;
    }
}
