using UnityEngine;

namespace MrTGame.Core
{
    /// <summary>全局数值配置。Project 里创建一份资产(右键 → MrTGame → GameConfig),挂到 GameManager。</summary>
    [CreateAssetMenu(menuName = "MrTGame/GameConfig", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("心跳节拍(设计文档 §3.1)")]
        [Tooltip("基准BPM(回忆主体的默认心率)")]
        public float baseBpm = 72f;

        [Tooltip("Perfect 判定半窗口(秒)")]
        public float perfectWindow = 0.08f;

        [Tooltip("Good 判定半窗口(秒)")]
        public float goodWindow = 0.18f;

        [Tooltip("容错倍率,按羁绊等级 Lv0~Lv3 —— 被爱得越深,心跳越稳")]
        public float[] toleranceByBond = { 1f, 1.15f, 1.3f, 1.5f };

        [Header("资源(设计文档 §3.2)")]
        public float heartMax = 100f;
        public float batteryMax = 100f;
        [Tooltip("故障(反向资源)显示上限")]
        public float faultMax = 100f;

        [Tooltip("故障达到此值进入高故障警戒状态；涨满100才失败")]
        public float faultFailThreshold = 80f;

        [Tooltip("节拍 Miss 一次扣多少心")]
        public float heartLossPerMiss = 2f;
    }
}
