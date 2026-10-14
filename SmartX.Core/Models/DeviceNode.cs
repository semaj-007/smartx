namespace SmartX.Core.Models;

/// <summary>
/// A node in a nested device-deployment tree, e.g.
/// Facility A -> Zone 1 -> Sub-Zone B -> ESP32-Soil-01
/// Mirrors the facility/zone/sub-zone hierarchies used in real
/// smart-grid and building-management deployments.
/// </summary>
public class DeviceNode
{
    public string Name { get; set; }
    public string NodeType { get; set; }
    public bool IsConfigured { get; set; }
    public List<DeviceNode> Children { get; set; } = new();

    public DeviceNode(string name, string nodeType, bool isConfigured = true)
    {
        Name = name;
        NodeType = nodeType;
        IsConfigured = isConfigured;
    }
}
