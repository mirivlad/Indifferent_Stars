using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

public sealed partial class StationView
{
    private enum YardEntryKind { Vessel, HullOffer, ModuleStock, ModuleOffer, Slot }
    private readonly record struct YardEntry(YardEntryKind Kind, uint Id);
    private readonly List<YardEntry> _yardEntries = new();
    private ShipyardState? _yard;
    private TabBar _stationTabs = null!;
    private TabBar _yardTabs = null!;
    private VBoxContainer _marketPage = null!;
    private VBoxContainer _yardPage = null!;
    private ItemList _yardList = null!;
    private Label _yardDetail = null!;
    private Button _yardPrimary = null!;
    private Button _yardSell = null!;
    private Button _yardRepair = null!;
    private Button _yardRearm = null!;
    private OptionButton _fitChoices = null!;
    private (ShipyardAction Action, ushort Catalog, uint Vessel, byte Slot)? _yardPrimaryOrder;
    private (ShipyardAction Action, ushort Catalog, uint Vessel, byte Slot)? _yardSellOrder;

    public event Action<ShipyardAction, ushort, uint, byte>? YardRequested;
    public Func<uint, string>? YardStationName { get; set; }

    private void BuildStationTabs(VBoxContainer parent)
    {
        _stationTabs = new TabBar { Name = "StationTabs" };
        parent.AddChild(_stationTabs);
        _stationTabs.AddTab("");
        _stationTabs.AddTab("");
        _marketPage = new VBoxContainer();
        parent.AddChild(_marketPage);
        _yardPage = new VBoxContainer { Visible = false, CustomMinimumSize = new Vector2(0, 318) };
        parent.AddChild(_yardPage);
        _stationTabs.TabChanged += index =>
        {
            _marketPage.Visible = index == 0;
            _yardPage.Visible = index == 1;
            if (index == 1)
            {
                YardRequested?.Invoke(ShipyardAction.Inspect, 0, 0, 0);
                RenderYard();
            }
        };

        _yardTabs = new TabBar { Name = "YardSections" };
        _yardPage.AddChild(_yardTabs);
        for (var i = 0; i < 3; i++) _yardTabs.AddTab("");
        _yardTabs.TabChanged += _ => RenderYard(preserveSelection: false);

        var center = new HBoxContainer();
        center.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _yardPage.AddChild(center);
        _yardList = new ItemList { Name = "YardInventory", CustomMinimumSize = new Vector2(310, 230) };
        center.AddChild(_yardList);
        _yardList.ItemSelected += _ => RenderYardDetail();
        var details = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        center.AddChild(details);
        _yardDetail = MakeLabel("", 15, Body);
        _yardDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _yardDetail.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        details.AddChild(_yardDetail);
        _fitChoices = new OptionButton { Name = "YardFitChoices", Visible = false };
        _fitChoices.ItemSelected += _ => SetInstallOrder();
        details.AddChild(_fitChoices);
        _yardPrimary = new Button { Name = "YardPrimary", Visible = false };
        _yardPrimary.Pressed += () => Request(_yardPrimaryOrder);
        details.AddChild(_yardPrimary);
        _yardSell = new Button { Name = "YardSell", Visible = false };
        _yardSell.Pressed += () => Request(_yardSellOrder);
        details.AddChild(_yardSell);

        var service = new HBoxContainer();
        _yardPage.AddChild(service);
        _yardRepair = new Button { Name = "YardRepair", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _yardRepair.Pressed += () => YardRequested?.Invoke(ShipyardAction.Repair, 0, 0, 0);
        service.AddChild(_yardRepair);
        _yardRearm = new Button { Name = "YardRearm", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _yardRearm.Pressed += () => YardRequested?.Invoke(ShipyardAction.Rearm, 0, 0, 0);
        service.AddChild(_yardRearm);
    }

    private void Request((ShipyardAction Action, ushort Catalog, uint Vessel, byte Slot)? order)
    {
        if (order is { } request) YardRequested?.Invoke(request.Action, request.Catalog, request.Vessel, request.Slot);
    }

    private void RefreshShipyardLanguage()
    {
        _stationTabs.SetTabTitle(0, Locale.Text("trade_title"));
        _stationTabs.SetTabTitle(1, Locale.Text("yard_title"));
        _yardTabs.SetTabTitle(0, Locale.Text("yard_hulls"));
        _yardTabs.SetTabTitle(1, Locale.Text("yard_modules"));
        _yardTabs.SetTabTitle(2, Locale.Text("yard_fitting"));
        RenderYard();
    }

    private void ResetShipyard()
    {
        _yard = null;
        _stationTabs.CurrentTab = 0;
    }

    public void UpdateShipyard(ShipyardReplyMessage reply)
    {
        if (_stationId == 0 || reply.State is { } state && state.StationId != _stationId) return;
        if (reply.State is { } fresh) _yard = fresh;
        if (reply.Action != ShipyardAction.Inspect || reply.Error != ShipyardError.None)
            ShowStatus(Locale.Text("yard_reply_" + reply.Error.ToString().ToLowerInvariant()), reply.Error == ShipyardError.None);
        RenderYard();
    }

    private void RenderYard(bool preserveSelection = true)
    {
        var selection = preserveSelection ? SelectedEntry() : null;
        _yardList.Clear();
        _yardEntries.Clear();
        if (_yard is { } yard)
        {
            if (_yardTabs.CurrentTab == 0)
            {
                foreach (var vessel in yard.Vessels)
                {
                    var name = HullCatalog.Get(vessel.HullId).Name;
                    AddEntry(YardEntryKind.Vessel, vessel.Serial, vessel.Serial == yard.ActiveHull
                        ? Locale.Format("yard_active", name)
                        : Locale.Format("yard_owned", name, YardStationName?.Invoke(vessel.StationId) ?? $"#{vessel.StationId}"));
                }
                foreach (var offer in yard.HullOffers)
                    AddEntry(YardEntryKind.HullOffer, offer.CatalogId, $"  {HullCatalog.Get(offer.CatalogId).Name} · {offer.Cost}");
            }
            else if (_yardTabs.CurrentTab == 1)
            {
                foreach (var row in yard.Modules.Where(row => row.StationId == yard.StationId))
                    AddEntry(YardEntryKind.ModuleStock, row.ModuleId, Locale.Format("yard_stock_module", ModuleName(row.ModuleId), row.Quantity));
                foreach (var row in yard.ModuleOffers)
                    AddEntry(YardEntryKind.ModuleOffer, row.CatalogId, $"  {ModuleName(row.CatalogId)} · {row.Cost}");
            }
            else
            {
                var vessel = yard.Vessels.Single(item => item.Serial == yard.ActiveHull);
                var hull = HullCatalog.Get(vessel.HullId);
                for (byte i = 0; i < ShipyardCatalog.SlotCount(hull); i++)
                {
                    var category = ShipyardCatalog.SlotCategory(hull, i);
                    var fitted = vessel.Modules.SingleOrDefault(item => item.Slot == i);
                    var item = fitted.ModuleId > 0 ? ModuleName(fitted.ModuleId) : Locale.Text("yard_empty_slot");
                    if (fitted.ModuleId == 0 && i >= hull.EquipmentSlots.Length)
                        item = Locale.Text("weapon_" + (byte)hull.DefaultWeapons[i - hull.EquipmentSlots.Length]);
                    AddEntry(YardEntryKind.Slot, i, $"{i + 1}. {CategoryName(category)} · {item}");
                }
            }
        }
        if (_yardEntries.Count > 0)
        {
            var index = selection is { } entry ? _yardEntries.IndexOf(entry) : -1;
            _yardList.Select(Math.Max(0, index));
        }
        _yardRepair.Text = Locale.Format("yard_service_repair", _yard?.RepairPrice ?? 0);
        _yardRearm.Text = Locale.Format("yard_service_rearm", _yard?.RearmPrice ?? 0);
        _yardRepair.Disabled = _yard is not { RepairPrice: > 0 } || _yard.RepairPrice > _yard.Credits;
        _yardRearm.Disabled = _yard is not { RearmPrice: > 0 } || _yard.RearmPrice > _yard.Credits;
        RenderYardDetail();
    }

    private void AddEntry(YardEntryKind kind, uint id, string text)
    {
        _yardEntries.Add(new YardEntry(kind, id));
        _yardList.AddItem(text);
    }

    private YardEntry? SelectedEntry()
    {
        var selected = _yardList.GetSelectedItems();
        return selected.Length == 1 && selected[0] < _yardEntries.Count ? _yardEntries[selected[0]] : null;
    }

    private void RenderYardDetail()
    {
        _yardPrimaryOrder = _yardSellOrder = null;
        _yardPrimary.Visible = _yardSell.Visible = _fitChoices.Visible = false;
        _yardDetail.Text = Locale.Text(_yard is null ? "yard_loading" : "yard_empty_list");
        if (_yard is not { } yard || SelectedEntry() is not { } item) return;
        var active = yard.Vessels.Single(vessel => vessel.Serial == yard.ActiveHull);
        var activeHull = HullCatalog.Get(active.HullId);
        switch (item.Kind)
        {
            case YardEntryKind.Vessel:
                var vessel = yard.Vessels.Single(candidate => candidate.Serial == item.Id);
                var hull = HullCatalog.Get(vessel.HullId);
                Compare(hull, ShipyardCatalog.Calculate(hull, vessel.Modules), activeHull, active.Modules);
                _yardDetail.Text += "\n" + Locale.Format("yard_hull_condition", vessel.Integrity);
                if (vessel.Serial != active.Serial && vessel.StationId == yard.StationId)
                {
                    Primary(Locale.Text("yard_activate"), (ShipyardAction.ActivateHull, 0, vessel.Serial, 0));
                    Sell(Locale.Format("yard_sell", vessel.Resale), (ShipyardAction.SellHull, 0, vessel.Serial, 0));
                }
                break;
            case YardEntryKind.HullOffer:
                var price = yard.HullOffers.Single(row => row.CatalogId == item.Id);
                var offered = HullCatalog.Get(price.CatalogId);
                Compare(offered, ShipyardCatalog.Calculate(offered, Array.Empty<FittedModule>()), activeHull, active.Modules);
                Primary(Locale.Format("yard_buy", price.Cost), (ShipyardAction.BuyHull, price.CatalogId, 0, 0),
                    !price.InStock || price.Cost > yard.Credits);
                break;
            case YardEntryKind.ModuleStock:
            case YardEntryKind.ModuleOffer:
                var module = ShipyardCatalog.Module((ushort)item.Id)!;
                _yardDetail.Text = Describe(module);
                if (item.Kind == YardEntryKind.ModuleStock)
                    Sell(Locale.Format("yard_sell", module.Price / 2), (ShipyardAction.SellModule, module.Id, 0, 0));
                else
                {
                    var offer = yard.ModuleOffers.Single(row => row.CatalogId == module.Id);
                    Primary(Locale.Format("yard_buy", offer.Cost), (ShipyardAction.BuyModule, module.Id, 0, 0),
                        !offer.InStock || offer.Cost > yard.Credits);
                }
                break;
            case YardEntryKind.Slot:
                var slot = (byte)item.Id;
                var fitted = active.Modules.SingleOrDefault(module => module.Slot == slot);
                _yardDetail.Text = FitLimits(ShipyardCatalog.Calculate(activeHull, active.Modules), activeHull);
                if (fitted.ModuleId != 0)
                {
                    _yardDetail.Text += "\n\n" + Describe(ShipyardCatalog.Module(fitted.ModuleId)!);
                    Primary(Locale.Text("yard_remove"), (ShipyardAction.RemoveModule, 0, active.Serial, slot));
                }
                else
                {
                    var category = ShipyardCatalog.SlotCategory(activeHull, slot);
                    _fitChoices.Clear();
                    foreach (var row in yard.Modules.Where(row => row.StationId == yard.StationId &&
                                 ShipyardCatalog.Module(row.ModuleId)?.Category == category))
                        _fitChoices.AddItem(ModuleName(row.ModuleId), row.ModuleId);
                    if (_fitChoices.ItemCount > 0)
                    {
                        _fitChoices.Visible = true;
                        _fitChoices.Select(0);
                        SetInstallOrder();
                    }
                    else _yardDetail.Text += "\n\n" + Locale.Text("yard_no_compatible_module");
                }
                break;
        }
    }

    private void SetInstallOrder()
    {
        if (_yard is not { } yard || SelectedEntry() is not { Kind: YardEntryKind.Slot } entry || _fitChoices.Selected < 0) return;
        var selected = (ushort)_fitChoices.GetItemId(_fitChoices.Selected);
        var active = yard.Vessels.Single(vessel => vessel.Serial == yard.ActiveHull);
        var hull = HullCatalog.Get(active.HullId);
        var modules = active.Modules.Append(new FittedModule((byte)entry.Id, selected)).ToArray();
        var next = ShipyardCatalog.Calculate(hull, modules);
        _yardDetail.Text = Describe(ShipyardCatalog.Module(selected)!) + "\n\n" + FitLimits(next, hull);
        Primary(Locale.Text("yard_install"), (ShipyardAction.FitModule, selected, active.Serial, (byte)entry.Id),
            !ShipyardCatalog.ValidFit(hull, modules));
    }

    private void Compare(HullDefinition selected, ShipFit fit, HullDefinition active, FittedModule[] installed)
    {
        var current = ShipyardCatalog.Calculate(active, installed);
        _yardDetail.Text = Locale.Format("yard_compare", selected.Name, active.Name, fit.CargoCapacity, current.CargoCapacity,
            selected.MaxSpeed, active.MaxSpeed, fit.AvailableEnergy, current.AvailableEnergy, fit.Shield, current.Shield,
            selected.HullIntegrity, active.HullIntegrity, fit.ScanRange / 1000, current.ScanRange / 1000) +
            "\n\n" + FitLimits(fit, selected);
    }

    private static string FitLimits(ShipFit fit, HullDefinition hull) =>
        Locale.Format("yard_limits", fit.MassKg / 1000, hull.MassKg * .00128, fit.Draw, fit.Reactor * .42);

    private static string Describe(ModuleDefinition module) =>
        Locale.Format("yard_module_description", ModuleName(module.Id), CategoryName(module.Category), module.MassKg, module.PowerDraw) +
        "\n" + (module.Category == ModuleCategory.Weapon ? Locale.Text("weapon_" + (byte)module.Weapon) :
            Locale.Format("yard_bonus_" + module.Category.ToString().ToLowerInvariant(), module.Bonus));

    private static string ModuleName(ushort id) => Locale.Text("equipment_" + ShipyardCatalog.Module(id)!.Key);
    private static string CategoryName(ModuleCategory? category) => Locale.Text("yard_category_" + category?.ToString().ToLowerInvariant());

    private void Primary(string label, (ShipyardAction, ushort, uint, byte) order, bool disabled = false)
    {
        _yardPrimary.Text = label;
        _yardPrimary.Visible = true;
        _yardPrimary.Disabled = disabled;
        _yardPrimaryOrder = order;
    }

    private void Sell(string label, (ShipyardAction, ushort, uint, byte) order)
    {
        _yardSell.Text = label;
        _yardSell.Visible = true;
        _yardSellOrder = order;
    }
}
