using System;
using Godot;

namespace IndifferentStars.Client;

public enum ImpactSoundKind { Hit, Scrape, Metal }

/// <summary>Original deterministic PCM, shared by local and positional impact players.</summary>
public static class ImpactSound
{
    private static readonly AudioStreamWav?[,] Samples=new AudioStreamWav?[3,4];
    private static int _users;
    public static void Retain()=>_users++;
    public static void Release()
    {
        if(--_users>0)return;
        _users=0;
        for(var i=0;i<3;i++)for(var j=0;j<4;j++){Samples[i,j]?.Dispose();Samples[i,j]=null;}
    }
    public static AudioStreamWav Sample(ImpactSoundKind kind,int level)
    {
        level=Math.Clamp(level,0,3);
        if(Samples[(int)kind,level] is { } cached)return cached;
        const int rate=22050;
        var seconds=kind switch {ImpactSoundKind.Hit=>.6,ImpactSoundKind.Scrape=>.85,_=>1.4};
        var count=(int)(rate*seconds);var bytes=new byte[count*2];
        var random=new Random(4731+(int)kind*173+level);double filtered=0;
        for(var i=0;i<count;i++)
        {
            var t=(double)i/rate;var noise=random.NextDouble()*2-1;filtered=filtered*.87+noise*.13;
            var sample=kind switch
            {
                ImpactSoundKind.Hit=>noise*Math.Exp(-t*55)*.55+Math.Sin(Math.Tau*63*t)*Math.Exp(-t*10)*.45+
                    Math.Sin(Math.Tau*181*t)*Math.Exp(-t*16)*.16,
                ImpactSoundKind.Scrape=>(noise-filtered)*.30*(.6+.4*Math.Sin(t*127)*Math.Sin(t*73))+
                    Math.Sin(Math.Tau*697*t+Math.Sin(t*39)*3)*.13,
                _=>(Math.Sin(Math.Tau*(151*t-24*t*t))+.28*Math.Sin(Math.Tau*219.3*t)+
                    .14*Math.Sin(Math.Tau*307.7*t))*Math.Exp(-t*2.5)*.26+filtered*.14,
            };
            var envelope=(1-Math.Exp(-t*500))*Math.Pow(Math.Max(0,1-t/seconds),kind==ImpactSoundKind.Scrape?1.2:.4);
            if(kind==ImpactSoundKind.Metal)envelope*=1-Math.Exp(-t*20);
            var value=(short)(Math.Clamp(sample*envelope*(.65+level*.15),-.95,.95)*32767);
            bytes[i*2]=(byte)value;bytes[i*2+1]=(byte)(value>>8);
        }
        var result=new AudioStreamWav {Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=rate,Data=bytes,ResourceName=$"Impact_{kind}_{level}"};
        Samples[(int)kind,level]=result;return result;
    }
}
