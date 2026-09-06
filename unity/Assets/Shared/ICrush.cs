using UnityEngine;

namespace Shared
{
    /// <summary>
    /// What the town exposes so the Titan can flatten it without the Proxies assembly referencing
    /// Town. Implemented by Town.TownDestruction, registered in Ctx as "town.destruction".
    /// </summary>
    public interface ICrush
    {
        /// <summary>
        /// Bring down the nearest house still standing whose footprint is within <paramref name="radius"/>
        /// of <paramref name="p"/>, shoved along <paramref name="dir"/>. False when there is nothing
        /// close enough left to crush.
        /// </summary>
        bool CrushNear(Vector3 p, float radius, Vector3 dir);

        /// <summary>How many houses have come down this run (harness + tests).</summary>
        int Crushed { get; }
    }
}
