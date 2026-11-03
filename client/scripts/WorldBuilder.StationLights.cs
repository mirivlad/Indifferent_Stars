using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared.Collision;

namespace IndifferentStars.Client;

public sealed partial class WorldBuilder
{
    private readonly List<Light3D> _stationLights = new();

    private void BuildStationLights(Node3D station)
    {
        foreach (var marker in station.FindChildren("Light_*", "Node3D", true, false).OfType<Node3D>())
        {
            var dock = marker.Name.ToString().StartsWith("Light_Dock_");
            var warm = dock || marker.Name.ToString().StartsWith("Light_Warm_");
            Light3D light = dock
                ? new SpotLight3D
                {
                    SpotRange = 400,
                    SpotAngle = 52,
                    LightEnergy = 400,
                    ShadowEnabled = _highQuality,
                }
                : new OmniLight3D
                {
                    OmniRange = 125,
                    LightEnergy = 400,
                    ShadowEnabled = false,
                };
            light.Name = marker.Name + "_Emitter";
            light.LightColor = warm ? new Color(1, .70f, .42f) : new Color(.30f, .63f, 1);
            light.Position = station.ToLocal(marker.GlobalPosition);
            light.DistanceFadeEnabled = true;
            light.DistanceFadeBegin = 8000;
            light.DistanceFadeShadow = 1500;
            light.DistanceFadeLength = 1000;
            station.AddChild(light);
            if (dock)
                light.LookAt(station.ToGlobal(new Vector3(0, (float)(4.0 * ModelPose.StationScale), 0)), Vector3.Up);
            _stationLights.Add(light);
        }
    }
}
