using System.Collections.Generic;
using UnityEngine;
using MrTGame.Core;
using MrTGame.Heartbeat;

namespace MrTGame.Systems
{
    /// <summary>
    /// 三资源初始值:心53 / 电量70 / 故障10; 心归零或故障涨满触发失败接口。
    /// 电量归零仍可推进，耗电操作通过 EmptyEnergyUsed 提醒充电。
    /// 任一失败 → GameOver 预留接口。常驻于 MrT_Systems,跨场景存活。
    /// </summary>
    public class GameResourceSystem : MonoBehaviour
    {
        public static GameResourceSystem Instance { get; private set; }

        [SerializeField] GameConfig config;

        readonly Dictionary<ResourceType, int> _fallbackValues =
            new Dictionary<ResourceType, int>();
        readonly Dictionary<ResourceType, float> _max = new Dictionary<ResourceType, float>();
        readonly Dictionary<ResourceType, ResourceFailMode> _failMode = new Dictionary<ResourceType, ResourceFailMode>();
        readonly Dictionary<ResourceType, float> _failThreshold = new Dictionary<ResourceType, float>();
        bool _gameOverRaised;

        void Awake()
        {
            if (Instance == null) Instance = this;   // 重复实例由 GameManager 的单例守卫销毁

            _max[ResourceType.Heart] = OSResourceSaveData.MaxValue;
            _max[ResourceType.Battery] = OSResourceSaveData.MaxValue;
            _max[ResourceType.Fault] = OSResourceSaveData.MaxValue;

            _failMode[ResourceType.Heart] = ResourceFailMode.DepleteAtZero;
            _failMode[ResourceType.Battery] = ResourceFailMode.DepleteAtZero;
            _failMode[ResourceType.Fault] = ResourceFailMode.OverflowAtThreshold;

            _failThreshold[ResourceType.Heart] = 0f;
            _failThreshold[ResourceType.Battery] = 0f;
            _failThreshold[ResourceType.Fault] =
                OSResourceSaveData.MaxValue;

            _fallbackValues[ResourceType.Heart] = OSResourceSaveData.InitialHeart;
            _fallbackValues[ResourceType.Battery] = OSResourceSaveData.InitialEnergy;
            _fallbackValues[ResourceType.Fault] = OSResourceSaveData.InitialFault;
        }

        void OnEnable() => GameEvents.Beat += OnBeat;
        void OnDisable() => GameEvents.Beat -= OnBeat;

        void OnBeat(BeatResult result)
        {
            if (result.Judgement != BeatJudgement.Miss) return;
            if (GameSessionFlow.Instance?.IsBusy == true) return;
            if (OS_Handle.Instance?.Chapter01Ending?.IsPresenting == true) return;
            // 设计文档 P0:序章/横板不进资源体系——Miss 惩罚仅 OS 主体阶段生效
            if (GameManager.Instance != null && GameManager.Instance.Phase != GamePhase.OSMain) return;
            float mult = BuffSystem.Instance != null ? BuffSystem.Instance.HeartLossPerMissMult : 1f;
            float loss = config != null ? config.heartLossPerMiss : 0f;
            Modify(ResourceType.Heart, -loss * mult);
        }

        public float Get(ResourceType type)
        {
            GameDataManager dataManager = GameDataManager.Instance;
            return dataManager != null
                ? dataManager.GetOSResourceValue(type)
                : _fallbackValues[type];
        }

        public float GetMax(ResourceType type) => _max[type];
        public float GetFailThreshold(ResourceType type) => _failThreshold[type];

        /// <summary>该资源当前是否付得起 delta(选项置灰/隐藏判断用)。</summary>
        public bool CanAfford(ResourceType type, float delta)
        {
            float value = Mathf.Clamp(
                Get(type) + Mathf.RoundToInt(delta),
                OSResourceSaveData.MinValue,
                OSResourceSaveData.MaxValue);
            return _failMode[type] == ResourceFailMode.DepleteAtZero
                ? value > 0f
                : value < _failThreshold[type];
        }

        public void Modify(ResourceType type, float delta)
        {
            int roundedDelta = Mathf.RoundToInt(delta);
            if (roundedDelta == 0)
                return;

            int previousValue = Mathf.RoundToInt(Get(type));
            int targetValue = Mathf.Clamp(
                previousValue + roundedDelta,
                OSResourceSaveData.MinValue,
                OSResourceSaveData.MaxValue);
            if (targetValue != previousValue)
            {
                int value = SetStoredValue(type, targetValue);
                PublishValue(type, previousValue, value, true);
            }
            if (type == ResourceType.Battery && roundedDelta < 0 && targetValue == 0)
                GameEvents.RaiseEmptyEnergyUsed();
        }

        public void NotifySavedResourceChanged(ResourceType type, int value)
        {
            NotifySavedResourceChanged(type, _fallbackValues[type], value);
        }

        public void NotifySavedResourceChanged(
            ResourceType type,
            int previousValue,
            int value)
        {
            int clampedValue = Mathf.Clamp(
                value,
                OSResourceSaveData.MinValue,
                OSResourceSaveData.MaxValue);
            PublishValue(type, previousValue, clampedValue, true);
        }

        int SetStoredValue(ResourceType type, int value)
        {
            int clampedValue = Mathf.Clamp(
                value,
                OSResourceSaveData.MinValue,
                OSResourceSaveData.MaxValue);
            _fallbackValues[type] = clampedValue;

            GameDataManager dataManager = GameDataManager.Instance;
            return dataManager != null
                ? dataManager.SetOSResourceValue(type, clampedValue)
                : clampedValue;
        }

        void PublishValue(
            ResourceType type,
            int previousValue,
            int value,
            bool evaluateFailure)
        {
            if (value == previousValue)
                return;

            _fallbackValues[type] = value;
            GameEvents.RaiseResourceChanged(type, value, _max[type]);

            if (!evaluateFailure || type == ResourceType.Battery)
                return;

            bool failed = _failMode[type] == ResourceFailMode.DepleteAtZero
                ? value <= 0f
                : value >= _failThreshold[type];

            if (failed)
            {
                GameEvents.RaiseResourceFailed(type);
                if (_gameOverRaised)
                    return;

                _gameOverRaised = true;
                GameEvents.RaiseGameOver();
            }
        }

        /// <summary>把当前值重新广播一遍——新场景的UI在 Start 里调用以初始化显示。</summary>
        public void Broadcast()
        {
            foreach (var type in _max.Keys)
            {
                int value = Mathf.RoundToInt(Get(type));
                _fallbackValues[type] = value;
                GameEvents.RaiseResourceChanged(type, value, _max[type]);
            }
        }

        public void ResetAll()
        {
            var initial = new OSResourceSaveData();
            foreach (var type in _max.Keys)
            {
                SetStoredValue(type, initial.Get(type));
            }

            _gameOverRaised = false;
            Broadcast();
        }

        // ---- 存档快照(检查点用):直接写值+广播,不触发失败判定 ----

        public void ApplySnapshot(float heart, float battery, float fault)
        {
            SetStoredValue(ResourceType.Heart, Mathf.RoundToInt(heart));
            SetStoredValue(ResourceType.Battery, Mathf.RoundToInt(battery));
            SetStoredValue(ResourceType.Fault, Mathf.RoundToInt(fault));
            _gameOverRaised = HasFailedResource();
            Broadcast();
        }

        bool HasFailedResource()
        {
            foreach (var type in _max.Keys)
            {
                if (type == ResourceType.Battery) continue;
                float value = Get(type);
                bool failed = _failMode[type] == ResourceFailMode.DepleteAtZero
                    ? value <= 0f
                    : value >= _failThreshold[type];
                if (failed)
                    return true;
            }

            return false;
        }
    }
}
