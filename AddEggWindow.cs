using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RidersToolkit;

internal sealed record EggSourceOption(string Label,EditorSubject? Subject)
{
    public override string ToString()=>Label;
}

internal sealed class AddEggWindow : Window
{
    readonly Func<IEggStudioEditor?> _backend;
    readonly NativeCatalog _catalog;
    readonly ComboBox _template=new(){DisplayMemberPath="Label"},_species=new(){DisplayMemberPath="Name"};
    readonly Button _add=new(){Content="Add egg",IsEnabled=false};
    readonly TextBlock _status=new(){Text="Choose a species, then preview. No changes have been made.",FontSize=12,TextWrapping=TextWrapping.Wrap};
    readonly Action<EggAdditionReceipt> _added;
    EggAdditionPlan? _plan;
    public AddEggWindow(Window owner,Func<IEggStudioEditor?> backend,NativeCatalog catalog,EditorSubject[] eggs,EditorSubject? selected,Action<EggAdditionReceipt> added)
    {
        _backend=backend;_catalog=catalog;_added=added;Owner=owner;Title="Kinship Studio · Add egg";Style=(Style)Application.Current.FindResource(typeof(Window));ShowInTaskbar=false;
        var fit=WindowLayout.Fit(SystemParameters.WorkArea,510,535);Width=fit.Width;Height=fit.Height;Left=fit.Left;Top=fit.Top;MinWidth=Math.Min(420,fit.Width);MinHeight=Math.Min(420,fit.Height);WindowStartupLocation=WindowStartupLocation.Manual;
        SourceInitialized+=(_,_)=>WindowLayout.ApplyCaption(this);
        var root=new DockPanel{Background=(Brush)FindResource("Canvas")};Content=new Border{Padding=new Thickness(18),Background=(Brush)FindResource("Canvas"),Child=root};
        var footer=new StackPanel{Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var content=new StackPanel();root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        content.Children.Add(new TextBlock{Text="One more egg for your adventure",FontSize=20,FontWeight=FontWeights.SemiBold});
        content.Children.Add(new TextBlock{Text="Create an unhatched egg even with an empty carton. New eggs start with their species’ fixed genes and open spare slots. You can edit the genes afterward.",FontSize=12,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,8,0,14)});
        content.Children.Add(new TextBlock{Text="Egg setup",FontSize=12,Margin=new Thickness(0,0,0,5)});content.Children.Add(_template);
        content.Children.Add(new TextBlock{Text="New egg species",FontSize=12,Margin=new Thickness(0,12,0,5)});content.Children.Add(_species);
        var options=new[]{new EggSourceOption("New egg · species starter genes",null)}.Concat(eggs.Select(e=>new EggSourceOption("Copy "+e.Label,e))).ToArray();
        _template.ItemsSource=options;_template.SelectedIndex=0;
        _species.ItemsSource=catalog.Species;_species.SelectedIndex=0;
        if(selected is {Egg:true})_species.SelectedItem=catalog.Species.FirstOrDefault(s=>s.Id==BitConverter.ToInt32(selected.Snapshot));
        _status.Foreground=(Brush)FindResource("Muted");_status.Margin=new Thickness(0,14,0,12);footer.Children.Add(_status);
        var actions=new WrapPanel();footer.Children.Add(actions);
        var preview=new Button{Content="Preview egg",Margin=new Thickness(0,0,8,8)};actions.Children.Add(preview);preview.Click+=(_,_)=>Run(Preview);
        _add.Style=(Style)FindResource("Primary");_add.Margin=new Thickness(0,0,8,8);actions.Children.Add(_add);_add.Click+=(_,_)=>Run(Add);
        var cancel=new Button{Content="Cancel",Margin=new Thickness(0,0,0,8)};actions.Children.Add(cancel);cancel.Click+=(_,_)=>Close();
        footer.Children.Add(new TextBlock{Text="Every Add creates a verified save backup first. Keep Stories 2 closed. Load the edited save slot in-game afterward to check the egg.",FontSize=11,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,5,0,0)});
        _template.SelectionChanged+=(_,_)=>Invalidate();_species.SelectionChanged+=(_,_)=>Invalidate();
        ThemeManager.BindLocalBrushes(this);
    }
    IEggStudioEditor Backend()=>_backend()??throw new InvalidOperationException("Open a saved game in the studio first.");
    void Run(Action action){try{action();}catch(Exception ex){Invalidate();_status.Text=ex.Message;}}
    void Invalidate(){_plan=null;_add.IsEnabled=false;}
    void Preview()
    {
        var option=_template.SelectedItem as EggSourceOption??throw new InvalidOperationException("Choose New egg or a template.");
        var source=option.Subject;
        var species=_species.SelectedItem as EditorChoice??throw new InvalidOperationException("Choose a species.");
        _plan=Backend().PreviewAddEgg(source,species.Id,_catalog);_add.IsEnabled=true;
        int used=Enumerable.Range(0,StableEditor.CartonSlots).Count(i=>BitConverter.ToInt32(_plan.CartonBefore,i*StableEditor.EggBytes)!=0);
        string setup=source is null?"Species starter genes; normal quality, no random genes or hatch bonus.":"Genes and hatch bonuses copied from "+source.Label+".";
        _status.Text=$"Preview · {species.Name} into empty carton slot {_plan.Slot+1}.\nCarton: {used}/{_plan.Capacity} → {used+1}/{_plan.Capacity}. {setup}\nA fresh verified save backup is required before writing.";
    }
    void Add()
    {
        if(_plan is null)throw new InvalidOperationException("Preview the egg first.");
        _add.IsEnabled=false;
        var receipt=Backend().AddEgg(_plan,Storage.Backup,Path.Combine(Storage.Root,"EditorJournals"));
        _plan=null;_added(receipt);Close();
    }
}
