using System;
using System.Collections.Generic;
using System.Linq;
using IndifferentStars.Shared;
using IndifferentStars.Shared.Collision;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;
namespace IndifferentStars.Simulation;
public sealed partial class GameWorld
{
    private readonly Queue<WorldActivity> _activities=new();
    private readonly Dictionary<int,int> _deposits=new();
    public HashSet<int> SurveyedObjects { get; }=new();
    private ulong _activitySequence;
    public IReadOnlyCollection<WorldActivity> Activities=>_activities.ToArray();
    public bool TryTakeActivity(out WorldActivity activity)=>_activities.TryDequeue(out activity!);
    public int StationStock(int station,ushort commodity)=>_stationStocks[station].GetValueOrDefault(commodity);
    public int DepositRemaining(int asteroid)
    {
        if(!_deposits.TryGetValue(asteroid,out var count))
        {count=new SeedStream(Catalog.Seed,asteroid,$"ore-deposit-{Catalog.SystemIndex}").Range(120,401);_deposits[asteroid]=count;}
        return count;
    }
    private void Activity(ShipState ship,string kind,string english,string russian,bool system=false)
    {
        if(_activities.Count>=128)_activities.Dequeue();
        _activities.Enqueue(new(++_activitySequence,ship.Id,ship.Name,ship.Position,ship.Work!.TargetId,kind,english,russian,system));
    }
    private void SpawnWorkers()
    {
        var names=new SeedStream(Catalog.Seed,Catalog.SystemIndex,"npc-names");
        var population=new SeedStream(Catalog.Seed,Catalog.SystemIndex,"npc-population");uint id=5000;
        foreach(var station in Catalog.Stations)
        {
            var count=population.Range(2,4);
            for(var i=0;i<count;i++)
            {
                var job=i==0?station.Purpose switch {StationPurpose.Mining=>NpcOccupation.Miner,StationPurpose.Research=>NpcOccupation.Surveyor,_=>NpcOccupation.Courier}
                    :i==1&&station.Purpose is StationPurpose.Trade or StationPurpose.Freight?NpcOccupation.Patrol:NpcOccupation.Courier;
                var spawn=station.Position+new Vec3d(Math.Cos(i*2.1)*3200,3000+i*350,Math.Sin(i*2.1)*3200);
                var ship=new ShipState {Id=id++,Name=$"{names.Name()} {Catalog.SystemIndex+1}/{id-5000}",Role=job.ToString(),Position=spawn,
                    Work=new NpcWork {Occupation=job,HomeStationId=station.Id,TargetId=station.Id,Goal=spawn,Phase=NpcPhase.Waiting,Until=population.Between(1,12)}};
                _npcs.Add(ship);
                if(job!=NpcOccupation.Courier)NextAssignment(ship);
            }
        }
    }
    private Vec3d HoldingPoint(ShipState ship,StaticBody station)=>station.Position+
        new Vec3d((ship.Id%3-1d)*450,station.Radius+2500,(ship.Id%5-2d)*350);
    private void Journey(ShipState ship,int target,Vec3d point)
    {
        var work=ship.Work!;ship.Docked=false;ship.DockingGranted=false;ship.DockingStationId=0;
        work.TargetId=target;work.Goal=point;work.Phase=NpcPhase.Travelling;
        ship.NavigationRoute=new[]{new NavigationPoint(0,point)};ship.RouteIndex=0;ship.RouteActive=true;
        ship.CruiseBraking=false;
        ship.RouteWarping=false;ship.RouteAdvanceAfterWarp=false;ship.RouteCruiseSeconds=0;ship.ApproachFollowing=false;
        var label=Catalog.Find(target)?.Name??Catalog.FindCelestial(target)?.Name??Catalog.SystemName;
        Activity(ship,"departure",$"Departing for {label}.",$"Отправляюсь к {label}.");
    }
    private void NextAssignment(ShipState ship)
    {
        var work=ship.Work!;
        var random=new SeedStream(Catalog.Seed,(int)ship.Id,$"assignment-{Catalog.SystemIndex}-{work.CompletedJobs}-{work.Deliveries}");
        if(work.Occupation==NpcOccupation.Courier)
        {
            var home=Catalog.Find(work.HomeStationId)!;
            work.TargetId=home.Id;work.Goal=HoldingPoint(ship,home);work.Phase=NpcPhase.Docking;
        }
        else if(work.Occupation==NpcOccupation.Miner)
        {
            var targets=Catalog.Asteroids.Where(a=>a.Id>=3000&&DepositRemaining(a.Id)>0).ToArray();
            if(targets.Length==0){WaitOutside(ship,60);return;}
            var body=targets[random.Range(0,targets.Length)];
            Journey(ship,body.Id,body.Position+Vec3d.Up*(BodySurface(body,TimeSeconds).Radius+1500));
        }
        else if(work.Occupation==NpcOccupation.Surveyor)
        {
            var targets=Catalog.Celestials.Where(b=>b.Kind!=CelestialKind.Star).ToArray();var body=targets[random.Range(0,targets.Length)];
            var radial=(Catalog.Star.SystemPosition-body.SystemPosition).Normalized();
            Journey(ship,body.Id,Catalog.CelestialOffset(body)+radial*(body.Radius+Math.Max(500000,body.AtmosphereHeight+WorldCatalog.CruiseClearance+1000)));
        }
        else
        {
            var home=Catalog.Find(work.HomeStationId)!;var angle=random.Between(0,Math.Tau);
            Journey(ship,home.Id,home.Position+new Vec3d(Math.Cos(angle)*7000,6000,Math.Sin(angle)*7000));
        }
    }
    private void WaitOutside(ShipState ship,double seconds)
    {
        var work=ship.Work!;ship.Docked=false;ship.DockingGranted=false;ship.DockingStationId=0;
        work.Goal=HoldingPoint(ship,Catalog.Find(work.HomeStationId)!);
        work.Phase=NpcPhase.Waiting;work.Until=TimeSeconds+seconds;
    }
    private void DisableWorker(ShipState ship,string reason,string russian)
    {
        var work=ship.Work!;work.Phase=NpcPhase.Disabled;work.Until=TimeSeconds+35;
        ship.RouteActive=false;ship.ApproachTargetId=0;ship.DockingGranted=false;
        ship.CruiseBraking=ship.Velocity.Length>WorldCatalog.MaxBoostSpeed;
        Activity(ship,"fault",$"Technical hold: {reason}. Last known position transmitted.",$"Техническая остановка: {russian}. Передаю последнюю позицию.",true);
    }
    private void StepWorker(ShipState ship,double dt,HashSet<uint> warping)
    {
        var work=ship.Work!;
        if(ship.InWarp){StepWarp(ship,dt);return;}
        if(work.Phase==NpcPhase.Disabled)
        {
            FlightModel.StepControlled(ship,InputState.None with {Brake=true},dt);
            if(TimeSeconds>=work.Until&&ship.Velocity.Length<3)
            {Activity(ship,"recovered","Systems restored. Resuming flight.","Системы восстановлены. Продолжаю полёт.",true);Journey(ship,work.TargetId,work.Goal);}
            return;
        }
        if(ship.Docked)
        {
            ship.Velocity=Vec3d.Zero;ship.Throttle=0;
            if(TimeSeconds<work.Until)return;
            var station=Catalog.Find((int)ship.DockingStationId)!;
            if(work.Cargo>0)
            {
                _stationStocks[station.Id][work.CommodityId]+=work.Cargo;
                Activity(ship,"arrival",$"Delivered {work.Cargo} cargo units to {station.Name}.",$"Доставлено {work.Cargo} ед. груза на {station.Name}.");
                work.Cargo=0;work.Deliveries++;work.Phase=NpcPhase.Loading;work.Until=TimeSeconds+4;
                if(work.Occupation==NpcOccupation.Courier)work.HomeStationId=station.Id;
                return;
            }
            if(work.Occupation==NpcOccupation.Courier)
            {
                work.CommodityId=station.Purpose switch {StationPurpose.Mining=>(ushort)5,StationPurpose.Research=>(ushort)3,StationPurpose.Freight=>(ushort)2,_=>(ushort)6};
                if(_stationStocks[station.Id][work.CommodityId]<=0)
                {
                    var available=CommodityCatalog.All.FirstOrDefault(c=>_stationStocks[station.Id][c.Id]>0);
                    if(available is null){WaitOutside(ship,20);return;}
                    work.CommodityId=available.Id;
                }
                work.Cargo=Math.Min(6,_stationStocks[station.Id][work.CommodityId]);
                _stationStocks[station.Id][work.CommodityId]-=work.Cargo;
                var destinations=Catalog.Stations.Where(s=>s.Id!=station.Id).ToArray();
                var random=new SeedStream(Catalog.Seed,(int)ship.Id,$"cargo-{Catalog.SystemIndex}-{work.Deliveries}");var destination=destinations[random.Range(0,destinations.Length)];
                Journey(ship,destination.Id,HoldingPoint(ship,destination));
            }
            else NextAssignment(ship);
            return;
        }
        if(work.Phase==NpcPhase.Waiting)
        {
            var arrived=ApproachPilot.Arrived(ship,work.Goal);
            FlightModel.StepControlled(ship,arrived?InputState.None with {Brake=true}:ApproachPilot.Steer(ship,work.Goal),dt);
            if(TimeSeconds>=work.Until&&arrived)NextAssignment(ship);
            return;
        }
        if(work.Phase==NpcPhase.Docking)
        {
            var station=Catalog.Find(work.TargetId)!;
            if(!ship.DockingGranted)
            {
                if(!ApproachPilot.Arrived(ship,HoldingPoint(ship,station)))
                {FlightModel.StepControlled(ship,ApproachPilot.Steer(ship,HoldingPoint(ship,station)),dt);return;}
                if(!GrantDocking(ship,station)){FlightModel.StepControlled(ship,InputState.None with {Brake=true},dt);return;}
            }
            if(ApproachPilot.Arrived(ship,ship.DockingPoint))
            {ship.Docked=true;ship.DockingGranted=false;ship.ApproachTargetId=0;work.Phase=work.Cargo>0?NpcPhase.Unloading:NpcPhase.Loading;work.Until=TimeSeconds+5;ship.Velocity=Vec3d.Zero;}
            else FlightModel.StepControlled(ship,ApproachPilot.Steer(ship,ship.DockingPoint),dt);
            return;
        }
        if(work.Phase==NpcPhase.Working)
        {
            FlightModel.StepControlled(ship,InputState.None with {Brake=true},dt);
            if(TimeSeconds<work.Until)return;
            if(Vec3d.Distance(ship.Position,work.Goal)>25){DisableWorker(ship,"work site lost","потеряна рабочая точка");return;}
            work.CompletedJobs++;
            if(work.Occupation==NpcOccupation.Miner)
            {
                var body=Catalog.Find(work.TargetId)!;work.CommodityId=body.Icy?(ushort)1:(ushort)5;
                work.Cargo=Math.Min(6,DepositRemaining(body.Id));_deposits[body.Id]-=work.Cargo;
                Activity(ship,"extraction",$"Extracted {work.Cargo} units at {body.Name}.",$"Извлечено {work.Cargo} ед. ресурса: {body.Name}.");
                var home=Catalog.Find(work.HomeStationId)!;Journey(ship,home.Id,HoldingPoint(ship,home));
            }
            else
            {
                SurveyedObjects.Add(work.TargetId);
                Activity(ship,work.Occupation==NpcOccupation.Surveyor?"survey":"patrol",work.Occupation==NpcOccupation.Surveyor?"Survey complete. Observation position transmitted.":"Patrol checkpoint inspected.",
                    work.Occupation==NpcOccupation.Surveyor?"Исследование завершено. Передаю точку наблюдения.":"Контрольная точка патруля проверена.",work.Occupation==NpcOccupation.Surveyor);
                NextAssignment(ship);
            }
            return;
        }
        if(!ship.RouteActive)
        {
            if(ship.RouteIndex<ship.NavigationRoute.Length||!ApproachPilot.Arrived(ship,work.Goal))
            {DisableWorker(ship,"navigation interrupted","навигация прервана");return;}
            if(Catalog.Find(work.TargetId)?.Kind==BodyKind.Station&&work.Occupation!=NpcOccupation.Patrol)work.Phase=NpcPhase.Docking;
            else {work.Phase=NpcPhase.Working;work.Until=TimeSeconds+12+(ship.Id%13);}
            return;
        }
        if(ship.CruiseBraking&&ship.Velocity.Length<=.1)ship.CruiseBraking=false;
        var input=StepRoute(ship,dt);
        if(ship.InWarp){warping.Add(ship.Id);return;}
        ship.CruiseSpeedLimit=CruiseLimitAt(ship);
        if(input.Cruise&&!CanCruise(ship))input=InputState.None with {Brake=true};
        if(ship.CruiseBraking)input=InputState.None with {Brake=true};
        if(!work.FaultUsed&&work.Deliveries>=1&&ship.Velocity.Length>9000&&ship.Id%3==0)
        {work.FaultUsed=true;DisableWorker(ship,"power restart","перезапуск питания");input=InputState.None with {Brake=true};}
        FlightModel.StepControlled(ship,input,dt);
    }
}
