using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using IndifferentStars.Shared.Collision;

namespace IndifferentStars.Shared.World;

public enum StationModuleShape { Box, Drum }
public sealed record StationModule(StationModuleShape Shape,Vec3d Size,Vec3d Center,float Yaw,string Material)
{
    public Triangle[] Triangles()
    {
        var triangles=new List<Triangle>();
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,Yaw);
        Vec3d Point(Vec3d p)
        {
            var v=SurfaceMesh.Rotate(p,rotation)+Center;
            return new Vec3d((float)v.X,(float)v.Y,(float)v.Z);
        }
        void Face(Vec3d a,Vec3d b,Vec3d c)=>triangles.Add(new(Point(a),Point(b),Point(c)));
        if(Shape==StationModuleShape.Box)
        {
            var s=Size*.5;
            var v=new[]{new Vec3d(-s.X,-s.Y,-s.Z),new Vec3d(s.X,-s.Y,-s.Z),new Vec3d(s.X,s.Y,-s.Z),new Vec3d(-s.X,s.Y,-s.Z),
                new Vec3d(-s.X,-s.Y,s.Z),new Vec3d(s.X,-s.Y,s.Z),new Vec3d(s.X,s.Y,s.Z),new Vec3d(-s.X,s.Y,s.Z)};
            foreach(var q in new[]{new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,4,7,3},new[]{1,2,6,5},new[]{3,7,6,2},new[]{0,1,5,4}})
            {Face(v[q[0]],v[q[1]],v[q[2]]);Face(v[q[0]],v[q[2]],v[q[3]]);}
        }
        else
        {
            for(var i=0;i<24;i++)
            {
                var a=Math.Tau*i/24;var b=Math.Tau*(i+1)/24;
                var lowA=new Vec3d(Math.Cos(a)*Size.X*.5,-Size.Y*.5,Math.Sin(a)*Size.Z*.5);
                var lowB=new Vec3d(Math.Cos(b)*Size.X*.5,-Size.Y*.5,Math.Sin(b)*Size.Z*.5);
                var highA=lowA+Vec3d.Up*Size.Y;var highB=lowB+Vec3d.Up*Size.Y;
                Face(lowA,highA,highB);Face(lowA,highB,lowB);
                Face(Vec3d.Up*(Size.Y*.5),highB,highA);Face(-Vec3d.Up*(Size.Y*.5),lowA,lowB);
            }
        }
        return triangles.ToArray();
    }
}

/// <summary>One seeded layout is consumed by both rendering and the server's exact triangle surface.</summary>
public sealed class StationAssembly
{
    public const int Version=1;
    public required IReadOnlyList<StationModule> Modules { get; init; }
    public double RadiusMetres=>Math.Max(SurfaceMesh.Station.Radius*ModelPose.StationScale,
        Modules.SelectMany(m=>m.Triangles()).Max(t=>Math.Max(t.A.Length,Math.Max(t.B.Length,t.C.Length))));
    private SurfaceMesh? _surface;
    public SurfaceMesh Surface => _surface??=SurfaceMesh.Compose(SurfaceMesh.Station.Triangles.Concat(
        Modules.SelectMany(m=>m.Triangles()).Select(t=>new Triangle(t.A/ModelPose.StationScale,t.B/ModelPose.StationScale,t.C/ModelPose.StationScale))),
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"assembly-{Version}|{SurfaceMesh.Station.SourceHash}|{Signature}"))));
    public required string Signature { get; init; }
    public static StationAssembly Generate(int seed,StationPurpose purpose)
    {
        var random=new SeedStream(seed,(int)purpose,"station-assembly-v1");
        var modules=new List<StationModule>();
        void Box(Vec3d size,Vec3d center,double yaw,string material="Hull")=>modules.Add(new(StationModuleShape.Box,size,center,(float)yaw,material));
        void Drum(Vec3d size,Vec3d center,string material="Hull")=>modules.Add(new(StationModuleShape.Drum,size,center,0,material));
        var arms=purpose switch {StationPurpose.Mining=>random.Range(3,6),StationPurpose.Research=>random.Range(2,4),StationPurpose.Freight=>random.Range(3,5),_=>random.Range(2,5)};
        var offset=random.Between(0,Math.Tau);
        for(var i=0;i<arms;i++)
        {
            var angle=offset+Math.Tau*i/arms;var radial=new Vec3d(Math.Cos(angle),0,Math.Sin(angle));
            var distance=random.Between(950,1500);var height=random.Between(-380,-180);
            Box(new Vec3d(distance,38,42),radial*(distance*.5)+Vec3d.Up*height,-angle,"Structure");
            var end=radial*distance+Vec3d.Up*height;
            if(purpose==StationPurpose.Mining)
            {
                Drum(new Vec3d(260,random.Between(250,480),260),end);
                Drum(new Vec3d(275,18,275),end+Vec3d.Up*100,"Structure");
                Box(new Vec3d(160,70,300),end-Vec3d.Up*240,-angle);
            }
            else if(purpose==StationPurpose.Research)
            {
                Drum(new Vec3d(140,120,140),end);
                Box(new Vec3d(24,random.Between(1400,1800),24),end+Vec3d.Up*700,0,"Structure");
                Box(new Vec3d(900,12,320),end+Vec3d.Up*80,-angle,"Solar");
                Drum(new Vec3d(360,24,360),end+Vec3d.Up*1500,"Structure");
            }
            else if(purpose==StationPurpose.Freight)
            {
                Box(new Vec3d(500,90,150),end,-angle,"Structure");
                for(var j=-1;j<=1;j++)Box(new Vec3d(130,80,260),end+radial*(j*150)+Vec3d.Up*65,-angle);
            }
            else
            {
                Drum(new Vec3d(230,random.Between(200,400),230),end);
                Drum(new Vec3d(235,18,235),end+Vec3d.Up*70,"Windows");
            }
            // Small service housings and panel strips make the added sections readable up close.
            Box(new Vec3d(60,20,85),end+Vec3d.Up*110,-angle,"Structure");
        }
        return new StationAssembly {Modules=modules.AsReadOnly(),Signature=$"{seed}:{purpose}"};
    }
}
