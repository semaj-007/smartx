using Microsoft.AspNetCore.Mvc;
using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/deployment")]
public class DeploymentController : ControllerBase
{
    private readonly TelemetryStore _store;

    public DeploymentController(TelemetryStore store)
    {
        _store = store;
    }

    /// <summary>GET /api/deployment/tree - the full nested deployment tree.</summary>
    [HttpGet("tree")]
    public IActionResult GetTree() => Ok(MapNode(_store.DeploymentTree));

    /// <summary>GET /api/deployment/validate - recursively validates every node's configuration state.</summary>
    [HttpGet("validate")]
    public IActionResult Validate()
    {
        var invalidPaths = DeploymentTreeValidator.Validate(_store.DeploymentTree);
        var deviceCount = DeploymentTreeValidator.CountDevices(_store.DeploymentTree);

        return Ok(new
        {
            TotalDevices = deviceCount,
            IsFullyValid = invalidPaths.Count == 0,
            InvalidPaths = invalidPaths
        });
    }

    private static object MapNode(DeviceNode node) => new
    {
        node.Name,
        node.NodeType,
        node.IsConfigured,
        Children = node.Children.Select(MapNode)
    };
}
