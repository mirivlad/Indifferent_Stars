using System.IO;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Shared.Protocol;

public static partial class ProtocolCodec
{
    private static void WriteShipyard(BinaryWriter writer, ShipyardState yard)
    {
        writer.Write(yard.StationId); writer.Write(yard.Credits); writer.Write(yard.ActiveHull);
        writer.Write(yard.RepairPrice); writer.Write(yard.RearmPrice);
        WriteCount(yard.Vessels.Length, ShipyardState.MaxVessels);
        foreach (var vessel in yard.Vessels)
        {
            writer.Write(vessel.Serial); writer.Write(vessel.HullId); writer.Write(vessel.StationId); writer.Write(vessel.Integrity);
            writer.Write(vessel.Resale);
            WriteCount(vessel.Modules.Length, ShipyardState.MaxFitted);
            foreach (var module in vessel.Modules)
            {
                writer.Write(module.Slot);
                writer.Write(module.ModuleId);
            }
        }
        WriteCount(yard.Modules.Length, ShipyardState.MaxModuleRows);
        foreach (var row in yard.Modules)
        {
            writer.Write(row.StationId); writer.Write(row.ModuleId); writer.Write(row.Quantity);
        }
        WriteOffers(yard.HullOffers);
        WriteOffers(yard.ModuleOffers);

        void WriteOffers(YardPrice[] offers)
        {
            WriteCount(offers.Length, ShipyardState.MaxOffers);
            foreach (var item in offers)
            {
                writer.Write(item.CatalogId); writer.Write(item.Cost); writer.Write(item.InStock);
            }
        }

        void WriteCount(int count, int max)
        {
            if (count > max) throw new ArgumentException("Too many yard rows.");
            writer.Write((byte)count);
        }
    }

    private static ShipyardAction ReadShipyardAction(BinaryReader reader)
    {
        var action = (ShipyardAction)reader.ReadByte();
        if (!Enum.IsDefined(action)) throw new InvalidDataException("Invalid shipyard action.");
        return action;
    }

    private static ShipyardReplyMessage ReadShipyardReply(BinaryReader reader)
    {
        var id = reader.ReadUInt32();
        var action = ReadShipyardAction(reader);
        var error = (ShipyardError)reader.ReadByte();
        if (!Enum.IsDefined(error)) throw new InvalidDataException("Invalid shipyard reply.");
        if (!reader.ReadBoolean())
        {
            if (error == ShipyardError.None) throw new InvalidDataException("Missing shipyard state.");
            return new(id, action, error, null);
        }
        var station = reader.ReadUInt32(); var credits = reader.ReadInt32(); var active = reader.ReadUInt32();
        var repair = reader.ReadInt32(); var rearm = reader.ReadInt32();
        if (station == 0 || active == 0 || credits < 0 || repair < 0 || rearm < 0)
            throw new InvalidDataException("Invalid shipyard account.");

        var vessels = new HangarVessel[ReadCount(ShipyardState.MaxVessels)];
        for (var i = 0; i < vessels.Length; i++)
        {
            var serial = reader.ReadUInt32(); var hull = reader.ReadUInt16(); var port = reader.ReadUInt32();
            var integrity = reader.ReadSingle();
            var resale = reader.ReadInt32();
            if (serial == 0 || port == 0 || resale < 0 || hull == 0 || !HullCatalog.Contains(hull) ||
                !float.IsFinite(integrity) || integrity < 0 || integrity > 1)
                throw new InvalidDataException("Invalid stored hull.");
            var modules = new FittedModule[ReadCount(ShipyardState.MaxFitted)];
            for (var slot = 0; slot < modules.Length; slot++)
                modules[slot] = new FittedModule(reader.ReadByte(), reader.ReadUInt16());
            if (!ShipyardCatalog.ValidFit(HullCatalog.Get(hull), modules)) throw new InvalidDataException("Invalid stored fit.");
            vessels[i] = new(serial, hull, port, integrity, modules, resale);
        }
        if (!vessels.Any(vessel => vessel.Serial == active) ||
            vessels.Select(vessel => vessel.Serial).Distinct().Count() != vessels.Length)
            throw new InvalidDataException("Invalid hangar vessel list.");
        var storage = new HangarModule[ReadCount(ShipyardState.MaxModuleRows)];
        for (var i = 0; i < storage.Length; i++)
        {
            var row = new HangarModule(reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt16());
            if (row.StationId == 0 || row.Quantity is < 1 or > 99 || ShipyardCatalog.Module(row.ModuleId) is null)
                throw new InvalidDataException("Invalid module storage.");
            storage[i] = row;
        }
        return new(id, action, error, new(station, credits, active, vessels, storage,
            ReadOffers(modules: false), ReadOffers(modules: true), repair, rearm));

        YardPrice[] ReadOffers(bool modules)
        {
            var offers = new YardPrice[ReadCount(ShipyardState.MaxOffers)];
            for (var i = 0; i < offers.Length; i++)
            {
                var item = new YardPrice(reader.ReadUInt16(), reader.ReadInt32(), reader.ReadBoolean());
                if (item.CatalogId == 0 || item.Cost < 0 || (modules
                        ? ShipyardCatalog.Module(item.CatalogId) is null : !HullCatalog.Contains(item.CatalogId)))
                    throw new InvalidDataException("Invalid shipyard offer.");
                offers[i] = item;
            }
            return offers;
        }

        int ReadCount(int max)
        {
            var count = reader.ReadByte();
            if (count > max) throw new InvalidDataException("Too many yard rows.");
            return count;
        }
    }
}
