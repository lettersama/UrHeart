using UnityEngine;
using UnityEngine.Events;
using MrTGame.Core;

namespace MrTGame.SideScroll
{
    /// <summary>
    /// 通用剧情触发器:报纸、老人、墓碑都用它——配一个事件id,流程侧订阅
    /// GameEvents.StoryEventTriggered 决定发生什么(弹报纸UI / 进CPR教程 / 切入OS界面)。
    /// </summary>
    public class StoryTriggerInteractable : Interactable
    {
        [Tooltip("剧情事件id,如 prologue.newspaper / prologue.elder / prologue.grave")]
        [SerializeField] string storyEventId;
        [SerializeField] bool oneShot = true;
        [SerializeField] UnityEvent onInteracted;   // 就地演出(高亮、音效)挂这里

        bool _consumed;

        public override void Interact(PlayerController2D player)
        {
            if (_consumed) return;
            if (oneShot) _consumed = true;

            GameEvents.RaiseStoryEvent(storyEventId);
            onInteracted?.Invoke();
        }
    }
}
