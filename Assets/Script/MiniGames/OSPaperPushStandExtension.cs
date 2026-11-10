using UnityEngine;
using UnityEngine.UI;

// Extend the common three-pixel stem without stretching the character artwork.
// All 14 authored 70px poses have their stem at x=33..35, ending at source y=67.
[DisallowMultipleComponent]
public sealed class OSPaperPushStandExtension : BaseMeshEffect
{
    public const int ExtraPixels = 10;
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || mesh.currentVertCount != 4) return;
        UIVertex left = default, right = default, vertex = default;
        mesh.PopulateUIVertex(ref left, 0);mesh.PopulateUIVertex(ref right, 3);
        for (int i = 0; i < 4; i++)
        { mesh.PopulateUIVertex(ref vertex, i);vertex.position.y += ExtraPixels;mesh.SetUIVertex(vertex, i); }
        // Repeat the existing stem's last row into a strip under the raised image.
        float texel = 1f / graphic.mainTexture.height;
        left.uv0.y += 2.5f * texel;right.uv0.y = left.uv0.y;
        left.position.y += 2f;right.position.y = left.position.y;
        int start = mesh.currentVertCount;
        mesh.AddVert(left);left.position.y += ExtraPixels;mesh.AddVert(left);
        right.position.y += ExtraPixels;mesh.AddVert(right);
        right.position.y -= ExtraPixels;mesh.AddVert(right);
        mesh.AddTriangle(start, start + 1, start + 2);mesh.AddTriangle(start, start + 2, start + 3);
    }
}
