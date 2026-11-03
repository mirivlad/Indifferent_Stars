using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
using IndifferentStars.Simulation;
namespace IndifferentStars.Tests;
public sealed class SensorTests
{
    [Theory]
    [InlineData(3_000_000, false, ContactSource.Tactical)]
    [InlineData(3_000_001, false, null)]
    [InlineData(75_000_000, true, ContactSource.DistantSignal)]
    [InlineData(75_000_001, true, null)]
    public void SensorsHaveDistinctBoundaries(double distance, bool cruise, ContactSource? expected)
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var viewer = world.AddPlayer("Viewer"); var target = world.AddPlayer("Target");
        target.TransponderEnabled = false; target.Cruising = cruise;
        target.Position = viewer.Position + new Vec3d(distance, 0, 0);
        Assert.Equal(expected, world.Detect(viewer, target));
        Assert.DoesNotContain(world.BuildSnapshot(viewer.Id), e => e.Id == target.Id);
    }
    [Fact]
    public void TranspondersRevealIdentityAcrossSystemWithoutDetailedSnapshots()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var viewer = world.AddPlayer("Viewer"); var target = world.AddPlayer("Target");
        target.Position = viewer.Position + new Vec3d(1e11, 0, 0);
        var contact = Assert.Single(world.BuildSensorContacts(viewer.Id), c => c.Id == target.Id);
        Assert.Equal(ContactSource.Transponder, contact.Source); Assert.Equal(target.Name, contact.Name);
        Assert.DoesNotContain(world.BuildSnapshot(viewer.Id), e => e.Id == target.Id);
        target.TransponderEnabled = false;
        Assert.Equal(ContactSource.LastKnown, Assert.Single(world.BuildSensorContacts(viewer.Id), c => c.Id == target.Id).Source);
        for (var i=0; i<11; i++) world.Tick(1);
        Assert.Equal(ContactSource.Lost, Assert.Single(world.BuildSensorContacts(viewer.Id), c => c.Id == target.Id).Source);
        target.TransponderEnabled = true;
        Assert.Equal(ContactSource.Transponder, Assert.Single(world.BuildSensorContacts(viewer.Id), c => c.Id == target.Id).Source);
    }
    [Fact]
    public void DistantSignalsAreAnonymousAndCannotBeUsedForLiveNavigation()
    {
        var world = new GameWorld(WorldCatalog.Generate(20261101));
        var viewer = world.AddPlayer("Viewer"); var target = world.AddPlayer("Hidden");
        target.Position = viewer.Position + new Vec3d(10_012_345, 0, 0);
        target.TransponderEnabled = false; target.Cruising = true;
        var contact = Assert.Single(world.BuildSensorContacts(viewer.Id), c => c.Id == target.Id);
        Assert.Equal("?", contact.Name); Assert.Equal(Vec3d.Zero, contact.Velocity); Assert.Equal(0u, contact.DestinationId);
        Assert.False(world.StartApproach(viewer.Id, target.Id)); Assert.False(world.Warp(viewer.Id, target.Id).Success);
        Assert.False(world.Scan(viewer.Id, target.Id).Success);
    }
    [Fact]
    public void HiddenConcentratedTrafficProducesOnlyAnonymousDistantSignals()
    {
        var world=new GameWorld(WorldCatalog.Generate(20261101));var viewer=world.AddPlayer("Viewer");
        var target=world.AddPlayer("Hidden cluster");target.TransponderEnabled=false;
        target.Position=viewer.Position+new Vec3d(50_000_000,0,0);
        Assert.Null(world.Detect(viewer,target));
        for(var i=1;i<WorldCatalog.ClusterSignalMembers;i++)
        {
            var member=world.AddPlayer("Hidden member");member.TransponderEnabled=false;
            member.Position=target.Position+Vec3d.Up*(1000*i);
        }
        Assert.Equal(ContactSource.DistantSignal,world.Detect(viewer,target));
        var signal=Assert.Single(world.BuildSensorContacts(viewer.Id),c=>c.Id==target.Id);
        Assert.Equal("?",signal.Name);Assert.Equal(0,(int)signal.Kind);Assert.Equal(Vec3d.Zero,signal.Velocity);
        Assert.False(world.StartApproach(viewer.Id,target.Id));
    }
    [Fact]
    public void SensorPacketsAreBoundedValidatedAndRoundTrip()
    {
        var contacts = Enumerable.Range(1,8).Select(i => new SensorContact((uint)i,new string('X',48),
            new Vec3d(1e12, 2, 3),Vec3d.Zero,ContactSource.Transponder,100,true,3)).ToArray();
        var packet = ProtocolCodec.Encode(new SensorContactsMessage(4, contacts));
        Assert.True(packet.Length <= ProtocolConstants.MaxPacketSize);
        Assert.True(ProtocolCodec.TryDecode(packet,out var decoded,out _));
        Assert.Equal(contacts,Assert.IsType<SensorContactsMessage>(decoded).Contacts);
        Assert.False(ProtocolCodec.TryDecode(packet[..^1],out _,out _));
        var invalid = ProtocolCodec.Encode(new SensorContactsMessage(1,new[]{contacts[0] with {Source=(ContactSource)255}}));
        Assert.False(ProtocolCodec.TryDecode(invalid,out _,out _));
        Assert.Throws<ArgumentException>(() => ProtocolCodec.Encode(new SensorContactsMessage(1, contacts.Concat(contacts).ToArray())));
    }
}
