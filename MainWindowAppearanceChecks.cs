using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace RidersToolkit;

public partial class MainWindow
{
    internal List<(string Title,bool Passed)> CheckAppearance()
    {
        var results=new List<(string,bool)>();
        void Check(string title,bool passed)=>results.Add((title,passed));
        string? previousRoot=Storage.TestRoot;var previousTheme=ThemeManager.Current;
        string temporary=Path.Combine(Path.GetTempPath(),"Kinship-appearance-"+Guid.NewGuid().ToString("N"));
        try
        {
            Storage.TestRoot=temporary;string settings=Path.Combine(temporary,"appearance.json");
            Check("A fresh installation defaults to Stories light",ThemeManager.Read(settings)==StudioTheme.White);
            Directory.CreateDirectory(temporary);File.WriteAllText(settings,"broken preference");
            Check("An unreadable or retired skin preference falls back to Stories light",ThemeManager.Read(settings)==StudioTheme.White&&ThemeManager.Parse("hunter-camp")==StudioTheme.White);
            ThemeManager.Apply(StudioTheme.White);UpdateThemeLabel();
            ThemeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();_studio?.UpdateLayout();
            Check("Theme button selects Moonlit hunt and saves that choice",ThemeManager.Current==StudioTheme.NightCamp&&Equals(ThemeButton.Content,"Theme: Moonlit hunt")&&ThemeManager.Read(settings)==StudioTheme.NightCamp);
            Check("Moonlit hunt updates an already-open editor's code-built surfaces",_studio?.Content is Border {Background:SolidColorBrush brush}&&brush.Color==Color.FromRgb(17,30,48));
            Check("Moonlit hunt updates an already-open editor's secondary text",_studio is not null&&Descendants<TextBlock>(_studio).Any(t=>t.Text.StartsWith("Offline save editor")&&t.Foreground is SolidColorBrush b&&b.Color==Color.FromRgb(169,190,208)));
            ThemeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();_studio?.UpdateLayout();
            Check("Theme button returns to Stories light and saves that choice",ThemeManager.Current==StudioTheme.White&&Equals(ThemeButton.Content,"Theme: Stories light")&&ThemeManager.Read(settings)==StudioTheme.White);
            Check("Stories light uses a white editor surface and cloud workspace",Background is SolidColorBrush white&&white.Color==Color.FromRgb(245,248,252)&&_studio?.Content is Border {Background:SolidColorBrush wb}&&wb.Color==white.Color&&((SolidColorBrush)FindResource("Panel")).Color==Colors.White);
            var mascot=Descendants<Image>(MascotCard).Single();
            Check("Pixel Navirou loads in the fixed sidebar header",mascot.Source is System.Windows.Media.Imaging.BitmapSource bitmap&&bitmap.PixelWidth>0&&mascot.IsVisible&&mascot.ActualHeight==44);
            Check("Pixel mascot uses nearest-neighbor sampling",RenderOptions.GetBitmapScalingMode(mascot)==BitmapScalingMode.NearestNeighbor);
            Check("The original coastal illustration is embedded and loaded",Descendants<Image>(CoastScene).Single().Source is System.Windows.Media.Imaging.BitmapSource scene&&scene.PixelWidth>0);
            var typeface=new Typeface((FontFamily)FindResource("PixelFont"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
            Check("Pixel display font loads from the bundled licensed font",typeface.TryGetGlyphTypeface(out var glyphs)&&glyphs.FontUri.ToString().Contains("silkscreen",StringComparison.OrdinalIgnoreCase));
            Check("The detached installation-path footer has been removed",!Descendants<TextBlock>(this).Any(t=>t.Text.StartsWith("Steam installation detected")));
            foreach(var theme in new[]{StudioTheme.NightCamp,StudioTheme.White})
            {
                ThemeManager.Apply(theme);
                Check(theme+" native title-bar colors are accepted by Windows",WindowLayout.ApplyCaption(this)&&_studio is not null&&WindowLayout.ApplyCaption(_studio));
                double Luminance(Color color)
                {
                    double Channel(byte value){double v=value/255.0;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}
                    return .2126*Channel(color.R)+.7152*Channel(color.G)+.0722*Channel(color.B);
                }
                bool Contrast(string foreground,string background)
                {
                    double a=Luminance(((SolidColorBrush)FindResource(foreground)).Color),b=Luminance(((SolidColorBrush)FindResource(background)).Color);
                    return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05)>=4.5;
                }
                Check(theme+" keeps workspace and sidebar text readable",new[]{("Ink","Canvas"),("Muted","Raised"),("Mint","Selected"),("Gold","Panel"),("Red","DangerSurface"),("OnAccent","Mint"),("SideInk","Sidebar"),("SideMuted","Sidebar"),("SideInk","SideSelected"),("CaptionInk","Caption")}.All(pair=>Contrast(pair.Item1,pair.Item2)));
            }
        }
        finally
        {
            Storage.TestRoot=previousRoot;ThemeManager.Apply(previousTheme);UpdateThemeLabel();
            // Delete only explicitly created fixture files, never a recursive path.
            string settings=Path.Combine(temporary,"appearance.json");if(File.Exists(settings))File.Delete(settings);
            if(Directory.Exists(temporary))Directory.Delete(temporary);
        }
        return results;
    }
}
