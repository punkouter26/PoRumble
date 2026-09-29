using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// Where in the ring the damage was done: a coarse grid over the canvas, each cell holding
    /// the hit points that landed on it this match.
    ///
    /// A fixed array allocated once. It is written on every landed punch, which in a ten-way is
    /// several times a second, and a grid that grew or was rebuilt would put allocation into
    /// exactly the frames where the fight is busiest.
    ///
    /// Derived state only. Nothing reads it back into the simulation.
    /// </summary>
    public sealed class DamageMapModel
    {
        /// <summary>
        /// Cells per side, whatever the ring's size. On the 17-unit SampleScene ring a cell is
        /// about a quarter of a unit, and the system's splat kernel spreads each punch over a
        /// fighter's width - fine enough to show which corner the fight lived in, smooth enough
        /// that a single punch does not read as a pinprick.
        /// </summary>
        public const int RESOLUTION = 64;

        private readonly float[] _cells = new float[RESOLUTION * RESOLUTION];

        /// <summary>Half the ring's width and height, the same extent BoxerSystem clamps fighters to.</summary>
        public Vector2 HalfExtent { get; private set; } = new(20f, 20f);

        /// <summary>The hottest cell's value, so a view can normalise without scanning the grid again.</summary>
        public float Peak { get; private set; }

        /// <summary>Everything deposited this match. Zero means nothing landed and there is no map to show.</summary>
        public float Total { get; private set; }

        /// <summary>Sets the ring the grid covers. Clears it, since cells mean different places at a different extent.</summary>
        public void Configure(Vector2 halfExtent)
        {
            HalfExtent = new Vector2(Mathf.Max(0.01f, halfExtent.x), Mathf.Max(0.01f, halfExtent.y));
            Clear();
        }

        /// <summary>
        /// The cell a ring position falls in. False outside the ropes: a punch can land on a
        /// fighter leaning over them, and it is not the canvas's damage.
        /// </summary>
        public bool TryWorldToCell(Vector2 world, out int cellX, out int cellY)
        {
            float u = (world.x + HalfExtent.x) / (2f * HalfExtent.x);
            float v = (world.y + HalfExtent.y) / (2f * HalfExtent.y);

            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                cellX = -1;
                cellY = -1;
                return false;
            }

            // A position exactly on the far rope floors to RESOLUTION, one past the last cell.
            cellX = Mathf.Min(RESOLUTION - 1, Mathf.FloorToInt(u * RESOLUTION));
            cellY = Mathf.Min(RESOLUTION - 1, Mathf.FloorToInt(v * RESOLUTION));
            return true;
        }

        /// <summary>The ring position at the centre of a cell.</summary>
        public Vector2 CellCentre(int cellX, int cellY)
        {
            return new Vector2(
                ((cellX + 0.5f) / RESOLUTION * 2f - 1f) * HalfExtent.x,
                ((cellY + 0.5f) / RESOLUTION * 2f - 1f) * HalfExtent.y);
        }

        public float At(int cellX, int cellY)
        {
            return InRange(cellX, cellY) ? _cells[cellY * RESOLUTION + cellX] : 0f;
        }

        /// <summary>Adds damage to one cell. Silently ignores cells off the grid, which is what lets a kernel run off the edge.</summary>
        public void Add(int cellX, int cellY, float amount)
        {
            if (!InRange(cellX, cellY) || amount <= 0f)
            {
                return;
            }

            int index = cellY * RESOLUTION + cellX;
            float value = _cells[index] + amount;
            _cells[index] = value;
            Total += amount;

            if (value > Peak)
            {
                Peak = value;
            }
        }

        public void Clear()
        {
            for (int index = 0; index < _cells.Length; index++)
            {
                _cells[index] = 0f;
            }

            Peak = 0f;
            Total = 0f;
        }

        private static bool InRange(int cellX, int cellY)
        {
            return cellX >= 0 && cellX < RESOLUTION && cellY >= 0 && cellY < RESOLUTION;
        }
    }
}
