using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RailSimCore
{


    public class Types
    {
        public enum MoveOutcome { Arrived, Blocked }

        public struct MoveCompletion
        {
            public MoveOutcome Outcome;
            public int BlockerId;     // valid if Blocked
            public Vector3 HitPos;    // approx world pos of first contact (if Blocked)
        }
    }

}