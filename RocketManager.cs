using System;
using System.Collections.Generic;
using System.Reflection;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.World;
using SmartSASMod;
using UnityEngine;

namespace NOVA_Autopilot
{
    /// <summary>
    /// Centralises all low-level rocket control operations.
    /// Autopilot modules delegate here instead of duplicating control code.
    /// Holds no mission state -- only rocket state.
    /// </summary>
    public class RocketManager
    {
        private Rocket rocket;

        // Last known non-zero acceleration -- used when engines are not burning
        private double cachedMaxAcceleration = 0;

        // Stages that have already been staged this session
        private HashSet<Stage> stagingAttempted = new HashSet<Stage>();

        // Tuneable tolerances
        public float ThrottleSnapThreshold = 0.01f;  // below this -> snap to 0

        public bool DebugLog = true;

        // ── Construction ───────────────────────────────────────────────────────

        public RocketManager(Rocket rocket)
        {
            this.rocket = rocket;
        }

        public void SetRocket(Rocket rocket)
        {
            this.rocket = rocket;
        }

        public void ResetStagingHistory()
        {
            stagingAttempted.Clear();
        }

        // ── Throttle ───────────────────────────────────────────────────────────

        /// <summary>Sets throttle [0-1]. Values below ThrottleSnapThreshold snap to 0.</summary>
        public void SetThrottle(float percent)
        {
            float effective = Mathf.Clamp01(percent);
            if (effective < ThrottleSnapThreshold) effective = 0f;

            rocket.throttle.throttlePercent.Value = effective;
            rocket.throttle.throttleOn.Value      = effective > 0f;
        }

        /// <summary>Immediately cuts all engine throttle.</summary>
        public void CutEngines() => SetThrottle(0f);

        // ── Staging ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fires the next stage if no engine is currently producing thrust.
        /// Pass skipDuringLiftoff=true from a liftoff state to avoid premature staging.
        /// </summary>
        public void CheckStaging(bool skipDuringLiftoff = false)
        {
            if (skipDuringLiftoff) return;
            if (rocket.staging.stages.Count == 0) return;

            Stage stage = rocket.staging.stages[0];
            if (stagingAttempted.Contains(stage)) return;
            if (HasThrust()) return;

            stagingAttempted.Add(stage);
            FireStage(stage);
        }

        private void FireStage(Stage stage)
        {
            try
            {
                if (StagingDrawer.main == null)
                {
                    if (DebugLog) Debug.Log("[RocketManager] StagingDrawer.main is null");
                    return;
                }

                var method = typeof(StagingDrawer).GetMethod("UseStage",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null)
                {
                    if (DebugLog) Debug.Log("[RocketManager] UseStage method not found");
                    return;
                }

                bool hadControl = PlayerController.main.hasControl.Value;
                PlayerController.main.hasControl.Value = true;
                method.Invoke(StagingDrawer.main, new object[] { stage });
                PlayerController.main.hasControl.Value = hadControl;

                if (DebugLog) Debug.Log($"[RocketManager] Stage {stage.stageId} fired");
            }
            catch (Exception e)
            {
                if (DebugLog) Debug.Log($"[RocketManager] FireStage exception: {e.Message}");
            }
        }

        // ── SSAS attitude control ──────────────────────────────────────────────

        /// <summary>Points the rocket surface-up with a pitch offset for gravity turns.</summary>
        public void SASGravityTurn(float pitchOffset)
        {
            SASComponent sas = rocket.GetSAS();
            sas.Direction = DirectionMode.Surface;
            sas.Offset    = pitchOffset;
        }

        /// <summary>Points the rocket toward the selected SAS target.</summary>
        public void SASPointAtTarget(float offset = 0f)
        {
            SASComponent sas = rocket.GetSAS();
            sas.Direction = DirectionMode.Target;
            sas.Offset    = offset;
        }

        /// <summary>Points the rocket prograde.</summary>
        public void SASPrograde()
        {
            SASComponent sas = rocket.GetSAS();
            sas.Direction = DirectionMode.Prograde;
            sas.Offset    = 0f;
        }

        /// <summary>Points the rocket retrograde.</summary>
        public void SASRetrograde()
        {
            SASComponent sas = rocket.GetSAS();
            sas.Direction = DirectionMode.Prograde;
            sas.Offset    = 180f;
        }

        /// <summary>Returns SAS to its default (off / hold) state and zeroes the offset.</summary>
        public void SASDefault()
        {
            SASComponent sas = rocket.GetSAS();
            sas.Direction = DirectionMode.Default;
            sas.Offset    = 0f;
        }

        // ── Velocity and position ──────────────────────────────────────────────

        /// <summary>Altitude above the planet surface in metres.</summary>
        public double GetAltitude()
        {
            if (rocket?.location?.planet?.Value == null) return 0;
            return rocket.location.position.Value.magnitude - rocket.location.planet.Value.Radius;
        }

        /// <summary>Apoapsis altitude above surface in metres.</summary>
        public double GetApoapsisAltitude()
        {
            if (rocket?.location?.planet?.Value == null) return 0;
            return rocket.physics.loader.apoapsis - rocket.location.planet.Value.Radius;
        }

        /// <summary>Periapsis altitude above surface in metres.</summary>
        public double GetPeriapsisAltitude()
        {
            if (rocket?.location?.planet?.Value == null) return 0;
            return rocket.physics.loader.periapsis - rocket.location.planet.Value.Radius;
        }

        /// <summary>Current speed (magnitude of velocity vector) in m/s.</summary>
        public double GetSpeed()
        {
            return rocket.location.velocity.Value.magnitude;
        }

        // ── Engine performance ─────────────────────────────────────────────────

        /// <summary>Total thrust from all active engines and boosters (N).</summary>
        public double GetMaxThrust()
        {
            double thrust = 0;
            foreach (var e in rocket.partHolder.GetModules<EngineModule>())
                if (e.engineOn.Value) thrust += e.thrust.Value;
            foreach (var b in rocket.partHolder.GetModules<BoosterModule>())
                if (b.enabled) thrust += b.thrustVector.Value.magnitude;
            return thrust * 9.8;
        }

        /// <summary>
        /// Maximum acceleration from all active engines (m/s2).
        /// Returns the last cached value when engines are off.
        /// </summary>
        public double GetMaxAcceleration()
        {
            double thrust = 0;
            foreach (var e in rocket.partHolder.GetModules<EngineModule>())
                if (e.engineOn.Value) thrust += e.thrust.Value * 9.8;
            foreach (var b in rocket.partHolder.GetModules<BoosterModule>())
                if (b.enabled) thrust += b.thrustVector.Value.magnitude * 9.8;

            double mass = rocket.mass.GetMass();
            if (mass <= 0.00001) return cachedMaxAcceleration;

            double accel = thrust / mass;
            if (accel > 0.001) cachedMaxAcceleration = accel;

            return accel > 0.001 ? accel : cachedMaxAcceleration;
        }

        /// <summary>True if any engine or booster is currently producing thrust.</summary>
        public bool HasThrust()
        {
            foreach (var e in rocket.partHolder.GetModules<EngineModule>())
                if (e.engineOn.Value && e.thrust.Value > 0.001f) return true;
            foreach (var b in rocket.partHolder.GetModules<BoosterModule>())
                if (b.enabled && b.thrustVector.Value.magnitude > 0.001f) return true;
            return false;
        }

        // ── Utility ────────────────────────────────────────────────────────────

        /// <summary>Wraps an angle to [-180, +180] degrees.</summary>
        public static float NormalizeAngle(float angle)
        {
            float m = (angle + 180f) % 360f;
            if (m < 0) m += 360f;
            return m - 180f;
        }
    }
}