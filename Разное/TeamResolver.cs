using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized helper for resolving and syncing team identity.
/// Uses HealthManager.TeamTag as primary source, with Unity tag as fallback.
/// </summary>
public static class TeamResolver
{
    private const string Untagged = "Untagged";
    private const string PlayerTag = "Player";

    private static readonly HashSet<int> warnedObjects = new HashSet<int>();
    private static string cachedPlayerTeamTag;
    private static int cachedPlayerFrame = -1;

    public static string ResolveTeamTag(Component component)
    {
        if (component == null) return null;

        var hm = component.GetComponentInParent<HealthManager>();
        if (hm != null && !string.IsNullOrEmpty(hm.TeamTag))
            return hm.TeamTag;

        return ResolveUnityTag(component.gameObject);
    }

    public static string ResolveTeamTag(GameObject obj)
    {
        if (obj == null) return null;

        var hm = obj.GetComponentInParent<HealthManager>();
        if (hm != null && !string.IsNullOrEmpty(hm.TeamTag))
            return hm.TeamTag;

        return ResolveUnityTag(obj);
    }

    public static string ResolvePlayerTeamTag()
    {
        if (Time.frameCount == cachedPlayerFrame) return cachedPlayerTeamTag;

        cachedPlayerFrame = Time.frameCount;
        var player = GameObject.FindGameObjectWithTag(PlayerTag);
        cachedPlayerTeamTag = ResolveTeamTag(player);
        return cachedPlayerTeamTag;
    }

    public static bool IsAlly(Component observer, Component target, string observerTeamOverride = null)
    {
        string observerTeam = !string.IsNullOrEmpty(observerTeamOverride)
            ? observerTeamOverride
            : ResolveTeamTag(observer);

        string targetTeam = ResolveTeamTag(target);

        if (string.IsNullOrEmpty(observerTeam) || string.IsNullOrEmpty(targetTeam))
            return false;

        return observerTeam == targetTeam;
    }

    public static bool IsEnemy(Component observer, Component target, string observerTeamOverride = null)
    {
        string observerTeam = !string.IsNullOrEmpty(observerTeamOverride)
            ? observerTeamOverride
            : ResolveTeamTag(observer);

        string targetTeam = ResolveTeamTag(target);

        if (string.IsNullOrEmpty(observerTeam) || string.IsNullOrEmpty(targetTeam))
            return true;

        return observerTeam != targetTeam;
    }

    public static bool IsEnemyToPlayer(Component target)
    {
        string playerTeam = ResolvePlayerTeamTag();
        string targetTeam = ResolveTeamTag(target);

        if (string.IsNullOrEmpty(playerTeam) || string.IsNullOrEmpty(targetTeam))
            return true;

        return playerTeam != targetTeam;
    }

    public static void AssignTeamTag(GameObject obj, string teamTag, bool syncUnityTag = true, bool warnOnMismatch = true)
    {
        if (obj == null || string.IsNullOrEmpty(teamTag)) return;

        var hm = obj.GetComponentInParent<HealthManager>();
        if (hm != null)
        {
            hm.teamTag = teamTag;
            obj = hm.gameObject;
        }

        if (syncUnityTag)
        {
            TrySyncUnityTag(obj, teamTag, warnOnMismatch);
        }
    }

    public static void TrySyncUnityTag(GameObject obj, string teamTag, bool warnOnMismatch = true)
    {
        if (obj == null || string.IsNullOrEmpty(teamTag)) return;

        string currentTag = obj.tag;

        if (currentTag == PlayerTag)
        {
            if (warnOnMismatch && teamTag != PlayerTag)
                WarnOnce(obj, $"[TeamResolver] {obj.name} uses Player tag; keeping it despite team '{teamTag}'.");
            return;
        }

        if (warnOnMismatch && currentTag != Untagged && currentTag != teamTag)
            WarnOnce(obj, $"[TeamResolver] {obj.name} tag '{currentTag}' differs from team '{teamTag}'.");

        if (currentTag == teamTag) return;

        try
        {
            obj.tag = teamTag;
        }
        catch (UnityException ex)
        {
            if (warnOnMismatch)
                WarnOnce(obj, $"[TeamResolver] Unable to set tag '{teamTag}' on {obj.name}: {ex.Message}");
        }
    }

    private static string ResolveUnityTag(GameObject obj)
    {
        if (obj == null) return null;

        string tag = obj.tag;
        if (string.IsNullOrEmpty(tag) || tag == Untagged) return null;

        return tag;
    }

    private static void WarnOnce(GameObject obj, string message)
    {
        if (obj == null) return;

        int id = obj.GetInstanceID();
        if (warnedObjects.Contains(id)) return;

        warnedObjects.Add(id);
        Debug.LogWarning(message);
    }
}
