using UnityEngine;

namespace MrTGame.SideScroll
{
    /// <summary>横板场景可交互物基类。需要挂 Collider2D(可设 isTrigger)。</summary>
    public abstract class Interactable : MonoBehaviour
    {
        [Tooltip("交互提示文案,如「查看」「施救」「献花」")]
        public string prompt = "交互";

        public abstract void Interact(PlayerController2D player);
    }
}
