#ifndef CHAPTER03_BAYER_INCLUDED
#define CHAPTER03_BAYER_INCLUDED

// OSDitherFill anchors its texture at the root canvas's bottom-left corner.
// D3D/Metal fragment coordinates start at the top: flip only those platforms,
// then use the same 360-line logical pixel grid and 4x4 Bayer order.
float Chapter03BayerThreshold(float2 screenPixel)
{
    #if UNITY_UV_STARTS_AT_TOP
        screenPixel.y = _ScreenParams.y - screenPixel.y;
    #endif
    float cell = max(1.0, _ScreenParams.y / 360.0);
    float2 pixel = fmod(floor(screenPixel / cell), 4.0);
    float4 row = pixel.y < 0.5 ? float4(0, 8, 2, 10)
        : pixel.y < 1.5 ? float4(12, 4, 14, 6)
        : pixel.y < 2.5 ? float4(3, 11, 1, 9) : float4(15, 7, 13, 5);
    float rank = pixel.x < 0.5 ? row.x : pixel.x < 1.5 ? row.y : pixel.x < 2.5 ? row.z : row.w;
    return (rank + 0.5) / 16.0;
}

#endif
