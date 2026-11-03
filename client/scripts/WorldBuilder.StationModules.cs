using System.Linq;
using Godot;
using IndifferentStars.Shared.World;
namespace IndifferentStars.Client;
public sealed partial class WorldBuilder
{
    private void AddStationModules(Node3D parent,StaticBody station)
    {
        if(station.Assembly is not { } assembly)return;
        var model=parent.GetNode<Node3D>("Model");
        var sources=model.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().ToArray();
        var index=0;
        foreach(var module in assembly.Modules)
        {
            var triangles=module.Triangles();var vertices=new Vector3[triangles.Length*3];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
            for(var i=0;i<triangles.Length;i++)
            {
                var t=triangles[i];var normal=ToVector3(IndifferentStars.Shared.Vec3d.Cross(t.B-t.A,t.C-t.A).Normalized());
                var points=new[]{t.A,t.B,t.C};
                for(var j=0;j<3;j++)
                {
                    vertices[i*3+j]=ToVector3(points[j]);normals[i*3+j]=normal;
                    uv[i*3+j]=System.Math.Abs(normal.Y)>.5f?new Vector2(vertices[i*3+j].X,vertices[i*3+j].Z)/100:
                        System.Math.Abs(normal.X)>.5f?new Vector2(vertices[i*3+j].Z,vertices[i*3+j].Y)/100:new Vector2(vertices[i*3+j].X,vertices[i*3+j].Y)/100;
                }
            }
            var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=uv;
            var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            var material=sources.FirstOrDefault(s=>s.Name.ToString()==module.Material)?.GetActiveMaterial(0)?.Duplicate() as BaseMaterial3D
                ??new StandardMaterial3D {AlbedoColor=new Color(.26f,.3f,.34f),Metallic=.7f,Roughness=.55f};
            material.CullMode=BaseMaterial3D.CullModeEnum.Disabled;
            parent.AddChild(new MeshInstance3D {Name=$"AssemblyModule_{index++}",Mesh=mesh,MaterialOverride=material});
        }
    }
}
