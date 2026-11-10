using System;
using UnityEngine;
using MrTGame.Core;
using MrTGame.Heartbeat;

namespace MrTGame.MiniGames
{
    /// <summary>
    /// 序章CPR节拍教程(设计文档 P0-2):连续命中 N 拍稳住老人心跳。
    /// 也是排异QTE的基础形态(排异时收紧 allowedMisses、配合 TriggerGlitch)。
    /// </summary>
    public class CprMinigame : MonoBehaviour
    {
        [SerializeField] HeartbeatSystem heartbeat;   // 可空:回退到常驻单例
        [SerializeField] int requiredHits = 8;
        [SerializeField] int allowedMisses = 3;       // 教程期容错从宽

        public bool IsRunning { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public int RequiredHits => requiredHits;
        public int AllowedMisses => allowedMisses;

        /// <summary>运行前改判定参数(排异QTE复用:收紧容错)。</summary>
        public void Configure(int required, int allowed)
        {
            requiredHits = Mathf.Max(1, required);
            allowedMisses = Mathf.Max(0, allowed);
        }

        HeartbeatSystem Heart => heartbeat != null ? heartbeat : HeartbeatSystem.Instance;
        Action<bool> _onFinished;

        public void Run(Action<bool> onFinished)
        {
            var hb = Heart;
            if (hb == null)
            {
                Debug.LogWarning("[CPR] 找不到 HeartbeatSystem(请从 GameSetUp 场景启动)");
                onFinished?.Invoke(false);
                return;
            }

            Hits = 0;
            Misses = 0;
            IsRunning = true;
            _onFinished = onFinished;
            GameEvents.Beat += OnBeat;
            hb.SetMode(HeartbeatMode.Qte);
        }

        void OnBeat(BeatResult result)
        {
            if (result.Judgement == BeatJudgement.Miss) Misses++;
            else Hits++;

            if (Hits >= requiredHits) Finish(true);
            else if (Misses > allowedMisses) Finish(false);
        }

        void Finish(bool success)
        {
            IsRunning = false;
            GameEvents.Beat -= OnBeat;
            var hb = Heart;
            if (hb != null) hb.SetMode(HeartbeatMode.Idle);
            // 教程失败不 game over,直接重试(序章还没进资源体系)
            _onFinished?.Invoke(success);
        }

        void OnDestroy() => GameEvents.Beat -= OnBeat;
    }
}
