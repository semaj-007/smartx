using SmartX.Core.Models;

namespace SmartX.Core.Services;

/// <summary>
/// Recursively walks a nested device-deployment tree (Facility -> Zone ->
/// Sub-Zone -> Device) to validate configuration state, locate nodes, and
/// count devices. Each method calls itself on every child node, so the
/// same logic handles trees of any depth without hard-coding levels.
/// </summary>
public static class DeploymentTreeValidator
{
    /// <summary>Recursively finds every node that is not configured, returning its full path.</summary>
    public static List<string> Validate(DeviceNode node, string path = "")
    {
        var invalid = new List<string>();
        string currentPath = string.IsNullOrEmpty(path) ? node.Name : $"{path} -> {node.Name}";

        if (!node.IsConfigured)
            invalid.Add(currentPath);

        foreach (var child in node.Children)
            invalid.AddRange(Validate(child, currentPath));

        return invalid;
    }

    /// <summary>Recursively depth-first searches the tree for a node by name.</summary>
    public static DeviceNode? FindNode(DeviceNode node, string name)
    {
        if (node.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNode(child, name);
            if (found is not null)
                return found;
        }

        return null;
    }

    /// <summary>Recursively counts every leaf node whose NodeType is "Device".</summary>
    public static int CountDevices(DeviceNode node)
    {
        int count = node.NodeType == "Device" ? 1 : 0;

        foreach (var child in node.Children)
            count += CountDevices(child);

        return count;
    }
}
