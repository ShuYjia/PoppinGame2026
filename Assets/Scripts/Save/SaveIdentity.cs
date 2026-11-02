using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Resolves scene object keys without relying on runtime instance IDs or world positions.
/// Assign an explicit ID when an object must survive hierarchy changes.
/// </summary>
public static class SaveIdentity
{
    public static string Resolve(Component component, string explicitId)
    {
        if (component == null)
            return string.Empty;

        Scene scene = component.gameObject.scene;
        string sceneKey = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
        string componentKey = component.GetType().FullName;
        if (!string.IsNullOrWhiteSpace(explicitId))
            return sceneKey + "|" + componentKey + "|" + explicitId.Trim();

        return sceneKey + "|" + componentKey + "|" + GetHierarchyPath(component.transform);
    }

    public static void LogDuplicateIds(IEnumerable<string> ids, Object context)
    {
        HashSet<string> seen = new HashSet<string>();
        foreach (string id in ids)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            if (!seen.Add(id))
                Debug.LogError("Duplicate save identity detected: " + id, context);
        }
    }

    private static string GetHierarchyPath(Transform transform)
    {
        List<string> segments = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            segments.Add(current.name + "[" + current.GetSiblingIndex() + "]");
            current = current.parent;
        }
        segments.Reverse();
        return string.Join("/", segments);
    }
}
