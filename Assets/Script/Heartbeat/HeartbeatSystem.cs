using UnityEngine;
using UnityEngine.InputSystem;
using MrTGame.Core;

namespace MrTGame.Heartbeat
{
    /// <summary>
    /// 心跳节拍系统:监听空格,按当前模式判定 Perfect/Good/Miss,经 GameEvents.Beat 广播。
    /// 判定窗口 = 配置窗口 × 羁绊容错倍率——"被爱得越深,心跳越稳"(设计文档 §3.1)。
    /// </summary>
    public class HeartbeatSystem : MonoBehaviour
    {
        public static HeartbeatSystem Instance { get; private set; }

        [SerializeField] GameConfig config;

        public HeartbeatMode Mode { get; private set; } = HeartbeatMode.Idle;

        InputAction _tapAction;
        float _nextBeatTime;   // 下一个理想拍点(Time.time)

        float BeatInterval => 60f / config.baseBpm;

        float Tolerance
        {
            get
            {
                int bond = GameManager.Instance != null ? GameManager.Instance.Flags.BondLevel : 0;
                float bonus = Systems.BuffSystem.Instance != null ? Systems.BuffSystem.Instance.ToleranceBonus : 0f;
                return config.toleranceByBond[Mathf.Clamp(bond, 0, config.toleranceByBond.Length - 1)] + bonus;
            }
        }

        void Awake()
        {
            if (Instance == null) Instance = this;   // 重复实例由 GameManager 的单例守卫销毁
            _tapAction = new InputAction("HeartTap", binding: "<Keyboard>/space");
            _tapAction.performed += _ => OnTap();
        }

        void OnEnable() => _tapAction.Enable();
        void OnDisable() => _tapAction.Disable();

        public void SetMode(HeartbeatMode mode)
        {
            Mode = mode;
            _nextBeatTime = Time.time + BeatInterval;
        }

        void Update()
        {
            if (Mode == HeartbeatMode.Idle) return;

            // 拍点完全错过(超出 Good 窗口仍未按)→ 记一次 Miss
            if (Time.time > _nextBeatTime + config.goodWindow * Tolerance)
            {
                _nextBeatTime += BeatInterval;
                GameEvents.RaiseBeat(new BeatResult(BeatJudgement.Miss, float.NaN));
            }

            // TODO: RhythmFollow 模式——BeatInterval 换成目标曲线采样(新节拍机制③)
        }

        void OnTap()
        {
            if (Mode == HeartbeatMode.Idle) return;

            float offset = Time.time - _nextBeatTime;
            float abs = Mathf.Abs(offset);

            BeatJudgement judgement =
                abs <= config.perfectWindow * Tolerance ? BeatJudgement.Perfect :
                abs <= config.goodWindow * Tolerance ? BeatJudgement.Good :
                BeatJudgement.Miss;

            if (judgement != BeatJudgement.Miss)
                _nextBeatTime += BeatInterval;   // 命中后推进到下一拍

            GameEvents.RaiseBeat(new BeatResult(judgement, offset));

            // TODO: 新节拍机制①长按(深呼吸/按压)、②双击(激动时刻)——加独立的 InputAction 与判定分支
        }
    }
}
