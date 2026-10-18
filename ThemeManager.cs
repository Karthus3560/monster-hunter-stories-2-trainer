using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace RidersToolkit;

internal enum StudioTheme { NightCamp, White }

internal static class ThemeManager
{
    internal static StudioTheme Current {get;private set;}=StudioTheme.White;
    static string SettingsPath=>Path.Combine(Storage.Root,"appearance.json");
    internal static readonly string[] Keys=["Canvas","Panel","Raised","Line","Ink","Muted","Mint","Gold","Red","Sidebar","Selected","RoseSurface","PeachSurface","DangerSurface","DangerLine","OnAccent","OverlaySurface","InputSurface","TextSelection","ScrollThumb","SwitchOff","SwitchThumb","SliderBorder","SliderDrag","PixelHighlight","PixelShadow","SideInk","SideMuted","SideRaised","SideSelected","SideLine","SideDangerSurface","SideDangerInk","Caption","CaptionInk"];
    static readonly string[] White=["#F5F8FC","#FFFFFF","#E9F0F7","#CAD7E3","#172E45","#50657B","#126D79","#A44733","#B32E45","#142C43","#DCF3F2","#FFE5DA","#E5F4F5","#FFE9EB","#E5ACB6","#FFFFFF","#FFFFFF","#F8FBFF","#B2DCE1","#8CA7BD","#B5C5D2","#FFFFFF","#155A6C","#2F8991","#FFFFFF","#8FA7BC","#F2F6FC","#A7BBD0","#203D57","#2A5266","#35526C","#422D3A","#FFB5C4","#142C43","#F2F6FC"];
    static readonly string[] Night=["#111E30","#192C41","#223A52","#34516A","#EEF5FC","#A9BED0","#82D9CE","#FFB49A","#FFADB9","#0C1726","#244B53","#493B42","#1B414A","#462936","#A75E70","#0E2B34","#192C41","#122338","#356373","#54768F","#496078","#DDEAF4","#B8F6EB","#A6ECDD","#42637D","#071320","#F2F6FC","#A7BBD0","#142C43","#244B53","#2F455F","#422D3A","#FFB5C4","#0C1726","#EEF5FC"];
    internal static StudioTheme Read(string path)
    {
        try{return Parse(JsonSerializer.Deserialize<Preference>(File.ReadAllText(path))?.Theme);}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){return StudioTheme.White;}
    }
    internal static void Save(string path,StudioTheme theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(new Preference(theme switch{StudioTheme.White=>"white",StudioTheme.NightCamp=>"night-camp",_=>"white"})));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static StudioTheme Parse(string? value)=>value=="night-camp"?StudioTheme.NightCamp:StudioTheme.White;
    internal static StudioTheme Next(StudioTheme value)=>value==StudioTheme.White?StudioTheme.NightCamp:StudioTheme.White;
    internal static string Label=>Current==StudioTheme.White?"Stories light":"Moonlit hunt";
    internal static void Initialize(string? preview=null)=>Apply(preview is not null?Parse(preview):Read(SettingsPath));
    internal static void Change(StudioTheme theme){Save(SettingsPath,theme);Apply(theme);}
    internal static void Apply(StudioTheme theme)
    {
        Current=theme;var resources=Application.Current.Resources;var values=theme==StudioTheme.White?White:Night;
        for(int i=0;i<Keys.Length;i++)resources[Keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));
        resources["MascotSurface"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[28]));
        foreach(Window window in Application.Current.Windows)WindowLayout.ApplyCaption(window);
    }
    internal static void BindLocalBrushes(DependencyObject root)
    {
        // Code-built editor controls use the same live resource bindings as XAML controls.
        if(root is FrameworkElement element)
        {
            var locals=element.GetLocalValueEnumerator();var replacements=new List<(DependencyProperty,string)>();
            while(locals.MoveNext())
                if(locals.Current.Value is Brush brush)
                    foreach(string key in Keys)
                        if(ReferenceEquals(brush,Application.Current.Resources[key])){replacements.Add((locals.Current.Property,key));break;}
            foreach(var (property,key) in replacements)element.SetResourceReference(property,key);
        }
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())BindLocalBrushes(child);
    }
    sealed record Preference(string Theme);
}
