using System;
using System.Collections.Generic;

public sealed partial class OSChapter03StoryDirector
{
    OSChapter03StoryState visualInspectionState;
    int visualFlagCount = -1, visualPaperCount = -1, visualItemCount = -1;
    int visualVisitCount = -1, visualYardCount = -1, visualClock = -1;
    readonly List<string> dimmedThings = new List<string>();

    // Remember the response the player actually requested. A new useful action
    // gets a different key, so an earlier inspection cannot hide that opportunity.
    string ThingInspectionKey(string id)
    {
        int stage = 0;
        switch (id)
        {
            case "study_clock": case "hall_clock": case "kitchen_clock": stage = State.clock; break;
            case "kitchen_coffee_machine": stage = !Has("coffee_water") ? 0 : !Has("coffee_beans") ? 1 : 2; break;
            case "kitchen_sink": stage = Has("machine_seen") ? 1 : 0; break;
            case "kitchen_cup": stage = Has("coffee_brewed") ? 1 : 0; break;
            case "kitchen_screwdriver": stage = Has("store_door_seen") ? 1 : 0; break;
            case "store_key": stage = Has("yard_returned") ? 2 : CluesReady ? 1 : 0; break;
            case "bed_nightstand": stage = Has("coffee_placed") || State.items.Contains("coffee") ? 1 : 0; break;
            case "lab_bed": stage = Has("bed_first") && State.papers.Contains("diary_13") ? 1 : 0; break;
        }
        return id + ":" + stage;
    }

    void RecordThingInspection(string key)
    {
        if (!State.inspectedThings.Contains(key)) State.inspectedThings.Add(key);
        RefreshInteractionVisuals(true);
    }

    void RefreshInteractionVisuals(bool force)
    {
        var state = State;
        if (state == null || view?.Layout == null) return;
        state.EnsureData();
        MigrateInteractionInspections();
        if (!force && ReferenceEquals(visualInspectionState, state)
            && visualFlagCount == state.flags.Count && visualPaperCount == state.papers.Count
            && visualItemCount == state.items.Count && visualVisitCount == state.inspectedThings.Count
            && visualYardCount == state.yardVisited.Count && visualClock == state.clock) return;

        dimmedThings.Clear();
        foreach (var room in view.Layout.rooms)
            foreach (var thing in room.things)
                if (thing != null && IsThingInspectionComplete(thing.id)) dimmedThings.Add(thing.id);
        view.SetVisitedThings(dimmedThings);
        // This letter is not one of the ordinary inventory papers.
        view.SetThingVisible("lab_letter", Has("box_open") && !Has("letter_collected"));
        visualInspectionState = state;
        visualFlagCount = state.flags.Count; visualPaperCount = state.papers.Count;
        visualItemCount = state.items.Count; visualVisitCount = state.inspectedThings.Count;
        visualYardCount = state.yardVisited.Count; visualClock = state.clock;
    }

    bool IsThingInspectionComplete(string id)
    {
        if (id.StartsWith("yard_", StringComparison.Ordinal)) return State.yardVisited.Contains(id);
        // Opening a puzzle or a cinematic is not the same as finishing it.
        switch (id)
        {
            case "bath_mirror": return Has("mirror_done");
            case "east_keypad": return Has("lab_open");
            case "lab_box": return Has("box_open") || Has("live_returned");
            case "lab_hairclip": return Has("hairclip_seen") || Has("letter_started");
            case "lab_letter": return Has("letter_collected");
            case "lab_log": if (State.papers.Contains("repair_11")) return true; break;
        }
        for (int i = 0; i < PaperThings.GetLength(0); i++)
            if (PaperThings[i, 0] == id && State.papers.Contains(PaperThings[i, 1])) return true;
        return State.inspectedThings.Contains(ThingInspectionKey(id));
    }

    // Old saves have no per-object record. Recover only evidence tied to that
    // exact object and current response, never a shared clue or room visit.
    void MigrateInteractionInspections()
    {
        if (State.interactionVisualVersion >= 1) return;
        foreach (var room in view.Layout.rooms)
            foreach (var thing in room.things)
            {
                if (thing == null || !LegacyThingInspected(thing.id)) continue;
                string key = ThingInspectionKey(thing.id);
                if (!State.inspectedThings.Contains(key)) State.inspectedThings.Add(key);
            }
        State.interactionVisualVersion = 1;
    }

    bool LegacyThingInspected(string id)
    {
        if (IsThingInspectionComplete(id)) return true;
        string key;
        switch (id)
        {
            // Clock text is shared by several clocks, and old saves do not say
            // which object was clicked or at what time. Leave it available.
            case "study_clock": case "hall_clock": case "kitchen_clock": return false;
            case "bath_mirror": case "east_keypad": case "lab_box": case "lab_hairclip": case "lab_letter": return false;
            case "kitchen_coffee_machine":
                if (Has("coffee_brewed")) return true;
                key = !Has("coffee_water") ? "interaction.kitchen.machine.cold"
                    : !Has("coffee_beans") ? "interaction.kitchen.machine.needs_beans" : "interaction.kitchen.machine.brew";
                if (!Has("coffee_water") && Has("machine_seen")) return true;
                break;
            case "kitchen_sink":
                if (Has("coffee_water")) return true;
                key = Has("machine_seen") ? "interaction.kitchen.sink.fill" : "interaction.kitchen.sink.inspect";
                break;
            case "kitchen_cup":
                if (Has("coffee_taken")) return true;
                key = Has("coffee_brewed") ? "interaction.kitchen.cup" : "interaction.kitchen.cup.empty";
                break;
            case "kitchen_screwdriver":
                return Has("screwdriver_taken") || !Has("store_door_seen") && WasInspectionNarrated("interaction.kitchen.screwdriver.before_door");
            case "store_key":
                if (Has("key_taken")) return true;
                key = Has("yard_returned") ? "interaction.store.key.take" : CluesReady
                    ? "interaction.store.key.before_yard" : "interaction.store.key.before_evidence";
                break;
            case "bed_nightstand":
                // coffee_placed can also come from the laboratory bed.
                key = Has("coffee_placed") || State.items.Contains("coffee")
                    ? "interaction.bed.nightstand.coffee" : "interaction.bed.nightstand.empty";
                break;
            case "lab_bed":
                return Has("bed_second_started") || Has("bed_first") && !State.papers.Contains("diary_13");
            case "kitchen_calendar": if (Has("calendar_seen")) return true; key = "interaction.kitchen.calendar"; break;
            case "kitchen_sugar": if (Has("medicine_seen")) return true; key = "interaction.kitchen.sugar"; break;
            case "kitchen_drawer": if (Has("drawer_seen")) return true; key = "interaction.kitchen.drawer"; break;
            case "kitchen_coffee_jar": if (Has("coffee_beans")) return true; key = "interaction.kitchen.beans"; break;
            case "kitchen_fridge": key = "interaction.kitchen.fridge"; break;
            case "study_desk": key = "interaction.study.desk"; break;
            case "store_makeup": if (Has("makeup_seen")) return true; key = "interaction.store.makeup"; break;
            case "store_old_leg": key = "interaction.store.old_leg"; break;
            case "lab_mask": key = "interaction.lab.cast"; break;
            case "lab_mask_note": key = "interaction.lab.cast_note"; break;
            case "lab_pillow_note": key = "interaction.lab.pillow_note"; break;
            case "lab_log": key = "interaction.lab.repair_log"; break;
            default: key = "interaction." + id.Replace('_', '.'); break;
        }
        return WasInspectionNarrated(key);
    }

    bool WasInspectionNarrated(string key)
    {
        int node = Content.Node(key);
        if (node > 0)
            foreach (int shown in GameDataManager.Instance.TriggeredOSMemoryDataIndices)
                if (shown == node) return true;
        string fullKey = OSChapter03StoryContent.FullKey(key);
        return State.memoryQueue.Exists(beat => beat.type == "text"
            && (beat.key == key || beat.key == fullKey));
    }
}
