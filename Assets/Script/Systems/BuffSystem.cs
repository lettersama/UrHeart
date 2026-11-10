using System;
using System.Collections.Generic;
using UnityEngine;

namespace MrTGame.Systems
{
    /// <summary>
    /// buff/debuff 登记与聚合查询(设计文档 §3.5)。
    /// 提供方:解锁的程序(buff)、剧情/排异(debuff)。
    /// 消费方:HeartbeatSystem(容错加成)、GameResourceSystem(Miss惩罚倍率)、WaveformUI(有debuff→波形复杂化)。
    /// </summary>
    public class BuffSystem : MonoBehaviour
    {
        public static BuffSystem Instance { get; private set; }

        readonly List<BuffDef> _active = new List<BuffDef>();

        /// <summary>激活列表变化时广播(WaveformUI/状态栏订阅刷新)。</summary>
        public event Action BuffsChanged;

        public bool HasDebuff
        {
            get
            {
                foreach (var b in _active) if (b.isDebuff) return true;
                return false;
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Add(BuffDef buff)
        {
            if (buff == null || _active.Contains(buff)) return;
            _active.Add(buff);
            BuffsChanged?.Invoke();
        }

        public void Remove(BuffDef buff)
        {
            if (_active.Remove(buff)) BuffsChanged?.Invoke();
        }

        // ---- 聚合查询(消费方调用)----

        public float HeartLossPerMissMult
        {
            get { float m = 1f; foreach (var b in _active) m *= b.heartLossPerMissMult; return m; }
        }

        public float BatteryDrainMult
        {
            get { float m = 1f; foreach (var b in _active) m *= b.batteryDrainMult; return m; }
        }

        public float ToleranceBonus
        {
            get { float t = 0f; foreach (var b in _active) t += b.toleranceBonus; return t; }
        }

        /// <summary>剧情每推进一节调用一次:结算回复类buff的持续效果(StoryRunner.Advance 后接入)。</summary>
        public void TickAdvance(GameResourceSystem resources)
        {
            if (resources == null) return;
            foreach (var b in _active)
                if (!Mathf.Approximately(b.regenPerAdvance, 0f))
                    resources.Modify(b.regenType, b.regenPerAdvance);
        }
    }
}
