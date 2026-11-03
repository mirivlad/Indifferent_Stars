using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Client;

public sealed record NavigationContact(uint Id, string Name, string Kind, double Distance);

public sealed partial class NavigationPanel : CanvasLayer
{
    private readonly string[] _groups = { "star", "planet", "asteroid", "station", "ship", "other" };
    private readonly Dictionary<string, bool> _collapsed = new() { ["asteroid"] = true };
    private Tree _tree = null!;
    private LineEdit _search = null!;
    private Button _toggle = null!;
    private VBoxContainer _content = null!;
    private VBoxContainer _route = null!;
    private Label _routeTitle = null!;
    private Label _orbitStatus = null!;
    private Control _info = null!;
    private Label _infoTitle = null!;
    private Label _infoBody = null!;
    private Button _infoClose = null!;
    private VBoxContainer _distanceForm = null!;
    private LineEdit _distance = null!;
    private OptionButton _direction = null!;
    private Label _distanceError = null!;
    private Button _distanceSubmit = null!;
    private Action<double, bool>? _submitDistance;
    private List<NavigationContact> _contacts = new();
    private readonly Dictionary<uint, TreeItem> _rows = new();
    private uint _selected;
    private string _roster = "";
    private bool _filtering;
    public event Action<uint, bool>? ContactSelected;
    public event Action<int, int>? RouteEdited;
    public bool IsTyping => _search.HasFocus();
    public bool IsModal => _info.Visible;

    public override void _Ready()
    {
        Layer = 13;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(root);
        var panel = new PanelContainer { Name = "NavigationPanel", MouseFilter = Control.MouseFilterEnum.Stop };
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.Position = new Vector2(-424, 24); panel.Size = new Vector2(400, 0); root.AddChild(panel);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat {
            BgColor = new Color(.008f,.018f,.03f,.94f), ContentMarginLeft=8, ContentMarginRight=8, ContentMarginTop=8, ContentMarginBottom=8 });
        var box = new VBoxContainer(); panel.AddChild(box);
        _toggle = new Button { Name="NavigationToggle", Text="▾ "+Locale.Text("navigation"), ToggleMode=true, ButtonPressed=true, FocusMode=Control.FocusModeEnum.None };
        box.AddChild(_toggle);
        _content = new VBoxContainer(); box.AddChild(_content);
        _toggle.Toggled += open => { _toggle.Text=(open?"▾ ":"▸ ")+Locale.Text("navigation"); _content.Visible = open; panel.Size = new Vector2(400,0); };
        _search = new LineEdit { Name="NavigationSearch", PlaceholderText=Locale.Text("navigation_search"), MaxLength=64 };
        _content.AddChild(_search);
        _search.TextChanged += _ => Rebuild();
        _search.GuiInput += e => { if(e is InputEventKey {Pressed:true,Keycode:Key.Escape or Key.Tab}) { _search.ReleaseFocus(); _search.AcceptEvent(); } };
        _tree = new Tree { Name="NavigationTree", Columns=2, HideRoot=true, SelectMode=Tree.SelectModeEnum.Row, AllowRmbSelect=true,
            FocusMode=Control.FocusModeEnum.None, CustomMinimumSize=new Vector2(0,260) };
        _tree.SetColumnExpand(0,true); _tree.SetColumnExpand(1,false); _tree.SetColumnCustomMinimumWidth(1,145);
        _tree.AddThemeFontSizeOverride("font_size",14);
        _content.AddChild(_tree);
        _tree.ItemSelected += () => Select(false);
        _tree.ItemMouseSelected += (_, button) => { if (button == (long)MouseButton.Right) Select(true); };
        _orbitStatus = new Label { Name="OrbitStatus", Visible=false, AutowrapMode=TextServer.AutowrapMode.WordSmart, CustomMinimumSize=new Vector2(380,0) }; _content.AddChild(_orbitStatus);
        _routeTitle = new Label { Visible=false }; _content.AddChild(_routeTitle);
        var scroll = new ScrollContainer { Name="RouteScroll", CustomMinimumSize=new Vector2(0,130), Visible=false };
        _content.AddChild(scroll);
        _route = new VBoxContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; scroll.AddChild(_route);
        _info = new Control { Name="ObjectInformation", Visible=false, MouseFilter=Control.MouseFilterEnum.Stop };
        _info.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.AddChild(_info);
        var shade = new ColorRect { Color=new Color(0,0,0,.6f), MouseFilter=Control.MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _info.AddChild(shade);
        var center = new CenterContainer { MouseFilter=Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _info.AddChild(center);
        var dialog = new PanelContainer { CustomMinimumSize=new Vector2(520,220) }; center.AddChild(dialog);
        dialog.AddThemeStyleboxOverride("panel",new StyleBoxFlat {BgColor=new Color(.02f,.035f,.055f),ContentMarginLeft=20,ContentMarginRight=20,ContentMarginTop=16,ContentMarginBottom=16});
        var details = new VBoxContainer();details.AddThemeConstantOverride("separation",14);dialog.AddChild(details);
        _infoTitle = new Label();_infoTitle.AddThemeFontSizeOverride("font_size",20);details.AddChild(_infoTitle);
        _infoBody = new Label {AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new Vector2(480,110)};details.AddChild(_infoBody);
        _distanceForm = new VBoxContainer { Visible=false }; details.AddChild(_distanceForm);
        _distance = new LineEdit { Name="NavigationDistance", MaxLength=40 }; _distanceForm.AddChild(_distance);
        _direction = new OptionButton { Name="OrbitDirection" }; _distanceForm.AddChild(_direction);
        _distanceError = new Label { Modulate=new Color(1,.5f,.3f) }; _distanceForm.AddChild(_distanceError);
        _distanceSubmit = new Button { Name="NavigationDistanceSubmit" }; _distanceForm.AddChild(_distanceSubmit);
        _distanceSubmit.Pressed += SubmitDistance;
        _distance.TextSubmitted += _ => SubmitDistance();
        _distance.TextChanged += _ => _distanceError.Text="";
        _infoClose = new Button {Text=Locale.Text("close"),SizeFlagsHorizontal=Control.SizeFlags.ShrinkEnd};details.AddChild(_infoClose);
        _infoClose.Pressed+=()=>_info.Visible=false;
        GetViewport().SizeChanged += ResizeList;
        ResizeList(); Rebuild();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_info.Visible && input is InputEventKey {Pressed:true,Keycode:Key.Escape})
        { _info.Visible=false; GetViewport().SetInputAsHandled(); }
    }

    private void ResizeList() => _tree.CustomMinimumSize = new Vector2(0, Mathf.Clamp(GetViewport().GetVisibleRect().Size.Y * .36f, 120, 360));
    private static string Group(string kind) => kind.ToLowerInvariant() switch { "player" or "npc" => "ship", "moon" => "planet", "star" or "planet" or "asteroid" or "station" => kind.ToLowerInvariant(), _ => "other" };
    public static string Distance(double metres)
    {
        metres = Math.Max(0,metres);
        string Format(string key, double value) => string.Format(CultureInfo.GetCultureInfo(Locale.Language),Locale.Text(key),value);
        if (metres >= 149_597_870_700 * .5) return Format("distance_au",metres/149_597_870_700);
        if (metres >= 1_000_000_000) return Format("distance_million_km",metres/1_000_000_000);
        if (metres >= 1_000_000) return Format("distance_thousand_km",metres/1_000_000);
        if (metres >= 1000) return Format("distance_km",metres/1000);
        return Format("distance_m",metres);
    }
    public void UpdateContacts(List<NavigationContact> contacts, uint selected)
    {
        _contacts=contacts.OrderBy(c=>c.Distance).ThenBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).ThenBy(c=>c.Id).ToList(); _selected=selected;
        var roster=string.Join("|",contacts.Select(c=>$"{c.Id}:{c.Name}:{c.Kind}"));
        if(roster!=_roster) { _roster=roster; Rebuild(); }
        var previousByGroup=new Dictionary<string,TreeItem>();
        foreach(var contact in _contacts)
        {
            if(!_rows.TryGetValue(contact.Id,out var row))continue;
            row.SetText(1,Distance(contact.Distance));
            var kind=Group(contact.Kind);
            if(previousByGroup.TryGetValue(kind,out var previous))
            { if(previous.GetNext()!=row)row.MoveAfter(previous); }
            else if(row.GetParent().GetFirstChild() is { } first&&first!=row)row.MoveBefore(first);
            previousByGroup[kind]=row;
            if(contact.Id==selected)row.Select(0);
        }
    }
    public void RefreshLanguage() { _toggle.Text=(_toggle.ButtonPressed?"▾ ":"▸ ")+Locale.Text("navigation"); _search.PlaceholderText=Locale.Text("navigation_search"); Rebuild(); }
    private void Rebuild()
    {
        var oldRoot=_tree.GetRoot();
        if(oldRoot!=null&&!_filtering)for(var group=oldRoot.GetFirstChild();group!=null;group=group.GetNext())_collapsed[group.GetMetadata(0).AsString()]=group.Collapsed;
        _filtering=_search.Text.Length>0;
        _tree.Clear(); _rows.Clear(); var root=_tree.CreateItem();
        foreach(var kind in _groups)
        {
            var group=_tree.CreateItem(root); group.SetText(0,Locale.Text("navigation_"+kind)); group.SetMetadata(0,kind);
            group.SetSelectable(0,false); group.SetSelectable(1,false); group.Collapsed=_search.Text.Length==0&&_collapsed.GetValueOrDefault(kind);
            foreach(var contact in _contacts.Where(c=>Group(c.Kind)==kind&&c.Name.Contains(_search.Text,StringComparison.OrdinalIgnoreCase)))
            {
                var row=_tree.CreateItem(group); row.SetText(0,contact.Name); row.SetText(1,Distance(contact.Distance));
                row.SetTextAlignment(1,HorizontalAlignment.Right); row.SetMetadata(0,(long)contact.Id); row.SetTooltipText(0,contact.Name);
                _rows[contact.Id]=row; if(contact.Id==_selected)row.Select(0);
            }
        }

    }
    private void Select(bool context)
    {
        var item=_tree.GetSelected(); if(item==null||item.GetMetadata(0).VariantType!=Variant.Type.Int)return;
        ContactSelected?.Invoke((uint)item.GetMetadata(0).AsInt64(),context);
    }
    public void ShowInformation(string title, string body)
    {
        _distanceForm.Visible=false; _submitDistance=null;
        _infoTitle.Text=title; _infoBody.Text=body; _infoClose.Text=Locale.Text("close");
        Input.MouseMode=Input.MouseModeEnum.Visible; _info.Visible=true; _infoClose.GrabFocus();
    }
    public void ShowDistanceRequest(string title, string initial, bool orbit, double minimum, Action<double,bool> submit)
    {
        ShowInformation(title, Locale.Text("navigation_distance_units")+"\n"+
            (orbit?Locale.Text("orbit_distance_help"):Locale.Text("warp_distance_help"))+"\n"+
            Locale.Text("navigation_distance_min")+": "+Distance(minimum));
        _submitDistance=submit; _distanceForm.Visible=true; _distance.Text=initial;
        _distanceError.Text=""; _direction.Visible=orbit; _direction.Clear();
        _direction.AddItem(Locale.Text("orbit_prograde")); _direction.AddItem(Locale.Text("orbit_retrograde"));
        _direction.Selected=0; _distanceSubmit.Text=Locale.Text("navigation_execute");
        _distance.SetMeta("minimum",minimum); _distance.GrabFocus(); _distance.SelectAll();
    }
    private void SubmitDistance()
    {
        if(!NavigationLimits.TryParseDistance(_distance.Text,out var metres) || metres<_distance.GetMeta("minimum").AsDouble())
        { _distanceError.Text=Locale.Text("navigation_distance_invalid"); return; }
        var callback=_submitDistance; _submitDistance=null; _info.Visible=false;
        callback?.Invoke(metres,_direction.Selected==1);
    }
    public void UpdateOrbit(string? target, double altitude, bool retrograde, bool established)
    {
        _orbitStatus.Visible=target is not null;
        _orbitStatus.Text=target is null?"":Locale.Text(established?"orbit_active":"orbit_transfer")+" · "+target+"\n"+
            Distance(altitude)+" · "+Locale.Text(retrograde?"orbit_retrograde":"orbit_prograde");
    }
    public void UpdateRoute(IReadOnlyList<string> names, bool active, int index, bool pending)
    {
        var signature=string.Join("|",names)+$":{active}:{index}:{pending}:{Locale.Text("route")}";
        if(_route.GetMeta("signature","").AsString()==signature)return;
        _route.SetMeta("signature",signature);
        foreach(var child in _route.GetChildren()){_route.RemoveChild(child);child.QueueFree();}
        _routeTitle.Visible=names.Count>index; _route.GetParent<Control>().Visible=names.Count>index;
        _routeTitle.Text=Locale.Text("route")+" · "+Locale.Text(pending?"route_pending":active?"route_active":"route_stopped");
        for(var i=index;i<names.Count;i++)
        {
            var position=i; var row=new HBoxContainer(); _route.AddChild(row);
            row.AddChild(new Label {Text=$"{i+1}. {names[i]}",TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,MouseFilter=Control.MouseFilterEnum.Ignore});
            Add("↑",-1,i==index); Add("↓",1,i==names.Count-1); Add("×",0,false);
            void Add(string text,int direction,bool disabled)
            {
                var button=new Button {Text=text,Disabled=disabled||pending,FocusMode=Control.FocusModeEnum.None,TooltipText=Locale.Text(direction==0?"route_remove":"route_reorder")};
                row.AddChild(button); button.Pressed+=()=>RouteEdited?.Invoke(position,direction);
            }
        }
    }
}
