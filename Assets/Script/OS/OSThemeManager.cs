using System;
using UnityEngine;

namespace MrTGame.OS
{
    /// <summary>主题分发:全界面元素从这里取色。换主题 = SetTheme(新资产),所有 ThemedGraphic 自动刷新。</summary>
    public class OSThemeManager : Singleton<OSThemeManager>
    {
        [SerializeField] OSTheme theme;

        public OSTheme Theme => theme;

        public event Action<OSTheme> ThemeChanged;

        protected override void Awake()
        {
            base.Awake();
        }

        public void SetTheme(OSTheme newTheme)
        {
            theme = newTheme;
            ThemeChanged?.Invoke(newTheme);
        }
    }
}
