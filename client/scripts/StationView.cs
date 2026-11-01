using System;
using System.Collections.Generic;
using Godot;
using IndifferentStars.Shared.Protocol;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

/// <summary>
/// Docked-station interface: station information and an authoritative market.
/// It never changes credits or cargo locally; every button sends an intent and
/// waits for the server's <see cref="MarketState"/>.
/// </summary>
public sealed partial class StationView : CanvasLayer
{
    private static readonly Color Cyan = new(0.55f, 0.85f, 1.0f);
    private static readonly Color Green = new(0.45f, 0.92f, 0.65f);
    private static readonly Color Red = new(1.0f, 0.42f, 0.38f);
    private static readonly Color Dim = new(0.62f, 0.72f, 0.82f);
    private static readonly Color Body = new(0.80f, 0.87f, 0.94f);

    private sealed class Row
    {
        public required Label Name { get; init; }
        public required Label Stock { get; init; }
        public required Label Aboard { get; init; }
        public required Button Buy { get; init; }
        public required Button Sell { get; init; }
    }

    private readonly Dictionary<ushort, Row> _rows = new();
    private readonly List<Label> _staticLabels = new();

    private Label _header = null!;
    private Label _info = null!;
    private Label _comms = null!;
    private Label _services = null!;
    private Label _credits = null!;
    private Label _cargo = null!;
    private Label _tradeTitle = null!;
    private Label _status = null!;
    private Button _undock = null!;
    private MarketState? _market;
    private string _stationName = "";
    private string _statusText = "";
    private bool _statusSuccess;

    public event Action? UndockRequested;
    public event Action<ushort, int, bool>? TradeRequested;

    public StationView()
    {
        Name = "StationView";
        Layer = 15;
        Visible = false;
        Build();
        RefreshLanguage();
    }

    public bool HasMarket => _market is not null;

    public void SetDocked(bool docked, string stationName)
    {
        if (!docked)
        {
            Visible = false;
            _market = null;
            _statusText = "";
            _status.Visible = false;
            return;
        }

        _stationName = stationName;
        Visible = true;
        _header.Text = Locale.Format("station_docked", _stationName.ToUpperInvariant());
        if (_market is not null)
            UpdateMarket(_market);
        else
            _status.Text = Locale.Text("trade_pending");
    }

    public void UpdateMarket(MarketState state)
    {
        _market = state;
        _credits.Text = Locale.Format("trade_credits", state.Credits);
        _cargo.Text = Locale.Format("trade_cargo", state.CargoUsed, state.CargoCapacity);

        foreach (var commodity in CommodityCatalog.All)
        {
            if (!_rows.TryGetValue(commodity.Id, out var row))
                continue;

            var entry = FindEntry(state, commodity.Id);
            row.Stock.Text = entry.StationStock.ToString();
            row.Aboard.Text = entry.PlayerQuantity.ToString();
            row.Buy.Text = Locale.Format("trade_buy_price", commodity.BuyPrice);
            row.Sell.Text = Locale.Format("trade_sell_price", commodity.SellPrice);
            row.Buy.Disabled = entry.StationStock <= 0 ||
                               state.Credits < commodity.BuyPrice ||
                               state.CargoUsed >= state.CargoCapacity;
            row.Sell.Disabled = entry.PlayerQuantity <= 0;
        }

        if (string.IsNullOrEmpty(_statusText))
            _status.Visible = false;
    }

    public void ShowStatus(string text, bool success)
    {
        _statusText = text;
        _statusSuccess = success;
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", success ? Green : Red);
        _status.Visible = true;
    }

    public void RefreshLanguage()
    {
        _header.Text = Locale.Format("station_docked", _stationName.ToUpperInvariant());
        _info.Text = Locale.Text("station_info");
        _comms.Text = Locale.Text("station_comms");
        _services.Text = Locale.Text("station_services");
        _tradeTitle.Text = Locale.Text("trade_title");
        _undock.Text = Locale.Text("undock");

        foreach (var label in _staticLabels)
            label.Text = Locale.Text(label.GetMeta("locale_key").AsString());

        foreach (var commodity in CommodityCatalog.All)
        {
            if (_rows.TryGetValue(commodity.Id, out var row))
                row.Name.Text = Locale.Text("commodity_" + commodity.Key);
        }

        if (_market is not null)
            UpdateMarket(_market);
        else if (Visible)
            _status.Text = Locale.Text("trade_pending");

        if (!string.IsNullOrEmpty(_statusText))
            ShowStatus(_statusText, _statusSuccess);
    }

    private static MarketEntry FindEntry(MarketState state, ushort id)
    {
        foreach (var entry in state.Entries)
        {
            if (entry.CommodityId == id)
                return entry;
        }

        return new MarketEntry(id, 0, 0);
    }

    private void Build()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(820, 600) };
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Position = new Vector2(-410, -300);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.012f, 0.022f, 0.04f, 0.94f),
            BorderColor = new Color(0.35f, 0.62f, 0.8f, 0.5f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 22,
            ContentMarginRight = 22,
            ContentMarginTop = 18,
            ContentMarginBottom = 18,
        });
        root.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        _header = MakeLabel("", 24, Cyan);
        column.AddChild(_header);

        _info = AddStaticLabel(column, "station_info", 14, Dim);
        _comms = AddStaticLabel(column, "station_comms", 14, Dim);
        _services = AddStaticLabel(column, "station_services", 14, Dim);

        column.AddChild(new HSeparator());

        var accountRow = new HBoxContainer();
        accountRow.AddThemeConstantOverride("separation", 28);
        column.AddChild(accountRow);
        _credits = MakeLabel("", 16, Green);
        accountRow.AddChild(_credits);
        _cargo = MakeLabel("", 16, Cyan);
        accountRow.AddChild(_cargo);

        _tradeTitle = MakeLabel("", 18, Cyan);
        column.AddChild(_tradeTitle);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 280),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);

        var grid = new GridContainer { Columns = 5, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(grid);

        foreach (var title in new[] { "", "trade_stock", "trade_you", "", "" })
            grid.AddChild(MakeLabel(Locale.Text(title), 12, Dim));

        foreach (var commodity in CommodityCatalog.All)
        {
            var name = MakeLabel(Locale.Text("commodity_" + commodity.Key), 15, Body);
            name.CustomMinimumSize = new Vector2(230, 0);
            grid.AddChild(name);

            var stock = MakeLabel("0", 15, Body);
            stock.CustomMinimumSize = new Vector2(70, 0);
            stock.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(stock);

            var aboard = MakeLabel("0", 15, Body);
            aboard.CustomMinimumSize = new Vector2(70, 0);
            aboard.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(aboard);

            var id = commodity.Id;
            var buy = new Button { CustomMinimumSize = new Vector2(140, 0) };
            buy.Pressed += () => TradeRequested?.Invoke(id, 1, true);
            grid.AddChild(buy);

            var sell = new Button { CustomMinimumSize = new Vector2(140, 0) };
            sell.Pressed += () => TradeRequested?.Invoke(id, 1, false);
            grid.AddChild(sell);

            _rows[id] = new Row { Name = name, Stock = stock, Aboard = aboard, Buy = buy, Sell = sell };
        }

        _status = MakeLabel("", 14, Dim);
        column.AddChild(_status);

        _undock = new Button { Text = Locale.Text("undock"), CustomMinimumSize = new Vector2(0, 34) };
        _undock.Pressed += () => UndockRequested?.Invoke();
        column.AddChild(_undock);
    }

    private Label AddStaticLabel(VBoxContainer parent, string key, int size, Color color)
    {
        var label = MakeLabel(Locale.Text(key), size, color);
        label.SetMeta("locale_key", key);
        _staticLabels.Add(label);
        parent.AddChild(label);
        return label;
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}