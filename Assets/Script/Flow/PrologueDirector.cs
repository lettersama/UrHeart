using UnityEngine;
using UnityEngine.UI;
using MrTGame.Core;
using MrTGame.MiniGames;

namespace MrTGame.Flow
{
    /// <summary>横板序章导演:响应报纸/老人/墓碑触发事件,驱动灰盒演出与 CPR 教程。</summary>
    public class PrologueDirector : MonoBehaviour
    {
        [SerializeField] Text infoText;
        [SerializeField] GameObject cprPanel;
        [SerializeField] Text cprStatusText;
        [SerializeField] CprMinigame cpr;

        bool _elderDone;
        float _infoUntil;

        void OnEnable() => GameEvents.StoryEventTriggered += OnStoryEvent;
        void OnDisable() => GameEvents.StoryEventTriggered -= OnStoryEvent;

        void Start()
        {
            if (cprPanel != null) cprPanel.SetActive(false);
            ShowInfo("灰盒序章 — A/D 移动,E 交互。往右走。", 6f);
        }

        void OnStoryEvent(string id)
        {
            switch (id)
            {
                case "prologue.newspaper":
                    ShowInfo("报纸:『大战后的病毒后遗症仍在持续。多数人的心脏,需要定期稳定。』", 6f);
                    break;

                case "prologue.elder":
                    if (_elderDone) { ShowInfo("老人已经平稳了,朝你摆了摆手。", 3f); break; }
                    StartCpr();
                    break;

                case "prologue.grave":
                    ShowInfo("……心口猛地绞痛。", 2f);
                    if (GameManager.Instance != null) GameManager.Instance.SwitchPhase(GamePhase.OSMain);
                    else Debug.LogWarning("[Prologue] GameManager 缺失,请从 GameSetUp 场景启动");
                    break;
            }
        }

        void StartCpr()
        {
            if (cpr == null) return;
            ShowInfo("老人捂住胸口倒下了——随身面板弹出:跟着节奏按【空格】稳定心跳!", 8f);
            if (cprPanel != null) cprPanel.SetActive(true);
            cpr.Run(success =>
            {
                if (cprPanel != null) cprPanel.SetActive(false);
                _elderDone = success;
                ShowInfo(success ? "心跳稳定下来了。" : "节奏乱了……缓一缓,按 E 再试一次。", 5f);
            });
        }

        void ShowInfo(string message, float seconds)
        {
            if (infoText == null) return;
            infoText.text = message;
            _infoUntil = Time.time + seconds;
        }

        void Update()
        {
            if (infoText != null && infoText.text.Length > 0 && Time.time > _infoUntil)
                infoText.text = "";

            if (cpr != null && cpr.IsRunning && cprStatusText != null)
                cprStatusText.text = $"跟着心跳节奏按【空格】\n命中 {cpr.Hits}/{cpr.RequiredHits}    失误 {cpr.Misses}/{cpr.AllowedMisses}";
        }
    }
}
