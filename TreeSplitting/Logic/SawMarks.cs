using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Logic;

/// <summary>
/// Two marked corners on one face of the log. Everything between them gets sawn out through the whole log, a column per cell.
/// </summary>
public record SawMarks(Vec3i A, Vec3i B, BlockFacing Face)
{
    private int Normal => Face.Axis switch { EnumAxis.X => 0, EnumAxis.Y => 1, _ => 2 };
    private int U => Normal == 0 ? 1 : 0;
    private int V => Normal == 2 ? 1 : 2;

    private static int Get(Vec3i p, int axis) => axis switch { 0 => p.X, 1 => p.Y, _ => p.Z };

    private static Vec3i Make(int axis1, int val1, int axis2, int val2, int axis3, int val3)
    {
        int[] c = new int[3];
        c[axis1] = val1; c[axis2] = val2; c[axis3] = val3;
        return new Vec3i(c[0], c[1], c[2]);
    }

    // The cells between the marks on the face, both marks included, at the depth the first mark was set
    public IEnumerable<Vec3i> FaceCells()
    {
        int u1 = Math.Min(Get(A, U), Get(B, U)), u2 = Math.Max(Get(A, U), Get(B, U));
        int v1 = Math.Min(Get(A, V), Get(B, V)), v2 = Math.Max(Get(A, V), Get(B, V));
        for (int u = u1; u <= u2; u++)
        for (int v = v1; v <= v2; v++)
        {
            yield return Make(U, u, V, v, Normal, Get(A, Normal));
        }
    }

    /// <summary>
    /// Cells in the order the saw takes them: it starts where the wood is next to open air and works in from there.
    /// Ties go top down on the side of the log, then from the first mark toward the second
    /// </summary>
    public List<Vec3i> CutOrder(byte[,,] voxels)
    {
        int across = Normal == 1 ? U : (U == 1 ? V : U);
        int other = across == U ? V : U;
        int Away(Vec3i c, int axis) => Math.Abs(Get(c, axis) - Get(A, axis));

        List<Vec3i> cells = FaceCells().ToList();
        List<Vec3i> open = cells.Where(c => Column(c).Any(v => voxels[v.X, v.Y, v.Z] != 0 && NextToAir(voxels, v))).ToList();
        int FromOpen(Vec3i c) => open.Count == 0 ? 0 : open.Min(o => Math.Abs(Get(c, U) - Get(o, U)) + Math.Abs(Get(c, V) - Get(o, V)));

        var ordered = cells.OrderBy(FromOpen);
        return (Normal != 1
            ? ordered.ThenBy(c => -c.Y).ThenBy(c => Away(c, across))
            : ordered.ThenBy(c => Away(c, other)).ThenBy(c => Away(c, across))).ToList();
    }

    // Air beside it across the face, the face itself doesn't count since that's always open
    private bool NextToAir(byte[,,] voxels, Vec3i v)
    {
        foreach (int axis in new[] { U, V })
        foreach (int step in new[] { -1, 1 })
        {
            int[] n = [v.X, v.Y, v.Z];
            n[axis] += step;
            if (n[axis] is < 0 or > 15 || voxels[n[0], n[1], n[2]] == 0) return true;
        }
        return false;
    }

    /// <summary>
    /// A saw can't start in the middle of a face, some of the wood it takes has to sit next to open air across the face:
    /// the outside of the log, a chiselled hole, an earlier cut. The face itself doesn't count, that's always open
    /// </summary>
    public bool StartsAtOpenEdge(byte[,,] voxels)
    {
        return FaceCells().Any(c => Column(c).Any(v => voxels[v.X, v.Y, v.Z] != 0 && NextToAir(voxels, v)));
    }

    // Every voxel behind a cell, all the way through the log
    public IEnumerable<Vec3i> Column(Vec3i cell)
    {
        for (int n = 0; n < 16; n++) yield return Make(U, Get(cell, U), V, Get(cell, V), Normal, n);
    }

    public bool ColumnEmpty(byte[,,] voxels, Vec3i cell)
    {
        for (int n = 0; n < 16; n++)
        {
            Vec3i p = Make(U, Get(cell, U), V, Get(cell, V), Normal, n);
            if (voxels[p.X, p.Y, p.Z] != 0) return false;
        }
        return true;
    }

    // Saws out the whole column behind a cell, returns how many voxels went
    public int CutColumn(byte[,,] voxels, Vec3i cell)
    {
        int removed = 0;
        for (int n = 0; n < 16; n++)
        {
            Vec3i p = Make(U, Get(cell, U), V, Get(cell, V), Normal, n);
            if (voxels[p.X, p.Y, p.Z] == 0) continue;
            voxels[p.X, p.Y, p.Z] = 0;
            removed++;
        }
        return removed;
    }
}
