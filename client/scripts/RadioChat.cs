using System;
using System.Collections.Generic;
using Godot;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Client;

public sealed partial class RadioChat : CanvasLayer
{
    private sealed record Line(DateTimeOffset Time,uint SenderId,string Sender,string Text,bool Npc);
    public const ushort Notifications = 0;
    private readonly ushort[] _channels={RadioChannels.Local,RadioChannels.System,RadioChannels.General,Notifications};
    private readonly Dictionary<ushort,List<Line>> _history=new();
    private readonly Dictionary<ushort,int> _unread=new();
    private TabBar _tabs=null!;
    private RichTextLabel _log=null!;
    private LineEdit _input=null!;
    private Label _heading=null!;
    private Button _send=null!;
    public event Action<ushort,string>? SendRequested;
    public event Action<bool>? TypingChanged;
    public bool IsTyping=>_input?.HasFocus()==true;
    public ushort SelectedChannel=>_channels[Math.Max(0,_tabs.CurrentTab)];
    public override void _Ready()
    {
        Name="RadioChat";Layer=12;
        var root=new Control {MouseFilter=Control.MouseFilterEnum.Ignore};root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);AddChild(root);
        var panel=new PanelContainer {Name="RadioPanel",MouseFilter=Control.MouseFilterEnum.Stop};
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);panel.Position=new Vector2(24,-268);panel.Size=new Vector2(550,244);root.AddChild(panel);
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat {BgColor=new Color(.008f,.018f,.03f,.91f),BorderColor=new Color(.14f,.31f,.40f,.8f),
            BorderWidthLeft=1,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1,ContentMarginLeft=10,ContentMarginRight=10,ContentMarginTop=7,ContentMarginBottom=8});
        var box=new VBoxContainer();box.AddThemeConstantOverride("separation",4);panel.AddChild(box);
        _heading=new Label();_heading.AddThemeFontSizeOverride("font_size",13);_heading.Modulate=new Color(.55f,.8f,.9f);box.AddChild(_heading);
        _tabs=new TabBar {Name="RadioTabs",FocusMode=Control.FocusModeEnum.None};box.AddChild(_tabs);
        foreach(var id in _channels){_history[id]=new();_unread[id]=0;_tabs.AddTab(Title(id));}
        _log=new RichTextLabel {Name="RadioHistory",BbcodeEnabled=false,ScrollFollowing=false,SelectionEnabled=true,FocusMode=Control.FocusModeEnum.None,
            SizeFlagsVertical=Control.SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(0,110),MouseFilter=Control.MouseFilterEnum.Stop};
        _log.AddThemeFontSizeOverride("normal_font_size",14);box.AddChild(_log);
        var row=new HBoxContainer();box.AddChild(row);
        _input=new LineEdit {Name="ChatInput",MaxLength=256,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};row.AddChild(_input);
        _send=new Button {FocusMode=Control.FocusModeEnum.None};row.AddChild(_send);
        _input.FocusEntered+=()=>TypingChanged?.Invoke(true);_input.FocusExited+=()=>TypingChanged?.Invoke(false);
        _input.TextSubmitted+=_=>Submit();_send.Pressed+=Submit;
        _input.GuiInput+=input=>{if(input is InputEventKey {Pressed:true,Keycode:Key.Escape}){_input.AcceptEvent();EndTyping();}};
        _tabs.TabChanged+=_=>{_unread[SelectedChannel]=0;RefreshLanguage();Render(true);_input.Editable=SelectedChannel!=Notifications;_send.Disabled=SelectedChannel==Notifications;EndTyping();};
        RefreshLanguage();
    }
    private static string Title(ushort id)=>Locale.Text("chat_channel_"+id);
    public void RefreshLanguage()
    {
        _heading.Text=Locale.Text("radio_heading");_input.PlaceholderText=Locale.Text("chat_placeholder");_send.Text=Locale.Text("chat_send");
        for(var i=0;i<_channels.Length;i++)_tabs.SetTabTitle(i,Title(_channels[i])+(_unread[_channels[i]]>0?$" ({_unread[_channels[i]]})":""));
    }
    public void BeginTyping(){if(SelectedChannel==Notifications)_tabs.CurrentTab=0;_input.GrabFocus();}
    public void EndTyping()=>_input.ReleaseFocus();
    private void Submit()
    {
        if(SelectedChannel==Notifications)return;
        var text=_input.Text.Trim();
        if(text.Length==0){EndTyping();return;}
        if(!RadioChannels.ValidText(text)){Notice(Notifications,Locale.Text("chat_error_2"));return;}
        _input.Text="";SendRequested?.Invoke(SelectedChannel,text);EndTyping();
    }
    public void Receive(ChatLineMessage line)=>Append(line.ChannelId,DateTimeOffset.FromUnixTimeMilliseconds(line.Timestamp),line.Sender,line.Text,false,line.SenderId);
    public void Radio(string sender,string text,uint senderId=0)=>Append(RadioChannels.Local,DateTimeOffset.Now,sender,text,true,senderId);
    public void Notice(ushort channel,string text)=>Append(channel,DateTimeOffset.Now,Locale.Text(channel==Notifications?"notifications_sender":"radio_heading"),text,true);
    private void Append(ushort channel,DateTimeOffset time,string sender,string text,bool npc,uint senderId=0)
    {
        if(!_history.TryGetValue(channel,out var history))return;
        history.Add(new Line(time,senderId,sender,text,npc));if(history.Count>100)history.RemoveAt(0);
        if(channel==SelectedChannel)Render();else _unread[channel]=Math.Min(99,_unread[channel]+1);
        RefreshLanguage();
    }
    private void Render(bool forceBottom=false)
    {
        var scroll=_log.GetVScrollBar();var previous=scroll.Value;var follow=forceBottom||previous>=scroll.MaxValue-scroll.Page-2;
        _log.Clear();
        foreach(var line in _history[SelectedChannel])
        {
            _log.PushColor(new Color(.43f,.56f,.65f));_log.AddText(line.Time.ToLocalTime().ToString("HH:mm")+" ");_log.Pop();
            _log.PushColor(line.Npc?new Color(.95f,.72f,.4f):new Color(.5f,.84f,.95f));_log.AddText(line.Sender+": ");_log.Pop();
            _log.AddText(line.Text+"\n");
        }
        Callable.From(()=>{if(GodotObject.IsInstanceValid(_log))scroll.Value=follow?scroll.MaxValue:previous;}).CallDeferred();
    }
}
