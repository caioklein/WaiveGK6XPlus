using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Waive.Core;
using Waive.Device.Windows;

namespace Waive.Desktop;

public static class EditorUi
{
    public static TextBlock Text(string text,int size=13)=>new(){Text=text,FontSize=size,Margin=new Thickness(0,5,0,6)};
    public static Button Button(string text,Action action)
    {
        var b=new Button{Content=text,Margin=new Thickness(0,0,8,8)};
        b.Click+=(_,_)=>{try{action();}catch(Exception e){MessageBox.Show(e.Message,"WaiveGK6XPlus",MessageBoxButton.OK,MessageBoxImage.Information);}};
        return b;
    }
    public static void Theme(Window window,string title)
    {
        window.Title=title;window.Width=970;window.Height=700;window.MinWidth=820;window.MinHeight=580;
        window.Background=new SolidColorBrush(Color.FromRgb(16,23,32));window.Foreground=Brushes.White;window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
    }
    public static DataGrid Grid()=>new(){AutoGenerateColumns=false,CanUserAddRows=false,Background=new SolidColorBrush(Color.FromRgb(20,30,40)),Foreground=Brushes.White,RowBackground=new SolidColorBrush(Color.FromRgb(25,37,48)),AlternatingRowBackground=new SolidColorBrush(Color.FromRgb(20,31,43)),GridLinesVisibility=DataGridGridLinesVisibility.None,BorderThickness=new Thickness(0),HeadersVisibility=DataGridHeadersVisibility.Column};
    public static Button ColorButton(TextBox target)
    {
        Button? button=null;
        button=Button("Pick color…",()=>
        {
            var picker=new ColorPicker{Width=240,Height=150,Margin=new Thickness(12)};picker.SetColor(Rgb.Parse(target.Text));picker.ColorChanged+=c=>target.Text=c.Hex;
            var popup=new System.Windows.Controls.Primitives.Popup{PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true,Child=new Border{Background=new SolidColorBrush(Color.FromRgb(16,25,35)),BorderBrush=new SolidColorBrush(Color.FromRgb(80,112,125)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Child=picker}};
            popup.IsOpen=true;
        });
        return button;
    }
}

public sealed class MacroWindow : Window
{
    readonly KeyboardProfile profile;
    readonly ListBox library=new();
    readonly TextBox name=new(){MaxLength=100},repeats=new(){Text="1",Width=70};
    readonly ComboBox stopMode=new(){ItemsSource=new[]{"Repeat N times","While key is held","Toggle with key"},SelectedIndex=0,Width=200};
    readonly CheckBox mouse=new(){Content="Record mouse buttons",Foreground=Brushes.White,IsChecked=true,Margin=new Thickness(10)};
    readonly DataGrid grid=EditorUi.Grid();
    readonly TextBlock status=EditorUi.Text("Select a macro, or create one. Recording runs only after you click Record.");
    readonly ObservableCollection<MacroEvent> events=[];
    readonly MacroRecorder recorder=new();
    MacroDefinition? selected;
    Button? recordButton,stopButton,saveButton;
    public MacroWindow(KeyboardProfile profile)
    {
        this.profile=profile;EditorUi.Theme(this,"WaiveGK6XPlus · Macro library");
        var stopText=new FrameworkElementFactory(typeof(TextBlock));stopText.SetBinding(TextBlock.TextProperty,new Binding());stopText.SetValue(TextBlock.ForegroundProperty,Brushes.White);stopMode.ItemTemplate=new DataTemplate{VisualTree=stopText};
        var root=new Grid{Margin=new Thickness(20)};root.ColumnDefinitions.Add(new(){Width=new GridLength(210)});root.ColumnDefinitions.Add(new(){Width=new GridLength(18)});root.ColumnDefinitions.Add(new());
        var left=new DockPanel();var tools=new WrapPanel();tools.Children.Add(EditorUi.Button("New",New));tools.Children.Add(EditorUi.Button("Delete",Delete));tools.Children.Add(EditorUi.Button("Import",Import));tools.Children.Add(EditorUi.Button("Export",Export));DockPanel.SetDock(tools,Dock.Top);left.Children.Add(tools);
        library.Background=new SolidColorBrush(Color.FromRgb(25,37,48));library.Foreground=Brushes.White;left.Children.Add(library);root.Children.Add(left);
        var right=new DockPanel();Grid.SetColumn(right,2);root.Children.Add(right);
        var bottom=new StackPanel();bottom.Children.Add(status);var close=new WrapPanel();saveButton=EditorUi.Button("Save library",Save);close.Children.Add(saveButton);close.Children.Add(EditorUi.Button("Cancel",()=>Close()));bottom.Children.Add(close);DockPanel.SetDock(bottom,Dock.Bottom);right.Children.Add(bottom);
        var top=new StackPanel();top.Children.Add(EditorUi.Text("Macro name",16));top.Children.Add(name);
        var settings=new WrapPanel{Margin=new Thickness(0,10,0,4)};settings.Children.Add(stopMode);settings.Children.Add(EditorUi.Text("Repeats"));settings.Children.Add(repeats);settings.Children.Add(mouse);top.Children.Add(settings);
        var buttons=new WrapPanel();recordButton=EditorUi.Button("Record",Record);stopButton=EditorUi.Button("Stop · F12",StopRecord);stopButton.IsEnabled=false;buttons.Children.Add(recordButton);buttons.Children.Add(stopButton);
        buttons.Children.Add(EditorUi.Button("Key down",()=>Add("KeyDown","A")));buttons.Children.Add(EditorUi.Button("Key up",()=>Add("KeyUp","A")));buttons.Children.Add(EditorUi.Button("Delay",()=>Add("Delay","50")));
        buttons.Children.Add(EditorUi.Button("Remove event",()=>{if(grid.SelectedItem is MacroEvent e)events.Remove(e);}));top.Children.Add(buttons);top.Children.Add(EditorUi.Text("Edit the key or delay in the table. Delays are milliseconds. Onboard macros allow up to 125 events."));
        DockPanel.SetDock(top,Dock.Top);right.Children.Add(top);
        grid.Columns.Add(new DataGridComboBoxColumn{Header="Action",ItemsSource=new[]{"KeyDown","KeyUp","Delay","MouseDown","MouseUp"},SelectedItemBinding=new Binding("Kind"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="Key / Left, Right, Middle / milliseconds",Binding=new Binding("Value"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(2,DataGridLengthUnitType.Star)});grid.ItemsSource=events;right.Children.Add(grid);
        Content=root;
        library.SelectionChanged+=(_,_)=>{if(recorder.IsRecording)return;Commit();selected=library.SelectedItem as MacroDefinition;Load();};
        recorder.Recorded+=e=>{events.Add(e);status.Text=$"Recording · {events.Count} events. Press F12 or Stop to finish.";};
        recorder.StopRequested+=()=>Dispatcher.BeginInvoke(StopRecord);
        Closing+=(_,_)=>recorder.Dispose();
        Refresh();if(profile.Macros.Count>0)library.SelectedIndex=0;
    }
    void Refresh(){library.ItemsSource=null;library.ItemsSource=profile.Macros;}
    void Commit()
    {
        if(selected is null)return;
        grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);
        selected.Name=name.Text.Trim();selected.Repeats=int.TryParse(repeats.Text,out var value)?value:1;selected.StopMode=stopMode.SelectedIndex+1;selected.Events=events.ToList();
    }
    void Load()
    {
        name.Text=selected?.Name??"";repeats.Text=(selected?.Repeats??1).ToString();stopMode.SelectedIndex=(selected?.StopMode??1)-1;events.Clear();
        foreach(var e in selected?.Events??[])events.Add(e);
    }
    void New(){if(recorder.IsRecording)return;Commit();var macro=new MacroDefinition{Name="Macro "+(profile.Macros.Count+1)};profile.Macros.Add(macro);Refresh();library.SelectedItem=macro;}
    void Delete()
    {
        if(recorder.IsRecording||selected is null)return;
        if(MessageBox.Show("Remove this macro and its key assignments from this profile?","Delete macro",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        var id=selected.Id;selected=null;profile.Macros.RemoveAll(m=>m.Id==id);foreach(var bindings in new[]{profile.Keys,profile.FnKeys})foreach(var key in bindings.Where(x=>x.Value=="macro:"+id).Select(x=>x.Key).ToArray())bindings.Remove(key);
        Refresh();library.SelectedIndex=profile.Macros.Count>0?0:-1;
    }
    void Add(string kind,string value){if(recorder.IsRecording)return;if(selected is null)New();events.Add(new(){Kind=kind,Value=value});grid.SelectedIndex=events.Count-1;}
    void Record()
    {
        if(selected is null)New();
        if(events.Count>0 && MessageBox.Show("Append recording to these events? Choose No to replace them.","Record macro",MessageBoxButton.YesNo)==MessageBoxResult.No)events.Clear();
        recorder.Start(mouse.IsChecked==true);recordButton!.IsEnabled=false;stopButton!.IsEnabled=true;saveButton!.IsEnabled=false;library.IsEnabled=false;grid.IsEnabled=false;status.Text="Recording keyboard input. Switch to your target app; press F12 to stop.";
    }
    void StopRecord(){recorder.Stop();recordButton!.IsEnabled=true;stopButton!.IsEnabled=false;saveButton!.IsEnabled=true;library.IsEnabled=true;grid.IsEnabled=true;status.Text=$"Recording stopped · {events.Count} events.";Commit();}
    void Save(){StopRecord();Commit();foreach(var m in profile.Macros)m.Validate();profile.Validate();DialogResult=true;}
    void Import()
    {
        if(recorder.IsRecording)return;
        var dialog=new OpenFileDialog{Filter="GK6+ macro (*.cms)|*.cms"};if(dialog.ShowDialog()!=true)return;
        var content=LegacyCodec.Decode(File.ReadAllBytes(dialog.FileName));if(content.Type!=5)throw new FormatException("Choose a CMS macro file.");
        var macro=LegacyImport.Macro(content.Body);Commit();profile.Macros.RemoveAll(m=>m.Id==macro.Id);profile.Macros.Add(macro);Refresh();library.SelectedItem=macro;
    }
    void Export()
    {
        if(recorder.IsRecording||selected is null)return;Commit();selected.Validate();
        var dialog=new SaveFileDialog{Filter="GK6+ macro (*.cms)|*.cms",FileName=selected.Name+".cms"};if(dialog.ShowDialog()==true)File.WriteAllBytes(dialog.FileName,LegacyCodec.Encode(5,"MACRO",LegacyImport.MacroText(selected)));
    }
}

public sealed class EffectWindow : Window
{
    readonly KeyboardProfile profile;
    readonly ListBox library=new(),frames=new();
    readonly TextBox name=new(),duration=new(){Text="1",Width=60},color=new(){Text="#68E4CC",Width=130};
    readonly DataGrid rules=EditorUi.Grid();
    readonly ObservableCollection<EffectRule> ruleItems=[];
    LightingEffect? selected;bool loading;
    public EffectWindow(KeyboardProfile profile)
    {
        this.profile=profile;EditorUi.Theme(this,"WaiveGK6XPlus · Lighting library");
        var root=new Grid{Margin=new Thickness(20)};root.ColumnDefinitions.Add(new(){Width=new GridLength(200)});root.ColumnDefinitions.Add(new(){Width=new GridLength(18)});root.ColumnDefinitions.Add(new());
        var left=new DockPanel();var commands=new WrapPanel();commands.Children.Add(EditorUi.Button("New",New));commands.Children.Add(EditorUi.Button("Delete",Delete));commands.Children.Add(EditorUi.Button("Import",Import));commands.Children.Add(EditorUi.Button("Export",Export));DockPanel.SetDock(commands,Dock.Top);left.Children.Add(commands);library.Background=new SolidColorBrush(Color.FromRgb(25,37,48));library.Foreground=Brushes.White;left.Children.Add(library);root.Children.Add(left);
        var right=new DockPanel();Grid.SetColumn(right,2);root.Children.Add(right);
        var footer=new WrapPanel();footer.Children.Add(EditorUi.Button("Save library",()=>{Commit();foreach(var effect in profile.Effects)effect.Validate();profile.Validate();DialogResult=true;}));footer.Children.Add(EditorUi.Button("Cancel",()=>Close()));DockPanel.SetDock(footer,Dock.Bottom);right.Children.Add(footer);
        var top=new StackPanel();top.Children.Add(EditorUi.Text("Effect name",16));top.Children.Add(name);
        var presets=new WrapPanel{Margin=new Thickness(0,10,0,0)};presets.Children.Add(EditorUi.Button("Capture keyboard",Capture));presets.Children.Add(EditorUi.Button("Rainbow preset",()=>Preset(1)));presets.Children.Add(EditorUi.Button("Breathing preset",()=>Preset(2)));top.Children.Add(presets);
        top.Children.Add(EditorUi.Text("Frames · one tick is 50 ms. Capture uses the per-key colors in your current profile."));
        frames.Height=95;frames.Background=new SolidColorBrush(Color.FromRgb(16,25,35));frames.Foreground=Brushes.White;top.Children.Add(frames);
        var frameControls=new WrapPanel{Margin=new Thickness(0,8,0,0)};frameControls.Children.Add(EditorUi.Text("Duration"));frameControls.Children.Add(duration);frameControls.Children.Add(color);frameControls.Children.Add(EditorUi.ColorButton(color));frameControls.Children.Add(EditorUi.Button("Set frame color",SetFrame));frameControls.Children.Add(EditorUi.Button("Remove frame",()=>{if(selected is null||frames.SelectedIndex<0)return;if(selected.Frames.Count==1)throw new FormatException("Keep at least one frame.");selected.Frames.RemoveAt(frames.SelectedIndex);RefreshFrames();}));top.Children.Add(frameControls);
        var ruleControls=new WrapPanel();ruleControls.Children.Add(EditorUi.Button("Add color rule",()=>{if(selected is null)New();ruleItems.Add(new(){Keys=selected!.CoordinateSpace=="Led"?KeyboardLightingMap.Keys.Select(k=>k.LocationCode).ToList():Enumerable.Range(0,112).ToList(),Color=color.Text,Count=72});}));ruleControls.Children.Add(EditorUi.Button("Remove rule",()=>{if(rules.SelectedItem is EffectRule rule)ruleItems.Remove(rule);}));top.Children.Add(ruleControls);
        top.Children.Add(EditorUi.Text("Rules affect the keys already present in each frame. Cycle/fade timing uses ticks; colors are RGB hex."));
        DockPanel.SetDock(top,Dock.Top);right.Children.Add(top);
        rules.Columns.Add(new DataGridComboBoxColumn{Header="Color effect",ItemsSource=new[]{new RuleKind(0,"Solid"),new RuleKind(1,"Color cycle"),new RuleKind(2,"Breathing")},DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValueBinding=new Binding("Type"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=130});
        rules.Columns.Add(new DataGridTextColumn{Header="Color",Binding=new Binding("Color"),Width=100});rules.Columns.Add(new DataGridTextColumn{Header="Cycle / fade ticks",Binding=new Binding("Count"),Width=120});rules.Columns.Add(new DataGridTextColumn{Header="Hold ticks",Binding=new Binding("StayCount"),Width=85});rules.ItemsSource=ruleItems;right.Children.Add(rules);
        Content=root;library.SelectionChanged+=(_,_)=>{if(loading)return;Commit();selected=library.SelectedItem as LightingEffect;Load();};frames.SelectionChanged+=(_,_)=>{if(selected is not null&&frames.SelectedIndex>=0)duration.Text=selected.Frames[frames.SelectedIndex].Count.ToString();};
        Refresh();if(profile.Effects.Count>0)library.SelectedIndex=0;
    }
    sealed record RuleKind(int Id,string Name);
    void Commit(){if(selected is null)return;rules.CommitEdit(DataGridEditingUnit.Cell,true);rules.CommitEdit(DataGridEditingUnit.Row,true);selected.Name=name.Text.Trim();selected.Rules=ruleItems.ToList();}
    void Load(){loading=true;name.Text=selected?.Name??"";ruleItems.Clear();foreach(var rule in selected?.Rules??[])ruleItems.Add(rule);RefreshFrames();loading=false;}
    void Refresh(){library.ItemsSource=null;library.ItemsSource=profile.Effects;}
    void RefreshFrames(){frames.ItemsSource=selected?.Frames.Select((f,i)=>$"Frame {i+1} · {f.Count} ticks · {f.Data.Count} keys").ToArray();if(selected?.Frames.Count>0)frames.SelectedIndex=0;}
    void New(){Commit();var effect=new LightingEffect{Name="Effect "+(profile.Effects.Count+1),Frames=[new(){Data=Enumerable.Range(0,112).ToDictionary(k=>k,_=>profile.Color)}]};profile.Effects.Add(effect);Refresh();library.SelectedItem=effect;}
    void Delete(){if(selected is null)return;if(MessageBox.Show("Remove this effect and its assignments?","Delete effect",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;var id=selected.Id;selected=null;profile.Effects.RemoveAll(e=>e.Id==id);if(profile.EffectId==id)profile.EffectId=null;foreach(var key in profile.KeyEffects.Where(k=>k.Value==id).Select(k=>k.Key).ToArray())profile.KeyEffects.Remove(key);Refresh();library.SelectedIndex=profile.Effects.Count>0?0:-1;}
    void Capture(){if(selected is null)New();var data=Enumerable.Range(0,112).ToDictionary(k=>k,k=>profile.KeyColors.GetValueOrDefault(k,profile.Color));if(selected!.CoordinateSpace=="Led")data=data.Where(k=>KeyboardLightingMap.LedForKey(k.Key).HasValue).ToDictionary(k=>KeyboardLightingMap.LedForKey(k.Key)!.Value,k=>k.Value);selected.Frames.Add(new(){Count=1,Data=data});RefreshFrames();frames.SelectedIndex=selected.Frames.Count-1;}
    void SetFrame(){if(selected is null)New();if(frames.SelectedIndex<0)return;var hex=Rgb.Parse(color.Text).Hex;if(!int.TryParse(duration.Text,out var ticks)||ticks<1||ticks>1000)throw new FormatException("Choose 1–1000 ticks.");var frame=selected!.Frames[frames.SelectedIndex];frame.Count=ticks;foreach(var key in frame.Data.Keys.ToArray())frame.Data[key]=hex;RefreshFrames();}
    void Preset(int type){if(selected is null)New();selected!.CoordinateSpace="Logical";ruleItems.Clear();ruleItems.Add(new(){Type=type,Color=type==1?"#FF4040":Rgb.Parse(color.Text).Hex,Count=type==1?72:20,StayCount=5,Keys=Enumerable.Range(0,112).ToList()});selected.Frames=[new(){Data=Enumerable.Range(0,112).ToDictionary(k=>k,_=>profile.Color)}];RefreshFrames();}
    void Import(){var dialog=new OpenFileDialog{Filter="GK6+ lighting (*.le)|*.le"};if(dialog.ShowDialog()!=true)return;var content=LegacyCodec.Decode(File.ReadAllBytes(dialog.FileName));if(content.Type!=2)throw new FormatException("Choose an LE lighting file.");var effect=LegacyImport.Effect(JsonNode.Parse(content.Body)!.AsObject());Commit();profile.Effects.RemoveAll(e=>e.Id==effect.Id);profile.Effects.Add(effect);Refresh();library.SelectedItem=effect;}
    void Export(){if(selected is null)return;Commit();selected.Validate();var dialog=new SaveFileDialog{Filter="GK6+ lighting (*.le)|*.le",FileName=selected.Name+".le"};if(dialog.ShowDialog()==true)File.WriteAllBytes(dialog.FileName,LegacyCodec.Encode(2,"light",LegacyImport.EffectJson(selected).ToJsonString()));}
}

