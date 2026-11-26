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
        var activity = new WorldActivity(++_activitySequence,ship.Id,ship.Name,ship.Position,ship.Work?.TargetId ?? 0,kind,english,russian,system,TimeSeconds);
        _activities.Enqueue(activity);
        RecordActivity(activity);
    }

    private void RecordActivity(WorldActivity activity)
    {
        if (_activityHistory.Count >= 128) _activityHistory.Dequeue();
        _activityHistory.Enqueue(activity);
    }
    private void SpawnWorkers()
    {
        var names=new SeedStream(Catalog.Seed,Catalog.SystemIndex,"npc-names");
        var population=new SeedStream(Catalog.Seed,Catalog.SystemIndex,"npc-population");uint id=5000;
        foreach(var station in Catalog.Stations)
        {
            var count=Catalog.WorkerCount(station);
            var roleSlots = new Dictionary<NpcOccupation, int>();
            for(var i=0;i<count;i++)
            {
                var job=station.Purpose switch
                {
                    StationPurpose.Mining when i<2 => NpcOccupation.Miner,
                    StationPurpose.Research when i%2==0 => NpcOccupation.Surveyor,
                    _ when i==1 || i==5 => NpcOccupation.Patrol,
                    _ => NpcOccupation.Courier,
                };
                var roleSlot = roleSlots.GetValueOrDefault(job);
                roleSlots[job] = roleSlot + 1;
                var spawn=station.Position+new Vec3d(Math.Cos(i*2.4)*(2200+i*180),2400+i*400,Math.Sin(i*2.4)*(2200+i*180));
                var shipId = id++;
                var ship=new ShipState {Id=shipId,Name=$"{names.Name()} {Catalog.SystemIndex+1}/{id-5000}",Role=job.ToString(),Position=spawn,
                    HullId=HullCatalog.ForWorker(job, station.Purpose, roleSlot),
                    Work=new NpcWork {Occupation=job,HomeStationId=station.Id,TargetId=station.Id,Goal=spawn,Phase=NpcPhase.Waiting,Until=population.Between(1,12)}};
                for(var attempt=0;!WarpSafe(ship,ship.Position,0);attempt++)
                {
                    if(attempt>=32)throw new InvalidOperationException("No clear NPC spawn near station.");
                    ship.Position+=Vec3d.Up*1000;
                }
                ship.Work!.Goal=ship.Position;
                _npcs.Add(ship);
                if(job!=NpcOccupation.Courier)NextAssignment(ship);
            }
        }
    }
    private static Vec3d HoldingPoint(ShipState ship, StaticBody station)
    {
        // The horizontal pattern repeats every 15 workers. Separate each repeat
        // vertically so two ships waiting for the same berth have distinct goals.
        var level = (ship.Id - 5000) / 15;
        return station.Position + new Vec3d((ship.Id % 3 - 1d) * 450,
            station.Radius + 2500 + level * 600, (ship.Id % 5 - 2d) * 350);
    }
    private void Journey(ShipState ship,int target,Vec3d point)
    {
        var work=ship.Work!;
        var departureStation = Catalog.Find((int)ship.DockingStationId) ?? Catalog.Find(work.HomeStationId)!;
        ship.Docked=false;ship.DockingGranted=false;ship.DockingStationId=0;
        work.TargetId=target;work.Goal=point;work.Phase=NpcPhase.Travelling;
        var departure=ship.Position;
        var local=ship.Position-departureStation.Position;
        var departureHeight=departureStation.Radius+WorldCatalog.CruiseClearance+2000;
        var leaving=local.Length<departureHeight && local.Y<departureHeight;
        if(leaving) departure=new Vec3d(ship.Position.X,departureStation.Position.Y+departureHeight,ship.Position.Z);
        ship.NavigationRoute=leaving
            ? new[]{new NavigationPoint(0,departure),new NavigationPoint(0,point)}
            : new[]{new NavigationPoint(0,point)};
        ship.RouteIndex=0;ship.RouteActive=true;
        ship.CruiseBraking=false;
        ship.RouteWarping=false;ship.RouteAdvanceAfterWarp=false;ship.RouteCruiseSeconds=0;ship.ApproachFollowing=false;
        var label=Catalog.Find(target)?.Name??Catalog.FindCelestial(target)?.Name??Catalog.SystemName;
        Activity(ship,"departure",$"Departing for {label}.",$"Отправляюсь к {label}.");
    }
    private void NextAssignment(ShipState ship)
    {
        var work=ship.Work!;
        var random=new SeedStream(Catalog.Seed,(int)ship.Id,$"assignment-{Catalog.SystemIndex}-{work.CompletedJobs}-{work.Deliveries}");
        if(work.Occupation==NpcOccupation.Courier || work.Cargo>0)
        {
            var home=Catalog.Find(work.HomeStationId)!;
            work.TargetId=home.Id;work.Goal=HoldingPoint(ship,home);work.Phase=NpcPhase.Docking;
        }
        else if(work.Occupation==NpcOccupation.Miner)
        {
            var home=Catalog.Find(work.HomeStationId)!;
            var targets=Catalog.Asteroids.Where(a=>a.Id>=3000&&DepositRemaining(a.Id)>0)
                .OrderBy(a=>Vec3d.Distance(a.Position,home.Position)).Take(40).ToArray();
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
        ship.CruiseBraking=ship.Velocity.Length>ship.Hull.BoostSpeed;
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
                var delivered = UnloadFreight(ship, station);
                if (delivered > 0)
                {
                    Activity(ship,"arrival",$"Delivered {delivered} cargo units to {station.Name}.",$"Доставлено {delivered} ед. груза на {station.Name}.");
                    work.Deliveries++;
                    if(work.Occupation==NpcOccupation.Courier)work.HomeStationId=station.Id;
                }
                if (work.Cargo > 0)
                {
                    if (!RerouteCargo(ship, station)) WaitOutside(ship, 20);
                }
                else
                {
                    work.Phase=NpcPhase.Loading;
                    work.Until=TimeSeconds+4;
                }
                return;
            }
            if(work.Occupation==NpcOccupation.Courier)
                LoadFreight(ship, station);
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
                var holding = HoldingPoint(ship, station);
                if(!ApproachPilot.Arrived(ship,holding))
                {FlightModel.StepControlled(ship,ApproachPilot.Steer(ship,holding),dt);return;}
                if(!GrantDocking(ship,station)){FlightModel.StepControlled(ship,InputState.None with {Brake=true},dt);return;}
                ship.ApproachDestination = station.Position + Vec3d.Up * (station.Radius + 800);
            }
            if (!ship.ApproachDestination.Equals(ship.DockingPoint))
            {
                if (ApproachPilot.Arrived(ship, ship.ApproachDestination))
                    ship.ApproachDestination = ship.DockingPoint;
                else
                {
                    FlightModel.StepControlled(ship, ApproachPilot.Steer(ship, ship.ApproachDestination), dt);
                    return;
                }
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
                work.Cargo=Math.Min(ship.Hull.NpcLoad,DepositRemaining(body.Id));_deposits[body.Id]-=work.Cargo;
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
