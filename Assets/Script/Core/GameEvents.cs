using System;
using MrTGame.Heartbeat;

namespace MrTGame.Core
{
    /// <summary>
    /// 全局事件总线。各系统只依赖这里互相通信,不直接引用。
    /// 订阅方记得在 OnDisable/OnDestroy 里退订。
    /// </summary>
    public static class GameEvents
    {
        // ---- 流程 ----
        public static event Action<GamePhase> PhaseChanged;
        

        // ---- Language ----
        public static event Action<LanguageState> LanguageChanged;
        public static void RaiseLanguageChanged(LanguageState language) => LanguageChanged?.Invoke(language);
public static void RaisePhaseChanged(GamePhase phase) => PhaseChanged?.Invoke(phase);

        // ---- 资源 (类型, 当前值, 最大值) ----
        public static event Action<ResourceType, float, float> ResourceChanged;
        public static void RaiseResourceChanged(ResourceType type, float value, float max) => ResourceChanged?.Invoke(type, value, max);

        // A paid action can continue at zero energy, including a clamped 0 -> 0.
        // Keep this separate from value changes so passive refresh/load is silent.
        public static event Action EmptyEnergyUsed;
        public static void RaiseEmptyEnergyUsed() => EmptyEnergyUsed?.Invoke();

        /// <summary>资源到达失败线(正向归零 / 反向涨满)。</summary>
        public static event Action<ResourceType> ResourceFailed;
        public static void RaiseResourceFailed(ResourceType type) => ResourceFailed?.Invoke(type);

        public static event Action GameOver;
        public static void RaiseGameOver() => GameOver?.Invoke();

        // ---- 心跳节拍 ----
        public static event Action<BeatResult> Beat;
        public static void RaiseBeat(BeatResult result) => Beat?.Invoke(result);

        // ---- 叙事 ----
        public static event Action<RevealStage> RevealProgressed;
        public static void RaiseRevealProgressed(RevealStage stage) => RevealProgressed?.Invoke(stage);

        public static event Action<int> BondLevelChanged;
        public static void RaiseBondLevelChanged(int level) => BondLevelChanged?.Invoke(level);

        /// <summary>横板交互物(报纸/老人/墓碑)→ 流程侧。事件id 如 "prologue.grave"。</summary>
        public static event Action<string> StoryEventTriggered;
        public static void RaiseStoryEvent(string eventId) => StoryEventTriggered?.Invoke(eventId);

        /// <summary>剧情请求解锁/"传输"一个程序到程序坞(设计文档 §3.5)。</summary>
        public static event Action<string> ProgramUnlockRequested;
        public static void RaiseProgramUnlockRequested(string programId) => ProgramUnlockRequested?.Invoke(programId);
    }
}
