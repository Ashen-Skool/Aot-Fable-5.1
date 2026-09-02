using UnityEngine;

namespace ODM
{
    /// <summary>One frame of ODM input. Live input and FlightScript both produce this.</summary>
    [System.Serializable]
    public struct OdmInput
    {
        public float moveX, moveY;   // WASD, -1..1
        public bool hook;            // RMB held
        public bool boost;           // Space held
        public bool reel;            // Shift held
        public bool hasAim;          // true: hooks fire at aimPoint (world); false: use camera forward
        public Vector3 aimPoint;
        public bool hasLook;         // true: body/boost direction is toward lookPoint; false: same as aim
        public Vector3 lookPoint;

        public Vector2 Move => new Vector2(moveX, moveY);
    }
}
