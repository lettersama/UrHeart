using UnityEngine;
using MrTGame.Core;

namespace MrTGame.Systems
{
    /// <summary>
    /// 一条 buff/debuff 定义(设计文档 §3.5)。buff 主要由解锁的程序提供;debuff 由剧情/排异施加。
    /// 想加新修正项:这里加字段,BuffSystem 的聚合查询同步加一行。
    /// </summary>
    [CreateAssetMenu(menuName = "MrTGame/BuffDef", fileName = "Buff_New")]
    public class BuffDef : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public bool isDebuff;

        [Header("数值修正(乘区默认1,加区默认0)")]
        [Tooltip("节拍Miss扣心倍率(0.5=惩罚减半;debuff可>1)")]
        public float heartLossPerMissMult = 1f;

        [Tooltip("电量消耗倍率")]
        public float batteryDrainMult = 1f;

        [Tooltip("判定容错加成(加在羁绊容错倍率上)")]
        public float toleranceBonus = 0f;

        [Header("持续回复(回复类程序的挂机效果)")]
        [Tooltip("每次剧情推进结算一次的资源变化量(0=无;故障填负数=修复)")]
        public float regenPerAdvance = 0f;
        public ResourceType regenType = ResourceType.Heart;
    }
}
