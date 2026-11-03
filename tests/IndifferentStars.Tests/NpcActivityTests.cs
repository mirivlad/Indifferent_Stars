using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;
namespace IndifferentStars.Tests;
public sealed class NpcActivityTests
{
    [Fact]
    public void PopulationJobsAndNamesAreSeededAndDistributed()
    {
        var a=new GameWorld(WorldCatalog.Generate(20261101));var b=new GameWorld(WorldCatalog.Generate(20261101));
        Assert.Equal(a.Npcs.Select(n=>n.Name),b.Npcs.Select(n=>n.Name));
        Assert.Equal(a.Npcs.Select(n=>n.Position),b.Npcs.Select(n=>n.Position));
        Assert.Equal(a.Npcs.Count,a.Npcs.Select(n=>n.Name).Distinct().Count());
        Assert.Equal(4,a.Npcs.Select(n=>n.Work!.Occupation).Distinct().Count());
        Assert.Equal(a.Catalog.Stations.Count,a.Npcs.Select(n=>n.Work!.HomeStationId).Distinct().Count());
        Assert.True(a.Npcs.Count>=8);
        for(var i=0;i<100;i++){a.Tick(.05);b.Tick(.05);}
        Assert.Equal(a.Npcs.Select(n=>n.Position),b.Npcs.Select(n=>n.Position));
    }
    [Fact]
    public void CourierMovesActualStockBetweenStationsWithoutCreatingCargo()
    {
        var world=new GameWorld(WorldCatalog.Generate(20261101));
        var ship=world.Npcs.First(n=>n.Work!.Occupation==NpcOccupation.Courier);var work=ship.Work!;
        var source=world.Catalog.Find(work.HomeStationId)!;
        ship.Docked=true;ship.DockingStationId=(uint)source.Id;ship.Position=source.Position+ModelPose.StationDockingOffset;
        work.Until=0;work.Phase=NpcPhase.Loading;
        var before=CommodityCatalog.All.ToDictionary(c=>c.Id,c=>world.Catalog.Stations.Sum(s=>world.StationStock(s.Id,c.Id)));
        world.Tick(.05);Assert.Equal(6,work.Cargo);Assert.True(ship.RouteActive);Assert.False(ship.Docked);
        var commodity=work.CommodityId;Assert.Equal(before[commodity]-6,world.Catalog.Stations.Sum(s=>world.StationStock(s.Id,commodity)));
        var destination=world.Catalog.Find(work.TargetId)!;
        ship.Position=destination.Position+ModelPose.StationDockingOffset;ship.Velocity=Vec3d.Zero;
        ship.Docked=true;ship.DockingStationId=(uint)destination.Id;ship.RouteActive=false;work.Phase=NpcPhase.Unloading;work.Until=0;
        world.Tick(.05);Assert.Equal(0,work.Cargo);Assert.Equal(1,work.Deliveries);
        Assert.Equal(before[commodity],world.Catalog.Stations.Sum(s=>world.StationStock(s.Id,commodity)));
        Assert.Contains(world.Activities,e=>e.SourceId==ship.Id&&e.Kind=="arrival");
    }
    [Fact]
    public void ExtractionDepletesTheActualAsteroidAndSurveyRecordsTheActualTarget()
    {
        var world=new GameWorld(WorldCatalog.Generate(20261101));var miner=world.Npcs.First(n=>n.Work!.Occupation==NpcOccupation.Miner);
        var work=miner.Work!;var source=world.Catalog.Find(work.TargetId)!;var count=world.DepositRemaining(source.Id);
        miner.Position=work.Goal;miner.Velocity=Vec3d.Zero;miner.RouteActive=false;work.Phase=NpcPhase.Working;work.Until=0;
        world.Tick(.05);Assert.Equal(count-6,world.DepositRemaining(source.Id));Assert.Equal(6,work.Cargo);Assert.True(miner.RouteActive);
        var survey=world.Npcs.First(n=>n.Work!.Occupation==NpcOccupation.Surveyor);var target=survey.Work!.TargetId;
        survey.Position=survey.Work.Goal;survey.Velocity=Vec3d.Zero;survey.RouteActive=false;survey.Work.Phase=NpcPhase.Working;survey.Work.Until=0;
        world.Tick(.05);Assert.Contains(target,world.SurveyedObjects);Assert.Contains(world.Activities,e=>e.SourceId==survey.Id&&e.Kind=="survey"&&e.SystemWide);
    }
    [Fact]
    public void InterruptedFlightStopsAndReportsARealLocationWithoutLosingCargo()
    {
        var world=new GameWorld(WorldCatalog.Generate(20261101));var ship=world.Npcs.First(n=>n.Work!.Occupation==NpcOccupation.Miner);
        ship.RouteActive=false;ship.RouteIndex=0;ship.Work!.Cargo=4;var position=ship.Position;
        world.Tick(.05);Assert.Equal(NpcPhase.Disabled,ship.Work.Phase);Assert.Equal(4,ship.Work.Cargo);
        var message=Assert.Single(world.Activities,e=>e.SourceId==ship.Id&&e.Kind=="fault");
        Assert.True(message.SystemWide);Assert.Equal(position,message.Position);
        var packet=new ChatLineMessage(1,0,RadioChannels.System,ship.Id,ship.Name,message.English,1000,message.Russian,message.Position);
        Assert.True(ProtocolCodec.TryDecode(ProtocolCodec.Encode(packet),out var decoded,out _));Assert.Equal(packet,decoded);
    }
    [Fact]
    public void NpcDoesNotTreatItselfAsACruiseObstacleAndCannotReceivePlayerWarpCommands()
    {
        var world=new GameWorld(WorldCatalog.Generate(20261101));var ship=world.Npcs[0];
        ship.Position=new Vec3d(0,1000000,0);var goal=ship.Position+new Vec3d(0,0,-100000000);
        ship.Work!.Goal=goal;ship.Work.Phase=NpcPhase.Travelling;
        ship.NavigationRoute=new[]{new NavigationPoint(0,goal)};ship.RouteIndex=0;ship.RouteActive=true;
        world.Tick(.05);Assert.True(ship.Cruising);
        Assert.False(world.Warp(ship.Id,0,goal).Success);
    }

    [Fact]
    public void ActualCourierCompletesAFlightAndDeliveryWithoutPlayers()
    {
        var world=new GameWorld(WorldCatalog.Generate(20261101));var ship=world.Npcs.First(n=>n.Work!.Occupation==NpcOccupation.Courier);
        var warp=false;var moving=false;
        for(var i=0;i<14000&&ship.Work!.Deliveries==0;i++)
        {world.Tick(.05);warp|=ship.InWarp;moving|=ship.Velocity.Length>10;}
        Assert.True(moving);Assert.True(warp,$"No warp: {ship.Work!.Phase}, speed {ship.Velocity.Length}, range {Vec3d.Distance(ship.Position,ship.Work.Goal)}");
        Assert.True(ship.Work!.Deliveries>0,$"{ship.Work.Phase}, pos {ship.Position}, goal {ship.Work.Goal}, speed {ship.Velocity.Length}, stage {ship.WarpStage}");
        Assert.Empty(world.Players);
    }
}
