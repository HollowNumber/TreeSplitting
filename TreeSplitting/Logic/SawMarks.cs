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
    /// Cells in the order the saw takes them: it starts at the first mark and works toward the second.
    /// On the side of the log it goes row by row from the top down, each row from the first mark's side across
    /// </summary>
    public List<Vec3i> CutOrder(byte[,,] voxels)
    {
        int across = Normal == 1 ? U : (U == 1 ? V : U);
        int other = across == U ? V : U;
        int Away(Vec3i c, int axis) => Math.Abs(Get(c, axis) - Get(A, axis));

        if (Normal != 1) return FaceCells().OrderBy(c => -c.Y).ThenBy(c => Away(c, across)).ToList();
        return FaceCells().OrderBy(c => Away(c, other)).ThenBy(c => Away(c, across)).ToList();
    }

    /// <summary>
    /// A saw can't start in the middle of a face, the edge it starts from has to be next to open air: the outside
    /// of the log or something already cut. On the side that's the top row, on the top face the first mark's row
    /// </summary>
    public bool StartsAtOpenEdge(byte[,,] voxels)
    {
        int across = Normal == 1 ? U : (U == 1 ? V : U);
        int other = across == U ? V : U;

        int a1 = Math.Min(Get(A, across), Get(B, across)), a2 = Math.Max(Get(A, across), Get(B, across));
        // The line of cells just past the starting edge, any one of them being air is enough
        bool LineOpen(int lineAxis, int at, int from, int to)
        {
            if (at is < 0 or > 15) return true;
            int run = lineAxis == across ? other : across;
            for (int i = from; i <= to; i++)
            {
                if (ColumnEmpty(voxels, Make(lineAxis, at, run, i, Normal, Get(A, Normal)))) return true;
            }
            return false;
        }

        if (Normal != 1)
        {
            int top = Math.Max(A.Y, B.Y);
            return LineOpen(1, top + 1, a1, a2);
        }

        int o1 = Math.Min(Get(A, other), Get(B, other)), o2 = Math.Max(Get(A, other), Get(B, other));
        int side = Get(A, other) <= Get(B, other) ? o1 - 1 : o2 + 1;
        if (LineOpen(other, side, a1, a2)) return true;

        // A single row can also start from its end at the first mark
        if (o1 == o2)
        {
            int end = Get(A, across) <= Get(B, across) ? a1 - 1 : a2 + 1;
            return LineOpen(across, end, o1, o2);
        }
        return false;
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
