using System.Collections.Generic;
using UnityEngine;

namespace MrTGame.Narrative
{
    /// <summary>
    /// 一章回忆(设计文档 §2.2:P0、M1~M5、E 各建一份资产)。
    /// 内容填充全部在 Inspector / 将来可换成表格导入,不写码。
    /// </summary>
    [CreateAssetMenu(menuName = "MrTGame/StoryChapter", fileName = "M0_NewChapter")]
    public class StoryChapter : ScriptableObject
    {
        public string chapterId;    // 如 "M1"
        public string title;        // 如 "拾获"
        public string startNodeId;
        public List<StoryNode> nodes = new List<StoryNode>();

        public StoryNode Find(string id) => nodes.Find(n => n.id == id);
    }
}
