using UnityEngine;
using UnityEngine.UI;

// A nine-pixel folded corner over the authored seal hierarchy. Whole-pixel
// quads keep the bend deliberately stepped, with no gradient or new red accent.
[DisallowMultipleComponent]
public sealed class OSPaperPushSealFoldGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        int size = Mathf.Min(9, Mathf.FloorToInt(Mathf.Min(rect.width, rect.height)));
        Color32 ink = new Color32(0x87, 0x82, 0x7e, 255);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool removedCorner = x + y >= size;
                Color32 shade = removedCorner || x == 0 || y == 0 || (x + y) % 2 == 0 ? (Color32)Color.black : ink;
                float left = rect.xMax - size + x, bottom = rect.yMax - size + y;
                int start = mesh.currentVertCount;
                mesh.AddVert(new Vector3(left, bottom, 0f), shade, Vector2.zero);
                mesh.AddVert(new Vector3(left, bottom + 1f, 0f), shade, Vector2.zero);
                mesh.AddVert(new Vector3(left + 1f, bottom + 1f, 0f), shade, Vector2.zero);
                mesh.AddVert(new Vector3(left + 1f, bottom, 0f), shade, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 3);
            }
    }
}
