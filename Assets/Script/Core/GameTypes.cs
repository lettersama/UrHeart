namespace MrTGame.Core
{
    /// <summary>
    /// 游戏全局阶段(设计文档 §2.1 三段式)。
    /// 横板只在首尾出现,中盘全程停留在 OSMain 的操作界面里,不切出。
    /// </summary>
    public enum GamePhase
    {
        Boot,       // 启动/标题
        Prologue,   // 序章:横板现在时
        OSMain,     // 主体:单一OS操作界面(回忆系统主线+内嵌小游戏程序)
        Interlude,  // [可选·未定] 中途缓解场景,默认不用
        Ending      // 终章:横板墓前放花
    }

    /// <summary>三资源(设计文档 §3.2,2026-07-26 已定)。</summary>
    public enum ResourceType
    {
        Heart,      // 心(正向,归零失败)
        Battery,    // 电量(正向,归零失败;揭示A之前在UI上伪装成"面板电量")
        Fault       // 故障(反向,0起步;80进入高故障,100时game over)
    }

    /// <summary>资源失败方向。</summary>
    public enum ResourceFailMode
    {
        DepleteAtZero,       // 正向:归零 → game over(心、电量)
        OverflowAtThreshold  // 反向:涨满 → game over(故障:100)
    }

    /// <summary>真相揭示阶段(设计文档 §1 两层真相)。</summary>
    public enum RevealStage
    {
        Concealed,      // 序章~M2:玩家被误导"主角是人类"
        CracksShowing,  // 裂缝暗示期
        IdentityKnown,  // Layer A:玩家确认主角是机器人(M3 排异)
        HeartKnown      // Layer B:心脏来源揭示(M5~终章)
    }
}
