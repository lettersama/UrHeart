using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class OSChapter03StoryDirector
{
    void StartFinalKnock()
    {
        if (!Once("final_knock")) return;
        State.phase = (int)Phase.FinalKnock;
        view.StopWalking();
        Say("memory.west.bed_door.final_knock", 1.5f);
        Say("memory.west.bed_door.panic", 1.2f);
        Effect("reasoning", seconds: 8.6f); // Baba rules need time to be read
        Enqueue("room", "yard", "7,1"); // just outside the front door
        Effect("yard_arrival");
        Say("memory.yard.arrive", 1.8f); Say("memory.yard.search", 1f);
        Enqueue("phase", number: (int)Phase.Yard);
    }

    void StartYardReturn()
    {
        State.phase = (int)Phase.YardReturn;
        // Objects near the centre walk fewer of these steps; the lines still play in order.
        // Capture the route in the saved queue so reopening midway resumes it.
        var route = FindYardRoute(view.PlayerTile, new Vector2Int(7, 4));
        if (route.Count > 0) Enqueue("walk", value: Tile(route[0]));
        Say("memory.yard.center.rain", .5f);
        if (route.Count > 2) Enqueue("walk", value: Tile(route[2]));
        Say("memory.yard.center.scolding", .5f);
        Enqueue("walk", value: "7,4");
        Say("memory.yard.center.footprints", 1.8f);
        Enqueue("walk", value: "7,1");
        Enqueue("room", "hall", "7,8");
        // Still close on 「我」 inside the door: the monologue runs in a white strip across
        // the map, then the view steps back out to the whole house (pancake 10-25).
        WaitLive(); Effect("resolve", seconds: 2.2f);
        Effect("zoom_out");
        Enqueue("flag", "yard_returned");
        Enqueue("phase", number: (int)Phase.KeyReady);
        PromptForKey();
    }

    List<Vector2Int> FindYardRoute(Vector2Int from, Vector2Int to)
    {
        var queue = new Queue<Vector2Int>(); var previous = new Dictionary<Vector2Int, Vector2Int>();
        queue.Enqueue(from); previous[from] = from;
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        while (queue.Count > 0 && !previous.ContainsKey(to))
        {
            var at = queue.Dequeue();
            foreach (var direction in directions)
            {
                var next = at + direction;
                if (previous.ContainsKey(next) || !YardWalkable(next)) continue;
                previous[next] = at; queue.Enqueue(next);
            }
        }
        var result = new List<Vector2Int>();
        if (!previous.ContainsKey(to)) return result;
        for (var at = to; at != from; at = previous[at]) result.Add(at);
        result.Reverse(); return result;
    }
    static string Tile(Vector2Int tile) => tile.x + "," + tile.y;
    bool YardWalkable(Vector2Int tile)
    {
        if (!view.CurrentRoom.OnFloor(tile)) return false;
        foreach (var thing in view.CurrentRoom.things)
            if (!thing.hidden && thing.blocksWalk && thing.Covers(tile)) return false;
        return true;
    }

    void StartMirror()
    {
        State.phase = (int)Phase.Mirror;
        Say("memory.9015", 1.5f); Say("memory.9020", 1.8f);
        Say("memory.9030", 1.2f); Say("memory.9040", 1.3f); Say("memory.9045", 1f);
        // Face to face with the reflection, then the whole game zooms in on her window for the
        // breakdown and back out after her last line (pancake 10-25).
        Effect("mirror_pause"); // completion follows the cursor, not a timed deadline
        Effect("live_to_center", seconds: 2f);
        // The second Live line starts the slow focus on its own presentation event.
        for (int i = 2; i <= 7; i++)
            Enqueue("live", i == 4 ? (State.knowAnswer == 1 ? "live.breakdown.04_yes" : "live.breakdown.04_no")
                : "live.breakdown." + i.ToString("00"));
        WaitLive(); Effect("failed_typing", seconds: 4f);
        Enqueue("live", "live.breakdown.08"); Enqueue("live", "live.breakdown.09"); WaitLive();
        Effect("closeup_close", seconds: 1.2f);
        Effect("live_to_home", seconds: 2f); Effect("go_offline", seconds: 1.8f);
        Effect("wait_mirror_resume");
        Say("memory.9048", 1.5f); Say("memory.9060", 2f); Say("memory.9080", 1.5f);
        Effect("close_mirror"); Enqueue("flag", "mirror_done");
        Enqueue("phase", number: (int)Phase.Offline);
    }

    const string InitialOfflineOfferKey = "live.offline.initial_offer";

    static bool IsInitialOfflineOffer(OSChapter03StoryBeat beat)
        => beat.type == "live" && beat.key == InitialOfflineOfferKey;

    void DeferPendingInitialOfflineOffer()
    {
        if (Has("offline_east_leak")) return;
        // Older saves queued this at the end of the mirror. Move only beats
        // that have not started; an already-presented choice keeps its state.
        State.memoryQueue.RemoveAll(beat => IsInitialOfflineOffer(beat) && !beat.begun && !beat.complete);
        State.liveQueue.RemoveAll(beat => IsInitialOfflineOffer(beat) && !beat.begun && !beat.complete);
    }

    void QueueInitialOfflineOfferAfterCry()
    {
        // The per-entry consumption record also covers choices that timed out
        // in an older save; the shared offline read receipt does not.
        if (GameDataManager.Instance.OSCharacterInteractionState.HasConsumedGlobalEntry(LiveId(InitialOfflineOfferKey))
            || State.memoryQueue.Exists(IsInitialOfflineOffer) || State.liveQueue.Exists(IsInitialOfflineOffer)) return;
        Live(InitialOfflineOfferKey);
    }

    void StartSecondBed()
    {
        State.phase = (int)Phase.BedSecond;
        Say(Has("coffee_taken") ? "memory.lab.bed.second.call" : "memory.lab.bed.second.call_without_coffee", 2f);
        // Keep the existing three-second pause before the heartbeat automatic.
        // The saved art beat owns that pause, so restoring/pausing cannot skip it.
        Say("memory.lab.bed.second.cold", 0f);
        Art("bed-hand", "os.home.lab.bed.label", autoCloseSeconds: 3f);
        Say("memory.lab.bed.second.heartbeat", 2f);
        Enqueue("live", "live.offline.bed"); WaitLive();
        Effect("reveal_box");
        // pancake 10-27 approved this line; until it is localized the box simply appears.
        if (Content.Find(BoxInHandKey) != null) Say(BoxInHandKey, 1f);
        Enqueue("phase", number: (int)Phase.Lab);
    }

    const string BoxInHandKey = "memory.lab.bed.second.box";

    void StartReunion()
    {
        if (!Once("reunion_started")) return;
        State.phase = (int)Phase.Reunion;
        Effect("reconnect", seconds: 2.2f);
        for (int i = 1; i <= 10; i++) Enqueue("live", "live.return." + i.ToString("00"));
        WaitLive();
        Enqueue("flag", "live_returned"); Enqueue("flag", "box_open");
        // The box opens: the hairclip and the envelope hop out beside it (pancake 10-26).
        Effect("box_pop", seconds: .7f);
        Enqueue("phase", number: (int)Phase.Lab);
    }

    void StartFinalLetter()
    {
        if (!Once("letter_started")) return;
        State.phase = (int)Phase.Letter;
        Say("interaction.lab.envelope"); Enqueue("live", "live.letter.silly"); WaitLive();
        Say("interaction.lab.envelope.small_print", 2f); Enqueue("letter");
        Enqueue("delay", delay: 3f); Enqueue("phase", number: (int)Phase.Complete);
    }

    void HandleChoice(string key, string option)
    {
        if (key.EndsWith("live.store.knows_question", StringComparison.Ordinal))
        {
            State.knowAnswer = option.EndsWith("knows_yes", StringComparison.Ordinal) ? 1 : 2;
            // Insert the answer immediately after its question, before later comments.
            State.liveQueue.Insert(1, new OSChapter03StoryBeat("live", State.knowAnswer == 1
                ? "live.store.knows_yes" : "live.store.knows_no", delay: 1.8f)
                { interactionSource = interactionSource });
        }
        if (key.Contains("live.offline.") && key.EndsWith("_offer", StringComparison.Ordinal))
        { State.Add("offline_read_receipt"); readReceiptSeconds = 2f; }
    }

    void AfterLiveCompleted(string key)
    {
        CompleteLivePortraitCue(key);
        if (key.EndsWith("live.store.knows_yes", StringComparison.Ordinal)
            || key.EndsWith("live.store.knows_no", StringComparison.Ordinal)) State.Add("knows_reply_done");
    }
}
