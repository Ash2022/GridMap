using UnityEngine;

namespace RailSimCore
{
    public static class SimTuning
    {
        // Fractions of cell size
        public const float CartLenFracOfCell = 1f / 3f;   // cart length along path
        public const float GapFracOfCell = 1f / 10f;  // gap between carts
        public const float SampleStepFracOfCell = 1f / 8f;   // path/tape sampling
        public const float EpsFracOfCell = 1e-4f;     // numeric tolerance

        // Other small constants
        public const float HeadHalfLenFracOfCell = 0.5f;    // head center to face
        public const float TapeMarginMeters = 0.10f;   // tiny extra behind tail

        // Helpers
        public static float CartLen(float cell) => cell * CartLenFracOfCell;
        public static float Gap(float cell) => cell * GapFracOfCell;
        public static float HeadHalfLen(float cell) => cell * HeadHalfLenFracOfCell;
        public static float CartHalfLen(float cell) => CartLen(cell) * 0.5f;
        public static float SampleStep(float cell) => Mathf.Max(1e-5f, cell * SampleStepFracOfCell);
        public static float Eps(float cell) => Mathf.Max(1e-5f, cell * EpsFracOfCell);
    }
}
