using UnityEngine;

namespace MrTGame.Visual
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PixelFogLayer : MonoBehaviour
    {
        Camera targetCamera;
        [SerializeField] bool followCamera = true;
        [SerializeField] bool fitCamera = true;
        [SerializeField, Min(1f)] float overscan = 1.15f;
        [SerializeField] Vector2 cameraOffset;
        [SerializeField, Min(0.001f)] float worldUnitsPerPixel = 0.05f;

        SpriteRenderer spriteRenderer;

        void OnEnable()
        {
            RefreshTransform();
        }

        void OnValidate()
        {
            RefreshTransform();
        }

        void LateUpdate()
        {
            RefreshTransform();
        }

        void RefreshTransform()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetCamera == null || !targetCamera.orthographic)
                return;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            if (spriteRenderer.sprite == null)
                return;

            if (followCamera)
            {
                Vector3 cameraPosition = targetCamera.transform.position;
                Vector3 position = transform.position;
                position.x = Snap(cameraPosition.x + cameraOffset.x);
                position.y = Snap(cameraPosition.y + cameraOffset.y);
                transform.position = position;
            }

            if (!fitCamera)
                return;

            Vector2 spriteSize = spriteRenderer.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
                return;

            float height = targetCamera.orthographicSize * 2f * overscan;
            float width = height * targetCamera.aspect;
            transform.localScale = new Vector3(width / spriteSize.x, height / spriteSize.y, 1f);
        }

        float Snap(float value)
        {
            return Mathf.Round(value / worldUnitsPerPixel) * worldUnitsPerPixel;
        }
    }
}
