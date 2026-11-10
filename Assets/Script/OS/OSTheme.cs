using UnityEngine;

namespace MrTGame.OS
{
    /// <summary>
    /// OS界面主题(换色系):AI打样的暗绿只是占位,正式风格未定,整体做成可自定义。
    /// 颜色按"角色"命名而非写死色值——出正式风格时新建一份资产换上即可,不动prefab。
    /// 也支持剧情驱动换主题(如揭示B时切一份"心色扩张"的主题)。
    /// </summary>
    [CreateAssetMenu(menuName = "MrTGame/OSTheme", fileName = "Theme_Default")]
    public class OSTheme : ScriptableObject
    {
        [Header("基础")]
        public Color background = Color.black;
        public Color panel;         // 面板/窗口底
        public Color line;          // 边框/分割线
        public Color textPrimary;
        public Color textDim;

        [Header("强调")]
        public Color accent;        // 常规高亮(选中框、按钮)
        public Color heart;         // 心脏专用色:波形、心资源、回忆系统入口——与系统基色分离(设计文档 §4)
        public Color warning;       // 低资源/错误提示

        [Header("特效")]
        public Color glitch;        // 排异/故障色
        [Range(0f, 1f)] public float scanlineStrength = 0.3f;
    }
}
