using UnityEngine;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════════════
//  PvP INTEL PROCESSOR
//
//  Interval-based sub-system that analyses the IntelLog stored in the
//  Blackboard.  Runs alongside the ThinkingLayer and feeds it with:
//    • Threat heatmap  — weighted density of intel around the map.
//    • Enemy prediction — extrapolated position from stale contacts.
//    • Sector status   — which map areas are "clear" vs "hostile".
//
//  Industry pattern: Information Fusion / Situational Awareness model.
// ═══════════════════════════════════════════════════════════════════════════

public class PvPIntelProcessor : MonoBehaviour
{
    [Header("Analysis")]
    [SerializeField] private float analysisInterval = 1.5f;   // seconds
    [SerializeField] private float intelMaxAge      = 20f;    // seconds
    [SerializeField] private float predictionSpeed  = 3f;     // assumed enemy movement m/s

    [Header("Sector Grid")]
    [SerializeField] private float sectorSize       = 25f;    // metres per grid cell
    [SerializeField] private int   gridHalfExtent   = 4;      // ±4 cells = 200×200 m area

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private PvPBotController core;
    private float nextAnalysisTime = 0f;

    // ─── Sector Grid ─────────────────────────────────────────────────────
    //  Simple 2D grid centred on the bot's spawn.
    //  Each cell stores a "threat score" that decays over time.

    private float[,] sectorGrid;
    private Vector3 gridOrigin;
    private int gridSize;

    // ─── Output ──────────────────────────────────────────────────────────

    /// <summary>
    /// Highest-threat sector centre in world space, or Vector3.zero.
    /// ThinkingLayer reads this for movement decisions.
    /// </summary>
    public Vector3 HighestThreatSector { get; private set; }

    /// <summary>
    /// Predicted current enemy position based on last known + extrapolation.
    /// </summary>
    public Vector3 PredictedEnemyPosition { get; private set; }

    // ═════════════════════════════════════════════════════════════════════

    public void Initialize(PvPBotController controller)
    {
        core = controller;
        gridSize = gridHalfExtent * 2 + 1;
        sectorGrid = new float[gridSize, gridSize];
        gridOrigin = core.Transform.position; // snapshot spawn
    }

    // ═════════════════════════════════════════════════════════════════════
    //  UPDATE (called per-frame by PvPBotController)
    // ═════════════════════════════════════════════════════════════════════

    public void UpdateModule()
    {
        if (Time.time < nextAnalysisTime) return;
        nextAnalysisTime = Time.time + analysisInterval;

        AnalyseIntel();
        PredictEnemy();
        DecaySectorGrid();
        FindHighestThreatSector();

        // Write best prediction to blackboard for ThinkingLayer
        if (PredictedEnemyPosition != Vector3.zero)
        {
            core.Blackboard.SetVector3(PvPBlackboardKey.PredictedEnemyPosition,
                PredictedEnemyPosition);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  ANALYSIS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Read all fresh intel records and project them onto the sector grid.
    /// </summary>
    private void AnalyseIntel()
    {
        var fresh = core.Blackboard.GetFreshIntel(intelMaxAge);

        foreach (var record in fresh)
        {
            // Weight by confidence and recency
            float recency = 1f - Mathf.Clamp01(record.Age / intelMaxAge);
            float weight  = record.confidence * recency;

            // Map world position to grid cell
            int gx, gz;
            if (WorldToGrid(record.position, out gx, out gz))
            {
                sectorGrid[gx, gz] += weight;
            }
        }

        // Also mark "clear" intel as negative weight
        var clearRecords = core.Blackboard.GetFreshIntel(intelMaxAge);
        foreach (var rec in clearRecords)
        {
            if (rec.type == IntelType.SectorClear)
            {
                int gx, gz;
                if (WorldToGrid(rec.position, out gx, out gz))
                {
                    sectorGrid[gx, gz] = Mathf.Max(0f, sectorGrid[gx, gz] - 0.5f);
                }
            }
        }
    }

    /// <summary>
    /// Predict enemy position by extrapolating from last known contact.
    /// </summary>
    private void PredictEnemy()
    {
        IntelRecord visual = core.Blackboard.GetLatestIntel(IntelType.VisualContact);

        if (visual != null && visual.IsFresh(intelMaxAge))
        {
            float elapsed = visual.Age;
            // Assume enemy moved at predictionSpeed since last seen
            // Direction: away from our position (conservative estimate)
            Vector3 awayDir = (visual.position - core.Transform.position).normalized;
            PredictedEnemyPosition = visual.position + awayDir * (elapsed * predictionSpeed);

            core.Blackboard.PushIntel(new IntelRecord
            {
                type       = IntelType.EnemyPredicted,
                position   = PredictedEnemyPosition,
                timestamp  = Time.time,
                confidence = Mathf.Max(0.1f, visual.confidence - elapsed * 0.05f)
            });

            return;
        }

        // Fall back to gunfire intel
        IntelRecord gunfire = core.Blackboard.GetLatestIntel(IntelType.GunfireHeard);
        if (gunfire != null && gunfire.IsFresh(intelMaxAge))
        {
            PredictedEnemyPosition = gunfire.position;
            return;
        }

        PredictedEnemyPosition = Vector3.zero;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SECTOR GRID
    // ═════════════════════════════════════════════════════════════════════

    private void DecaySectorGrid()
    {
        float decay = 0.05f; // per analysis tick
        for (int x = 0; x < gridSize; x++)
        for (int z = 0; z < gridSize; z++)
        {
            sectorGrid[x, z] = Mathf.Max(0f, sectorGrid[x, z] - decay);
        }
    }

    private void FindHighestThreatSector()
    {
        float best = 0f;
        int bx = 0, bz = 0;

        for (int x = 0; x < gridSize; x++)
        for (int z = 0; z < gridSize; z++)
        {
            if (sectorGrid[x, z] > best)
            {
                best = sectorGrid[x, z];
                bx = x;
                bz = z;
            }
        }

        if (best > 0.1f)
        {
            HighestThreatSector = GridToWorld(bx, bz);
        }
        else
        {
            HighestThreatSector = Vector3.zero;
        }
    }

    // ─── Grid ↔ World conversion ─────────────────────────────────────────

    private bool WorldToGrid(Vector3 worldPos, out int gx, out int gz)
    {
        float dx = worldPos.x - gridOrigin.x;
        float dz = worldPos.z - gridOrigin.z;

        gx = Mathf.RoundToInt(dx / sectorSize) + gridHalfExtent;
        gz = Mathf.RoundToInt(dz / sectorSize) + gridHalfExtent;

        return gx >= 0 && gx < gridSize && gz >= 0 && gz < gridSize;
    }

    private Vector3 GridToWorld(int gx, int gz)
    {
        float wx = gridOrigin.x + (gx - gridHalfExtent) * sectorSize;
        float wz = gridOrigin.z + (gz - gridHalfExtent) * sectorSize;
        return new Vector3(wx, gridOrigin.y, wz);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get threat score for a world position (0 = safe, high = hostile).
    /// Useful for movement planning.
    /// </summary>
    public float GetThreatAtPosition(Vector3 worldPos)
    {
        int gx, gz;
        if (WorldToGrid(worldPos, out gx, out gz))
            return sectorGrid[gx, gz];
        return 0f;
    }

    /// <summary>
    /// Get the nearest sector centre that is above the given threat threshold.
    /// </summary>
    public Vector3 GetNearestHostileSector(Vector3 fromPos, float minThreat = 0.3f)
    {
        float bestDist = float.MaxValue;
        Vector3 bestPos = Vector3.zero;

        for (int x = 0; x < gridSize; x++)
        for (int z = 0; z < gridSize; z++)
        {
            if (sectorGrid[x, z] < minThreat) continue;

            Vector3 wp = GridToWorld(x, z);
            float d = Vector3.Distance(fromPos, wp);
            if (d < bestDist)
            {
                bestDist = d;
                bestPos = wp;
            }
        }

        return bestPos;
    }

    // ─── Debug Gizmo ─────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (sectorGrid == null) return;

        for (int x = 0; x < gridSize; x++)
        for (int z = 0; z < gridSize; z++)
        {
            float threat = sectorGrid[x, z];
            if (threat < 0.05f) continue;

            Vector3 center = GridToWorld(x, z);
            Gizmos.color = Color.Lerp(Color.yellow, Color.red, Mathf.Clamp01(threat));
            Gizmos.DrawWireCube(center, new Vector3(sectorSize, 1f, sectorSize));
        }

        if (PredictedEnemyPosition != Vector3.zero)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(PredictedEnemyPosition, 1.5f);
        }
    }
#endif
}
