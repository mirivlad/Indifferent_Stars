using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;
using Xunit;

namespace IndifferentStars.Tests;

public sealed class SectorTests
{
    [Fact]
    public void CatalogDefinesTwoContiguousSectors()
    {
        var catalog = WorldCatalog.Generate(2026);

        Assert.Equal(2, catalog.Sectors.Count);
        Assert.Equal("kestrel-reach-orbit", catalog.SectorAt(Vec3d.Zero).Id);
        Assert.Equal("kestrel-reach-belt", catalog.SectorAt(catalog.Sectors[1].Center).Id);
        Assert.Contains(catalog.Sectors[1].Bodies, b => b.Id >= 2000);
        Assert.All(catalog.Asteroids, body => Assert.True(catalog.Find(body.Id) is not null));
    }

    [Fact]
    public void SnapshotFiltersEntitiesOutsideRelevanceRange()
    {
        var world = new GameWorld(WorldCatalog.Generate(4321));
        var near = world.AddPlayer("Near");
        var far = world.AddPlayer("Far");
        far.Position = world.Catalog.Sectors[1].Center;

        var nearView = world.BuildSnapshot(near.Id);
        Assert.Contains(nearView, entity => entity.Id == near.Id);
        Assert.DoesNotContain(nearView, entity => entity.Id == far.Id);

        var full = world.BuildSnapshot();
        Assert.Contains(full, entity => entity.Id == far.Id);
    }

    [Fact]
    public void BeltNpcsSimulateButAreCulledFromOrbitSnapshots()
    {
        var world = new GameWorld(WorldCatalog.Generate(9876));
        var ship = world.AddPlayer("Orbit");
        var beltNpcs = world.Npcs.Where(npc =>
            Vec3d.Distance(npc.Position, world.Catalog.Sectors[1].Center) < 5000).ToList();

        Assert.NotEmpty(beltNpcs);
        for (var i = 0; i < 20; i++)
            world.Tick(0.05);

        var view = world.BuildSnapshot(ship.Id);
        Assert.DoesNotContain(view, entity => beltNpcs.Any(npc => npc.Id == entity.Id));
    }
}