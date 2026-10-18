using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RidersToolkit;

internal sealed record SavedSlotChoice(string FileName,string Label)
{
    public override string ToString()=>Label;
}

internal sealed class StableEditorWindow : Window
{
    readonly Func<IEggStudioEditor?> _backend;
    readonly bool _offline;
    OfflineEggEditor? _fileEditor;
    readonly ComboBox _slots=new();
    readonly NativeCatalog _catalog;
    readonly ComboBox _subjects=new(){MinWidth=260,DisplayMemberPath="Label"},_species=new(){DisplayMemberPath="Name"};
    readonly ComboBox[] _genes=new ComboBox[9],_stars=new ComboBox[9];
    readonly CheckBox[] _locked=new CheckBox[9];
    readonly Button[] _rainbow=new Button[9];
    readonly TextBlock _status=new(){Text="Load a solo save, then refresh. New editor: live gameplay check pending.",TextWrapping=TextWrapping.Wrap};
    readonly Button _addEgg=new(){Content="Add egg…",IsEnabled=false,ToolTip="Open a saved game, then create an egg in a free slot—even when the carton is empty."};
    EggAdditionReceipt? _eggReceipt;AddEggWindow? _addWindow;
    readonly Button _apply=new(){Content="Apply preview",IsEnabled=false},_undo=new(){Content="Undo last edit",IsEnabled=false};
    EditorChange[]? _plan;EditorSubject? _previewSubject;EditReceipt? _receipt;
    public StableEditorWindow(Window owner,NativeCatalog catalog):this(owner,()=>null,catalog,true){_backend=()=>_fileEditor;}
    public StableEditorWindow(Window owner,Func<IEggStudioEditor?> backend,NativeCatalog catalog,bool offline=false)
    {
        _backend=backend;_catalog=catalog;_offline=offline;Owner=owner;Title="Kinship Studio · Egg & gene studio";
        Style=(Style)Application.Current.FindResource(typeof(Window));WindowStartupLocation=WindowStartupLocation.Manual;ResizeMode=ResizeMode.CanResize;
        var fit=WindowLayout.Fit(SystemParameters.WorkArea,860,720);Left=fit.Left;Top=fit.Top;Width=fit.Width;Height=fit.Height;MinWidth=Math.Min(650,fit.Width);MinHeight=Math.Min(450,fit.Height);
        SourceInitialized+=(_,_)=>WindowLayout.ApplyCaption(this);
        var root=new DockPanel{Background=(Brush)FindResource("Canvas")};Content=new Border{Padding=new Thickness(18),Background=(Brush)FindResource("Canvas"),Child=root};
        _status.Foreground=(Brush)FindResource("Muted");_status.FontSize=12;
        var footer=new StackPanel{Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var actions=new WrapPanel();footer.Children.Add(actions);
        void Button(string title,Action action,bool primary=false)
        {var b=new Button{Content=title,Margin=new Thickness(0,0,8,8)};if(primary)b.Style=(Style)FindResource("Primary");b.Click+=(_,_)=>Run(action);actions.Children.Add(b);}
        Button("Preview changes",Preview,true);_apply.Margin=new Thickness(0,0,8,8);_apply.Click+=(_,_)=>Run(Apply);actions.Children.Add(_apply);
        _addEgg.Margin=new Thickness(0,0,8,8);_addEgg.Click+=(_,_)=>Run(OpenAddEgg);actions.Children.Add(_addEgg);
        _undo.Margin=new Thickness(0,0,8,8);_undo.Click+=(_,_)=>Run(Undo);actions.Children.Add(_undo);
        _status.MinHeight=34;_status.MaxHeight=130;_status.Margin=new Thickness(0,5,0,0);footer.Children.Add(_status);
        var content=new StackPanel();root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        content.Children.Add(new TextBlock{Text="Egg & gene studio",FontSize=21,FontWeight=FontWeights.SemiBold});
        content.Children.Add(new TextBlock{Text=offline?"Offline save editor · keep Stories 2 closed. Every Apply, Add and Undo backs up your saves first.":"Apply and Add egg always back up your last in-game save first. Save in-game to include recent progress.",FontSize=12,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,6,0,10)});
        if(offline)
        {
            _status.Text="Save in-game, close Stories 2, then open a save slot. No files have been changed.";
            var saveRow=new DockPanel{Margin=new Thickness(0,0,0,12)};
            var open=new Button{Content="Open save",Margin=new Thickness(8,0,0,0)};DockPanel.SetDock(open,Dock.Right);saveRow.Children.Add(open);saveRow.Children.Add(_slots);content.Children.Add(saveRow);
            _slots.ItemsSource=Storage.SaveDirectory is {} dir?Enumerable.Range(1,3).Select(i=>new SavedSlotChoice($"mhr_slot_{i}.sav",$"Save slot {i}")).Where(s=>File.Exists(Path.Combine(dir,s.FileName))).ToArray():[];_slots.SelectedIndex=0;
            open.Click+=(_,_)=>Run(()=>
            {
                if(_slots.SelectedItem is not SavedSlotChoice file||Storage.SaveDirectory is null)throw new InvalidOperationException("Create a manual save in-game first, then close and reopen the studio.");
                _addWindow?.Close();_fileEditor=null;_addEgg.IsEnabled=false;_subjects.ItemsSource=null;_undo.IsEnabled=false;_receipt=null;_eggReceipt=null;
                _fileEditor=new(Path.Combine(Storage.SaveDirectory,file.FileName),_catalog);Refresh();
            });
            _slots.SelectionChanged+=(_,_)=>{_addWindow?.Close();_fileEditor=null;_subjects.ItemsSource=null;_addEgg.IsEnabled=false;_undo.IsEnabled=false;_receipt=null;_eggReceipt=null;Invalidate();_status.Text="Choose Open save to load this slot. Keep Stories 2 closed.";};
        }
        var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});content.Children.Add(row);
        var selection=new StackPanel{Margin=new Thickness(0,0,8,0)};selection.Children.Add(new TextBlock{Text=offline?"Saved Monstie or egg":"Owned Monstie or egg",FontSize=11,Margin=new Thickness(0,0,0,5)});selection.Children.Add(_subjects);row.Children.Add(selection);
        var speciesPanel=new StackPanel();speciesPanel.Children.Add(new TextBlock{Text="Egg species",FontSize=11,Margin=new Thickness(0,0,0,5)});speciesPanel.Children.Add(_species);Grid.SetColumn(speciesPanel,1);row.Children.Add(speciesPanel);
        var refresh=new Button{Content="Refresh owned",Margin=new Thickness(8,0,0,0)};refresh.VerticalAlignment=VerticalAlignment.Bottom;Grid.SetColumn(refresh,2);row.Children.Add(refresh);refresh.Click+=(_,_)=>Run(Refresh);
        _species.ItemsSource=_catalog.Species;_species.IsEnabled=false;
        content.Children.Add(new TextBlock{Text=offline?"Choose a saved egg or Monstie below. Apply preview writes the selected slot; load that same slot in-game afterward.":"Close the game’s egg and gene menus before Apply. Reopen them or switch Monsties to refresh.",Foreground=(Brush)FindResource("Muted"),FontSize=11,Margin=new Thickness(0,7,0,12)});
        var geneGrid=new Grid();for(int i=0;i<3;i++){geneGrid.ColumnDefinitions.Add(new());geneGrid.RowDefinitions.Add(new(){Height=GridLength.Auto});}content.Children.Add(geneGrid);
        for(int i=0;i<9;i++)
        {
            int index=i;var panel=new StackPanel();var box=new Border{Style=(Style)FindResource("Surface"),Padding=new Thickness(7),Margin=new Thickness(0,0,7,7),Child=panel};Grid.SetColumn(box,i%3);Grid.SetRow(box,i/3);geneGrid.Children.Add(box);
            panel.Children.Add(new TextBlock{Text=$"Slot {i+1}",FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,3)});
            _genes[i]=new(){ItemsSource=_catalog.Genes,DisplayMemberPath="Name",IsTextSearchEnabled=true,IsEnabled=false,FontSize=11};panel.Children.Add(_genes[i]);
            var controls=new WrapPanel{Margin=new Thickness(0,5,0,0)};panel.Children.Add(controls);
            _stars[i]=new(){ItemsSource=new[]{0,1,2},SelectedIndex=0,IsEnabled=false,MinWidth=48,FontSize=11,ItemTemplate=(DataTemplate)FindResource("GeneStars"),ToolTip="Gene stars"};controls.Children.Add(_stars[i]);
            _locked[i]=new(){Content="Lock",IsEnabled=false,ToolTip="Keep this slot locked",Margin=new Thickness(8,6,0,0),FontSize=11};controls.Children.Add(_locked[i]);
            var rainbow=new Button{Content="Rainbow",IsEnabled=false,FontSize=10,Padding=new Thickness(5,4,5,4),MinHeight=30,BorderBrush=(Brush)FindResource("Gold"),Margin=new Thickness(7,0,0,0),ToolTip="Put a Rainbow Gene in this slot; preview before applying"};_rainbow[i]=rainbow;controls.Children.Add(rainbow);
            rainbow.Click+=(_,_)=>{if(_subjects.SelectedItem is null)return;_genes[index].SelectedItem=_catalog.Genes.Single(g=>g.Id==17);_stars[index].SelectedIndex=0;_locked[index].IsChecked=false;Invalidate();};
            _genes[i].SelectionChanged+=(_,_)=>{if(_genes[index].SelectedItem is EditorChoice g){_stars[index].ItemsSource=Enumerable.Range(0,g.Stars+1).ToArray();_stars[index].SelectedIndex=0;}Invalidate();};
            _stars[i].SelectionChanged+=(_,_)=>Invalidate();_locked[i].Click+=(_,_)=>Invalidate();
        }
        var swap=new WrapPanel{Margin=new Thickness(0,6,0,10)};content.Children.Add(swap);swap.Children.Add(new TextBlock{Text="Rearrange slots",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
        ComboBox from=new ComboBox{ItemsSource=Enumerable.Range(1,9).ToArray(),SelectedIndex=0,Width=55},to=new ComboBox{ItemsSource=Enumerable.Range(1,9).ToArray(),SelectedIndex=1,Width=55,Margin=new Thickness(7,0,7,0)};swap.Children.Add(from);swap.Children.Add(to);
        var swapButton=new Button{Content="Swap"};swap.Children.Add(swapButton);swapButton.Click+=(_,_)=>{if(_subjects.SelectedItem is null)return;int a=from.SelectedIndex,b=to.SelectedIndex;var ga=_genes[a].SelectedItem;var gb=_genes[b].SelectedItem;var sa=_stars[a].SelectedItem;var sb=_stars[b].SelectedItem;var la=_locked[a].IsChecked;var lb=_locked[b].IsChecked;_genes[a].SelectedItem=gb;_genes[b].SelectedItem=ga;_stars[a].SelectedItem=sb;_stars[b].SelectedItem=sa;_locked[a].IsChecked=lb;_locked[b].IsChecked=la;Invalidate();};
        content.Children.Add(new TextBlock{Text="Rainbow Gene changes a bingo slot; rainbow egg glow remains a separate hunt setting. Add egg creates a new unhatched egg in a free slot, even with an empty carton. You can also copy an existing egg.",Foreground=(Brush)FindResource("Muted"),FontSize=11});
        _subjects.SelectionChanged+=(_,_)=>Run(Select);_species.SelectionChanged+=(_,_)=>Invalidate();
        ThemeManager.BindLocalBrushes(this);
    }
    IEggStudioEditor Backend()=>_backend()??throw new InvalidOperationException(_offline?"Save and close Stories 2, then choose Open save first.":"Start Stories 2 and connect before refreshing the editor.");
    void Run(Action action){try{action();}catch(Exception ex){_apply.IsEnabled=false;_status.Text=ex.Message;}}
    void Invalidate(){_plan=null;_previewSubject=null;_apply.IsEnabled=false;}
    void Refresh()
    {
        Invalidate();_addEgg.IsEnabled=false;var subjects=Backend().Read(_catalog);_subjects.ItemsSource=subjects;_subjects.SelectedIndex=0;_addEgg.IsEnabled=true;
        _status.Text=$"{subjects.Count(s=>!s.Egg)} Monsties · {subjects.Count(s=>s.Egg)} carton eggs. Add egg works with an empty carton. "+(_offline?"Opened "+_fileEditor?.FileName+". Edits are written only after Preview and Apply/Add.":"Live verification pending.");
    }
    void OpenAddEgg()
    {
        if(_addWindow is not null){WindowLayout.Reveal(_addWindow);return;}
        var eggs=(_subjects.ItemsSource as IEnumerable<EditorSubject>??[]).Where(s=>s.Egg).ToArray();
        _addWindow=new(this,_backend,_catalog,eggs,_subjects.SelectedItem as EditorSubject,receipt=>
        {
            _receipt=null;_eggReceipt=receipt;_undo.IsEnabled=true;
            Refresh();_subjects.SelectedItem=(_subjects.ItemsSource as IEnumerable<EditorSubject>)?.FirstOrDefault(s=>s.Egg&&s.Address==receipt.Plan.Target);
            _status.Text=_offline?"Egg saved, re-encrypted and checksum-verified. A fresh verified backup was created. Launch Stories 2 and load this save slot to check the carton.":"Egg added and verified in memory. Reopen the carton to check it before saving. A fresh verified save backup and journal were created; Undo last edit is available while the carton stays unchanged.";
        });
        _addWindow.Closed+=(_,_)=>_addWindow=null;WindowLayout.Reveal(_addWindow);
    }
    void Select()
    {
        Invalidate();_species.IsEnabled=false;
        for(int i=0;i<9;i++){_genes[i].IsEnabled=false;_stars[i].IsEnabled=false;_locked[i].IsEnabled=false;_rainbow[i].IsEnabled=false;}
        if(_subjects.SelectedItem is not EditorSubject s)return;
        int species=BitConverter.ToInt32(s.Snapshot,checked((int)(s.SpeciesAddress-s.Address)));_species.SelectedItem=_catalog.Species.First(x=>x.Id==species);_species.IsEnabled=s.Egg;
        for(int i=0;i<9;i++)
        {
            int at=checked((int)(s.GenesAddress-s.Address))+i*4,id=BitConverter.ToUInt16(s.Snapshot,at);int stars=s.Snapshot[at+3];
            var choice=_catalog.Genes.FirstOrDefault(g=>g.Id==id)??new EditorChoice(id,$"Existing slot {id}",Math.Clamp(stars,0,2));
            _genes[i].ItemsSource=_catalog.Genes.Any(x=>x.Id==id)?_catalog.Genes:[choice,.._catalog.Genes];_genes[i].SelectedItem=choice;_genes[i].IsEnabled=true;_stars[i].IsEnabled=true;_locked[i].IsEnabled=true;_rainbow[i].IsEnabled=true;
            _stars[i].SelectedItem=stars;_locked[i].IsChecked=s.Snapshot[at+2]!=0;
        }
    }
    void Preview()
    {
        var s=_subjects.SelectedItem as EditorSubject??throw new InvalidOperationException("Refresh and select an existing egg or Monstie first.");
        var genes=Enumerable.Range(0,9).Select(i=>(_genes[i].SelectedItem as EditorChoice??throw new ArgumentException("Choose a gene in every slot."),_stars[i].SelectedItem is int n?n:0,_locked[i].IsChecked==true)).Select(x=>(x.Item1.Id,x.Item2,x.Item3)).ToArray();
        var changes=StableEditor.Plan(s,(_species.SelectedItem as EditorChoice)!.Id,genes,_catalog);_plan=changes;_previewSubject=s;_apply.IsEnabled=changes.Length>0;
        _status.Text=changes.Length==0?"No changes selected.":"Preview · "+s.Label+"\n"+string.Join("\n",changes.Select(c=>c.Label))+"\nApply creates a new verified save backup before writing.";
    }
    void Apply()
    {
        if(_plan is null||_previewSubject is null)throw new InvalidOperationException("Preview the changes first.");
        _receipt=Backend().Apply(_previewSubject,_plan,Storage.Backup,Path.Combine(Storage.Root,"EditorJournals"));_eggReceipt=null;_undo.IsEnabled=true;
        Refresh();_status.Text=_offline?"Save updated, re-encrypted and checksum-verified. Your previous save is backed up. Load the selected slot in Stories 2 to see the changes.":"Applied and verified in memory. Reopen the carton or switch Monsties to refresh. Save in-game only after checking the result. A verified save backup and change journal are available.";
    }
    void Undo()
    {
        if(_eggReceipt is not null){Backend().UndoAddedEgg(_eggReceipt);_eggReceipt=null;}
        else if(_receipt is not null){Backend().Undo(_receipt);_receipt=null;}else return;
        _undo.IsEnabled=false;Refresh();_status.Text=_offline?"Previous save restored exactly. The edited version was also backed up before Undo.":"Previous fields restored in memory. Check the game before saving.";
    }
}
