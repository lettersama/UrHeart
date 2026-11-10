using System.Collections.Generic;
using UnityEngine;

namespace MrTGame.Core
{
    /// <summary>剧情flag、羁绊等级、真相揭示阶段。将来存档序列化的主体(设计文档 §1)。</summary>
    public class NarrativeFlags
    {
        readonly HashSet<string> _flags = new HashSet<string>();

        public RevealStage Reveal { get; private set; } = RevealStage.Concealed;

        /// <summary>0~3,驱动心跳容错(GameConfig.toleranceByBond)与新节拍机制解锁。</summary>
        public int BondLevel { get; private set; }

        public bool Has(string flag) => _flags.Contains(flag);

        public void Set(string flag)
        {
            if (!string.IsNullOrEmpty(flag)) _flags.Add(flag);
        }

        /// <summary>揭示只进不退。</summary>
        public void AdvanceReveal(RevealStage stage)
        {
            if (stage <= Reveal) return;
            Reveal = stage;
            GameEvents.RaiseRevealProgressed(stage);
        }

        public void SetBondLevel(int level)
        {
            level = Mathf.Clamp(level, 0, 3);
            if (level == BondLevel) return;
            BondLevel = level;
            GameEvents.RaiseBondLevelChanged(level);
        }

        // ---- 存档序列化(GameManager 检查点用) ----

        public List<string> ExportFlags() => new List<string>(_flags);

        public void Import(List<string> flags, int bondLevel, RevealStage reveal)
        {
            _flags.Clear();
            if (flags != null) foreach (var f in flags) _flags.Add(f);
            BondLevel = Mathf.Clamp(bondLevel, 0, 3);
            Reveal = reveal;
            GameEvents.RaiseBondLevelChanged(BondLevel);
            GameEvents.RaiseRevealProgressed(Reveal);
        }
    }
}
