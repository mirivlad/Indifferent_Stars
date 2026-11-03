using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
namespace IndifferentStars.Simulation;
public sealed partial class GameWorld
{
    private (Vec3d Center,double Radius,StaticBody[] Bodies)[] _collisionRegions=Array.Empty<(Vec3d,double,StaticBody[])>();
    private void BuildCollisionRegions()
    {
        const double cell=50000;
        _collisionRegions=_collisionBodies.GroupBy(b=>(X:Math.Floor(b.Position.X/cell),Y:Math.Floor(b.Position.Y/cell),Z:Math.Floor(b.Position.Z/cell)))
            .Select(g=>(new Vec3d((g.Key.X+.5)*cell,(g.Key.Y+.5)*cell,(g.Key.Z+.5)*cell),
                Math.Sqrt(3)*cell*.5+g.Max(b=>BodySurface(b,0).Radius),g.ToArray())).ToArray();
    }
    private IEnumerable<StaticBody> CollisionCandidates(Vec3d from,Vec3d to,double shipRadius)
    {
        var travel=to-from;var length=travel.LengthSquared;
        foreach(var region in _collisionRegions)
        {
            var nearest=from+travel*(length>0?Math.Clamp(Vec3d.Dot(region.Center-from,travel)/length,0,1):0);
            if(Vec3d.Distance(nearest,region.Center)>region.Radius+shipRadius)continue;
            foreach(var body in region.Bodies)yield return body;
        }
    }
}
