using System.Collections.Generic;
using UnityEngine;

public static class LoadHeartbeatCellularTextureBuilder
{
    static readonly Vector2[] WavePoints =
    {
        new Vector2(0.02f, 0.50f),
        new Vector2(0.12f, 0.50f),
        new Vector2(0.17f, 0.57f),
        new Vector2(0.22f, 0.50f),
        new Vector2(0.34f, 0.50f),
        new Vector2(0.385f, 0.42f),
        new Vector2(0.43f, 0.88f),
        new Vector2(0.475f, 0.13f),
        new Vector2(0.525f, 0.50f),
        new Vector2(0.64f, 0.50f),
        new Vector2(0.72f, 0.64f),
        new Vector2(0.82f, 0.50f),
        new Vector2(0.98f, 0.50f)
    };

    public static Texture2D Create(int width, int height)
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);

        bool[] waveMask = BuildWaveMask(width, height);
        int[] birthGenerations = BuildBirthGenerations(
            waveMask,
            width,
            height,
            out int maximumGeneration);
        Color32[] pixels = BuildTexturePixels(
            waveMask,
            birthGenerations,
            maximumGeneration);

        var texture = new Texture2D(
            width,
            height,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = $"Load Heartbeat Cellular Map {width}x{height}",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static bool[] BuildWaveMask(int width, int height)
    {
        var mask = new bool[width * height];
        var scaledPoints = new Vector2[WavePoints.Length];
        for (int i = 0; i < WavePoints.Length; i++)
        {
            scaledPoints[i] = new Vector2(
                WavePoints[i].x * (width - 1),
                WavePoints[i].y * (height - 1));
        }

        for (int pointIndex = 0;
             pointIndex < scaledPoints.Length - 1;
             pointIndex++)
        {
            Vector2 segmentStart = scaledPoints[pointIndex];
            Vector2 segmentEnd = scaledPoints[pointIndex + 1];
            Vector2 segment = segmentEnd - segmentStart;
            int stepCount = Mathf.Max(
                1,
                Mathf.CeilToInt(Mathf.Max(
                    Mathf.Abs(segment.x),
                    Mathf.Abs(segment.y))));

            for (int step = 0; step <= stepCount; step++)
            {
                Vector2 point = Vector2.Lerp(
                    segmentStart,
                    segmentEnd,
                    step / (float)stepCount);
                int x = Mathf.Clamp(Mathf.RoundToInt(point.x), 0, width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(point.y), 0, height - 1);
                mask[y * width + x] = true;
            }
        }

        return mask;
    }

    static int[] BuildBirthGenerations(
        bool[] mask,
        int width,
        int height,
        out int maximumGeneration)
    {
        var birthGenerations = new int[mask.Length];
        for (int i = 0; i < birthGenerations.Length; i++)
            birthGenerations[i] = -1;

        int minimumWaveX = FindMinimumWaveX(mask, width, height);
        int remainingCells = 0;
        for (int index = 0; index < mask.Length; index++)
        {
            if (mask[index])
                remainingCells++;
        }

        if (minimumWaveX < 0 || remainingCells == 0)
        {
            maximumGeneration = 1;
            return birthGenerations;
        }

        for (int y = 0; y < height; y++)
        {
            int index = y * width + minimumWaveX;
            if (!mask[index])
                continue;

            birthGenerations[index] = 0;
            remainingCells--;
        }

        var birthsThisGeneration = new List<int>();
        int generation = 1;
        int generationLimit = mask.Length * 2;
        while (remainingCells > 0 && generation <= generationLimit)
        {
            birthsThisGeneration.Clear();
            for (int y = 0; y < height; y++)
            {
                for (int x = minimumWaveX; x < width; x++)
                {
                    int index = y * width + x;
                    if (!mask[index] || birthGenerations[index] >= 0)
                        continue;

                    int activeNeighborCount = 0;
                    int earliestNeighborGeneration = int.MaxValue;
                    CountActiveNeighbors(
                        birthGenerations,
                        width,
                        height,
                        x,
                        y,
                        ref activeNeighborCount,
                        ref earliestNeighborGeneration);
                    if (activeNeighborCount == 0)
                        continue;

                    int requiredDelay = 1 + (StableHash(x, y) % 3);
                    if (activeNeighborCount >= 2)
                        requiredDelay = Mathf.Max(1, requiredDelay - 1);

                    if (generation - earliestNeighborGeneration >= requiredDelay)
                        birthsThisGeneration.Add(index);
                }
            }

            for (int i = 0; i < birthsThisGeneration.Count; i++)
            {
                birthGenerations[birthsThisGeneration[i]] = generation;
                remainingCells--;
            }

            generation++;
        }

        if (remainingCells > 0)
        {
            Debug.LogWarning(
                "[LoadPanel] Heartbeat cellular mask contained disconnected cells; "
                + "falling back to ordered activation for those cells.");
            for (int x = minimumWaveX; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int index = y * width + x;
                    if (!mask[index] || birthGenerations[index] >= 0)
                        continue;

                    birthGenerations[index] =
                        generation + (x - minimumWaveX) * 2 + StableHash(x, y) % 2;
                }
            }
        }

        maximumGeneration = 1;
        for (int index = 0; index < birthGenerations.Length; index++)
        {
            maximumGeneration = Mathf.Max(
                maximumGeneration,
                birthGenerations[index]);
        }

        return birthGenerations;
    }

    static Color32[] BuildTexturePixels(
        bool[] mask,
        int[] birthGenerations,
        int maximumGeneration)
    {
        var pixels = new Color32[mask.Length];
        float denominator = Mathf.Max(1f, maximumGeneration + 1f);
        for (int index = 0; index < pixels.Length; index++)
        {
            if (!mask[index])
            {
                pixels[index] = new Color32(0, 0, 0, 0);
                continue;
            }

            float normalizedBirth =
                (Mathf.Max(0, birthGenerations[index]) + 1f) / denominator;
            byte birthThreshold = (byte)Mathf.Clamp(
                Mathf.CeilToInt(normalizedBirth * 255f),
                1,
                255);
            pixels[index] = new Color32(255, birthThreshold, 0, 255);
        }

        return pixels;
    }

    static int FindMinimumWaveX(bool[] mask, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (mask[y * width + x])
                    return x;
            }
        }

        return -1;
    }

    static void CountActiveNeighbors(
        int[] birthGenerations,
        int width,
        int height,
        int cellX,
        int cellY,
        ref int activeNeighborCount,
        ref int earliestNeighborGeneration)
    {
        for (int yOffset = -1; yOffset <= 1; yOffset++)
        {
            int neighborY = cellY + yOffset;
            if (neighborY < 0 || neighborY >= height)
                continue;

            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                if (xOffset == 0 && yOffset == 0)
                    continue;

                int neighborX = cellX + xOffset;
                if (neighborX < 0 || neighborX >= width)
                    continue;

                int neighborGeneration =
                    birthGenerations[neighborY * width + neighborX];
                if (neighborGeneration < 0)
                    continue;

                activeNeighborCount++;
                earliestNeighborGeneration = Mathf.Min(
                    earliestNeighborGeneration,
                    neighborGeneration);
            }
        }
    }

    static int StableHash(int x, int y)
    {
        unchecked
        {
            uint value = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            value ^= value >> 13;
            value *= 1274126177u;
            return (int)(value & 0x7fffffff);
        }
    }

}
