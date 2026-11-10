using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

// Uses the existing checkpoint jump; all preview progress stays in the Play-mode snapshot.
[InitializeOnLoad]
public static class OSEnergyMiniGamePreview
{
    const string Pending = "MrTGame.EnergyPreview.Pending";
    static bool jumping;
    static double deadline;

    static OSEnergyMiniGamePreview()
    {
        if (SessionState.GetInt(Pending, 0) != 0) StartWaiting();
    }

    [MenuItem("Tools/MrTGame/能源小游戏/运行发现流程")]
    public static void Discovery() => Start(1);

    [MenuItem("Tools/MrTGame/能源小游戏/直接试玩对局")]
    public static void Match() => Start(2);

    static void Start(int mode)
    {
        // Entering Play Mode preserves the editor scene snapshot, including unsaved edits.
        // The checkpoint reload happens only inside that runtime copy.
        SessionState.SetInt(Pending, mode);jumping = false;StartWaiting();
        if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
    }

    static void StartWaiting()
    {
        deadline = EditorApplication.timeSinceStartup + 90;
        EditorApplication.update -= Tick;EditorApplication.update += Tick;
    }

    static void Tick()
    {
        int mode = SessionState.GetInt(Pending, 0);
        if (mode == 0) { EditorApplication.update -= Tick;return; }
        if (EditorApplication.timeSinceStartup > deadline)
        { Finish();Debug.LogWarning("能源小游戏预览未能进入，请先打开 GameSetUp 场景。");return; }
        if (!EditorApplication.isPlaying || GameDataManager.Instance == null) return;
        var test = UnityEngine.Object.FindAnyObjectByType<GameTest>();
        if (test == null) return;
        if (!jumping) { jumping = test.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeInteractivePicture);return; }
        if (test.IsCheckpointJumpRunning || OS_Handle.Instance == null) return;
        var data = GameDataManager.Instance;var os = OS_Handle.Instance;
        if (!os.IsOSVisible || os.IsSystemInputLocked) return;
        data.TryInstallToolProgram(OSToolProgramIds.EnergyLoop);
        if (!data.IsEnergyLoopInstalled) { Finish();Debug.LogError("能源回路预览安装失败。");return; }
        MrTGame.Systems.GameResourceSystem.Instance?.ApplySnapshot(80, 65, 10);
        os.MemoryWindowController.GetComponent<OSWindowUnit>().Close();
        if (mode == 2)
        { data.DiscoverPaperPush();data.UnlockPaperPush();data.TryBeginEnergyCharge();os.OpenPaperPushWindow(); }
        else
        {
            var icon = os.transform.Find("DesktopProcessGroup/DesktopProcessUnit_EnergyLoop").GetComponent<DesktopProcessUnit>();
            icon.OnPointerClick(new PointerEventData(EventSystem.current));
        }
        Finish();
    }

    static void Finish()
    { SessionState.EraseInt(Pending);EditorApplication.update -= Tick;jumping = false; }
}
