using System;
using System.Collections.Generic;
using UnityEngine;
using MrTGame.Core;

namespace MrTGame.Narrative
{
    public enum StoryNodeType
    {
        Text,         // 正文,节拍/点击确认后走 nextNodeId
        Choice,       // 选项分支(资源增减挂在选项上)
        Illustration, // 插画展示节点
        Minigame,     // 内嵌小游戏/转阶段程序(OS里"另一个程序"),payloadId 索引具体内容
        BeatSequence, // 节拍段落(排异QTE等),payloadId 索引配置
        ChapterEnd    // 本章结束——章节转阶段用OS内演出表现(重启/弹窗/文件解锁),不切出界面
    }

    [Serializable]
    public class ResourceDelta
    {
        public ResourceType type;
        public float amount;   // 正=恢复 负=消耗
    }

    [Serializable]
    public class StoryChoice
    {
        [TextArea] public string text;
        public string nextNodeId;
        public List<ResourceDelta> effects = new List<ResourceDelta>();
        public string setFlag;   // 可空
    }

    /// <summary>剧情节点。一章 = 节点列表,靠 id 串联成图,内容全在 Inspector 里填。</summary>
    [Serializable]
    public class StoryNode
    {
        public string id;
        public StoryNodeType type = StoryNodeType.Text;

        [TextArea(3, 10)] public string body;                                  // 正文
        public Sprite illustration;                                            // 可空:附带插画
        public string nextNodeId;                                              // Text/Illustration 的默认去向
        public List<StoryChoice> choices = new List<StoryChoice>();            // Choice 节点用
        public List<ResourceDelta> effects = new List<ResourceDelta>();        // 进入节点时结算
        public string setFlag;                                                 // 可空:进入节点时设置
        public string unlockProgramId;                                         // 可空:解锁/"传输"程序到程序坞(设计文档 §3.5)
        public string payloadId;                                               // Minigame/BeatSequence 的内容索引
    }
}
