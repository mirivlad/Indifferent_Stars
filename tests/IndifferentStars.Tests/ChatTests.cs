using IndifferentStars.Shared;
using IndifferentStars.Shared.Protocol;

namespace IndifferentStars.Tests;

public sealed class ChatTests
{
    [Theory]
    [InlineData(15000,true)]
    [InlineData(15000.01,false)]
    [InlineData(1000000,false)]
    public void LocalAudienceUsesThreeDimensionalDistance(double height,bool expected)
    {
        Assert.Equal(expected,RadioChannels.InAudience(RadioChannels.Local,Vec3d.Zero,new(0,height,0)));
        Assert.True(RadioChannels.InAudience(RadioChannels.System,Vec3d.Zero,new(0,height,0)));
        Assert.True(RadioChannels.InAudience(RadioChannels.General,Vec3d.Zero,new(0,height,0)));
        Assert.False(RadioChannels.InAudience(99,Vec3d.Zero,Vec3d.Zero));
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    [InlineData("control\tcharacter")]
    [InlineData("paragraph\u2028separator")]
    public void EmptyOrControlTextIsRejected(string text)=>Assert.False(RadioChannels.ValidText(text));
    [Fact]
    public void TextUsesUtf8ByteBudgetAndMarkupRemainsPlainText()
    {
        Assert.True(RadioChannels.ValidText(new string('я',256)));
        Assert.False(RadioChannels.ValidText(new string('я',257)));
        var line=new ChatSendMessage(1,RadioChannels.General,new string('я',256));
        Assert.True(ProtocolCodec.TryDecode(ProtocolCodec.Encode(line),out var parsed,out _));Assert.Equal(line,parsed);
        Assert.Throws<ArgumentException>(()=>ProtocolCodec.Encode(line with {Text=new string('я',257)}));
        Assert.True(RadioChannels.ValidText("[b]WASD[/b]"));
    }
}
