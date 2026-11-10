using System;
using UnityEngine;

namespace MrTGame.MiniGames
{
    /// <summary>
    /// OS内嵌小游戏/解谜程序基类(设计文档 §3.3)。
    /// MinigameHost 按 StoryNode.payloadId 匹配 MinigameId 分发;面板显隐由 Host 管理。
    /// 失败代价与重试逻辑写在子类内部,只有"解开"才回调。
    /// </summary>
    public abstract class MinigameBase : MonoBehaviour
    {
        [SerializeField] string minigameId;
        public string MinigameId => minigameId;

        /// <summary>开始运行。解开时回调(Host 收到后关面板并推进剧情)。</summary>
        public abstract void Begin(Action onSolved);
    }
}
