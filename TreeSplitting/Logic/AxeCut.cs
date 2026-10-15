using System;
using TreeSplitting.BlockEntities;
using TreeSplitting.Config;
using TreeSplitting.Enums;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Logic;

public enum EnumChopResult : byte
{
    None,
    // A whole plane came off
    Split,
    // Bit into the plane but didn't get through yet
    Bite,
    // Heavy hit
    Heavy
}

/// <summary>
/// Cuts what an axe hit takes off the voxels. The server cuts the real log, the client runs it on a copy to know
/// which sound to play straight away.
///
/// A normal hit takes a few hits to get through a plane: vertical planes split from the top down like an axe driving
/// in, horizontal layers get notched from the side the player stands on. A heavy hit takes three planes in one go.
/// </summary>
public static class AxeCut
{
    private const int Size = 16;

    public static EnumChopResult Cut(byte[,,] voxels, Vec3i hit, double playerDx, double playerDz,
        EnumAxeToolModes mode, TreeSplittingConfig config)
    {
        bool sideways = mode == EnumAxeToolModes.ChopHorizontal;

        // Vertical picks the plane by which side the player is on, horizontal takes the layer
        int dominant = Math.Abs(playerDx) > Math.Abs(playerDz) ? 0 : 2;
        int normal = sideways ? 1 : (dominant == 0 ? 2 : 0);
        int[] hitArr = [hit.X, hit.Y, hit.Z];
        int plane = hitArr[normal];

        if (mode == EnumAxeToolModes.HeavyHit)
        {
            // Takes the struck plane plus one on each side in one go, anything past the edge of the log is just skipped
            bool removed = false;
            for (int c = Math.Max(0, plane - 1); c <= Math.Min(Size - 1, plane + 1); c++) removed |= ClearPlane(voxels, normal, c);
            return removed ? EnumChopResult.Heavy : EnumChopResult.None;
        }

        int hits = Math.Max(1, config.HitsPerPlane);
        if (hits == 1) return ClearPlane(voxels, normal, plane) ? EnumChopResult.Split : EnumChopResult.None;

        int biteAxis = sideways ? dominant : 1;
        bool fromHigh = !sideways || (dominant == 0 ? playerDx : playerDz) > 0;
        return Bite(voxels, normal, plane, biteAxis, fromHigh, (int)Math.Ceiling(Size / (double)hits));
    }

    /// <summary>
    /// Clears a whole plane, true if there was any wood in it
    /// </summary>
    public static bool ClearPlane(byte[,,] voxels, int normal, int plane)
    {
        bool removed = false;
        for (int a = 0; a < Size; a++)
        for (int b = 0; b < Size; b++)
        {
            (int x, int y, int z) = Coords(normal, plane, a, b);
            if (voxels[x, y, z] == (byte)EnumWoodMaterial.Empty) continue;
            voxels[x, y, z] = (byte)EnumWoodMaterial.Empty;
            removed = true;
        }
        return removed;
    }

    // Bite a notch into the plane, starting from the high or low end of the bite axis
    private static EnumChopResult Bite(byte[,,] voxels, int normal, int plane, int biteAxis, bool fromHigh, int depth)
    {
        int first = -1;
        for (int i = 0; i < Size; i++)
        {
            if (SliceHasWood(voxels, normal, plane, biteAxis, fromHigh ? Size - 1 - i : i)) { first = i; break; }
        }
        if (first < 0) return EnumChopResult.None;

        for (int i = first; i < Math.Min(Size, first + depth); i++)
        {
            ClearSlice(voxels, normal, plane, biteAxis, fromHigh ? Size - 1 - i : i);
        }

        // Got all the way through
        for (int c = 0; c < Size; c++)
        {
            if (SliceHasWood(voxels, normal, plane, biteAxis, c)) return EnumChopResult.Bite;
        }
        return EnumChopResult.Split;
    }

    // One line through the plane at a fixed spot along the bite axis
    private static void ClearSlice(byte[,,] voxels, int normal, int plane, int biteAxis, int c)
    {
        int[] p = new int[3];
        p[normal] = plane;
        p[biteAxis] = c;
        int other = 3 - normal - biteAxis;
        for (int i = 0; i < Size; i++)
        {
            p[other] = i;
            voxels[p[0], p[1], p[2]] = (byte)EnumWoodMaterial.Empty;
        }
    }

    private static bool SliceHasWood(byte[,,] voxels, int normal, int plane, int biteAxis, int c)
    {
        int[] p = new int[3];
        p[normal] = plane;
        p[biteAxis] = c;
        int other = 3 - normal - biteAxis;
        for (int i = 0; i < Size; i++)
        {
            p[other] = i;
            if (voxels[p[0], p[1], p[2]] != (byte)EnumWoodMaterial.Empty) return true;
        }
        return false;
    }

    private static (int, int, int) Coords(int normal, int plane, int a, int b) => normal switch
    {
        0 => (plane, a, b),
        1 => (a, plane, b),
        _ => (a, b, plane)
    };
}
