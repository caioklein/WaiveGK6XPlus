using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Waive.Core;

namespace Waive.Desktop;

public partial class MainWindow
{
    KeyboardProfile profile=new();
    readonly ProfileStore profileStore=new(Path.Combine(AppContext.BaseDirectory,"Profiles"));
    readonly ObservableCollection<KeyboardProfile> savedProfiles=[];
    readonly Dictionary<int,uint> defaultKeys=[];
    readonly Dictionary<Border,int> slotsByKey=[];
    readonly HashSet<int> selectedKeys=Enumerable.Range(0,112).ToHashSet();
    readonly List<string> undo=[],redo=[];
    bool loadingProfile,dirty;
    int anchor=-1,returnLayer=1;
    LivePlayback? playback;
    readonly System.Windows.Threading.DispatcherTimer previewTimer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    readonly System.Diagnostics.Stopwatch previewClock=System.Diagnostics.Stopwatch.StartNew();
    sealed record LayerChoice(int Mode,string Name){public override string ToString()=>Name;}
    sealed record ActionChoice(string Value,string Name){public override string ToString()=>Name;}
    sealed record EffectChoiceItem(Guid? Id,string Name){public override string ToString()=>Name;}

    void InitializeProfiles()
    {
        previewTimer.Tick+=(_,_)=>RefreshPreview();
        using var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","96xs-live-defaults.json")));
        foreach(var key in data.RootElement.GetProperty("KeySet").EnumerateArray())defaultKeys[key.GetProperty("Index").GetInt32()]=Convert.ToUInt32(key.GetProperty("DriverValue").GetString()![2..],16);
        foreach(var saved in profileStore.Load().OrderBy(p=>p.Name))savedProfiles.Add(saved);
        loadingProfile=true;
        LayerSelector.ItemsSource=new[]{new LayerChoice(1,"Built-in lighting"),new LayerChoice(2,"Layer 1 · Windows"),new LayerChoice(3,"Layer 2 · Mac"),new LayerChoice(4,"Layer 3")};
        ProfileList.ItemsSource=savedProfiles;
        var remembered=Path.Combine(AppContext.BaseDirectory,"Profiles","last-profile.txt");
        var selected=File.Exists(remembered)&&Guid.TryParse(File.ReadAllText(remembered),out var id)?savedProfiles.FirstOrDefault(p=>p.Id==id):null;
        selected??=savedProfiles.FirstOrDefault();
        if(selected is not null){profile=selected.Clone();ProfileList.SelectedItem=selected;}
        loadingProfile=false;SyncProfileControls();dirty=false;
        if(profileStore.Warnings.Count>0)MessageBox.Show(this,"Some saved profiles could not be loaded. Their files have been kept.\n\n"+string.Join("\n",profileStore.Warnings),"Saved profile warning",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    void SyncProfileControls()
    {
        keyEffectPreview=null;
        loadingProfile=true;
        ProfileName.Text=profile.Name;color=Rgb.Parse(profile.Color);HexBox.Text=profile.Color;BrightnessSlider.Value=profile.Brightness;validColor=true;
        returnLayer=profile.ReturnLayer;
        LiveOption.IsChecked=profile.Mode==0;StoredOption.IsChecked=profile.Mode!=0;LayerSelector.SelectedIndex=profile.Mode==0?returnLayer-1:profile.Mode-1;
        if(profile.Mode!=0)returnLayer=profile.Mode;
        RefreshLibraries();loadingProfile=false;
        RefreshModeText();RefreshPreview();RefreshProfileStatus();
    }
    void RefreshLibraries()
    {
        var previous=loadingProfile;loadingProfile=true;
        BindingChoice.ItemsSource=KeyCatalog.Names.Select(name=>new ActionChoice(name,name)).Concat(profile.Macros.Select(m=>new ActionChoice("macro:"+m.Id,"Macro · "+m.Name))).ToArray();
        var effects=new[]{new EffectChoiceItem(null,"None · use per-key colors")}.Concat(profile.Effects.Select(e=>new EffectChoiceItem(e.Id,e.Name))).ToArray();
        EffectChoice.ItemsSource=effects;EffectChoice.SelectedItem=effects.FirstOrDefault(e=>e.Id==profile.EffectId)??effects[0];
        KeyEffectChoice.ItemsSource=effects;KeyEffectChoice.SelectedIndex=0;loadingProfile=previous;
    }
    void RefreshModeText()
    {
        if(!ready)return;
        LayerSelector.IsEnabled=!busy;
        LayerCaption.Text=StoredOption.IsChecked==true?"SAVE INTO THIS LAYER":"RETURN TO WHEN STOPPED";
        EditFn.IsEnabled=!busy&&StoredOption.IsChecked==true&&LayerSelector.SelectedIndex>0;
        if(!EditFn.IsEnabled)EditFn.IsChecked=false;
        ModeDescription.Text=StoredOption.IsChecked==true?"Stored on the keyboard. Apply writes this layer; Save keeps a local copy.":"Live profile: WaiveGK6XPlus runs effects and macros. Stop returns to the selected onboard layer.";
    }
    void BeforeEdit()
    {
        if(loadingProfile||colorGesture)return;
        var snapshot=JsonSerializer.Serialize(profile);
        if(undo.LastOrDefault()!=snapshot)undo.Add(snapshot);
        while(undo.Count>20 || undo.Sum(x=>x.Length)>2_500_000)undo.RemoveAt(0);
        redo.Clear();
    }
    void Changed()
    {
        dirty=true;RefreshProfileStatus();RefreshPreview();
    }
    void RefreshProfileStatus()=>ProfileStatus.Text=dirty?"Edited · not saved on this PC":savedProfiles.Any(p=>p.Id==profile.Id)?"Saved on this PC":"New · not saved";
    void Guard(Action action){try{action();}catch(Exception error){SetResult(error.Message,true);}}
    bool LeaveProfile()
    {
        if(!dirty)return true;
        var result=MessageBox.Show("Save your changes on this PC before leaving this profile?","Unsaved profile",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
        if(result==MessageBoxResult.Cancel)return false;
        if(result==MessageBoxResult.Yes){try{SaveProfile();}catch(Exception error){SetResult(error.Message,true);return false;}}
        return true;
    }
    void SaveProfile()
    {
        ProfileRenamed(this,new RoutedEventArgs());profile.Validate();
        profileStore.Save(profile);
        var old=savedProfiles.FirstOrDefault(p=>p.Id==profile.Id);loadingProfile=true;
        if(old is not null)savedProfiles[savedProfiles.IndexOf(old)]=profile.Clone();else savedProfiles.Add(profile.Clone());
        ProfileList.SelectedItem=savedProfiles.First(p=>p.Id==profile.Id);loadingProfile=false;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"Profiles","last-profile.txt"),profile.Id.ToString());
        dirty=false;RefreshProfileStatus();SetResult("Profile saved on this PC. Apply sends it to the keyboard.");
    }
    void SaveProfileClicked(object sender,RoutedEventArgs e)=>Guard(SaveProfile);
    void ProfileRenamed(object sender,RoutedEventArgs e)
    {
        if(!ready||loadingProfile||ProfileName.Text==profile.Name)return;
        BeforeEdit();profile.Name=ProfileName.Text.Trim();Changed();
    }
    void ProfileChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ready||loadingProfile||ProfileList.SelectedItem is not KeyboardProfile selected)return;
        if(!LeaveProfile()){loadingProfile=true;ProfileList.SelectedItem=savedProfiles.FirstOrDefault(p=>p.Id==profile.Id);loadingProfile=false;return;}
        profile=selected.Clone();dirty=false;undo.Clear();redo.Clear();SyncProfileControls();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"Profiles","last-profile.txt"),profile.Id.ToString());
    }
    void NewProfileClicked(object sender,RoutedEventArgs e)=>Guard(()=>{if(!LeaveProfile())return;profile=new(){Name="Profile "+(savedProfiles.Count+1)};dirty=false;loadingProfile=true;ProfileList.SelectedIndex=-1;loadingProfile=false;undo.Clear();redo.Clear();SyncProfileControls();});
    void CopyProfileClicked(object sender,RoutedEventArgs e)=>Guard(()=>{BeforeEdit();profile=profile.Clone();profile.Id=Guid.NewGuid();profile.Name+=" copy";dirty=true;loadingProfile=true;ProfileList.SelectedIndex=-1;loadingProfile=false;SyncProfileControls();});
    void DeleteProfileClicked(object sender,RoutedEventArgs e)=>Guard(()=>{if(MessageBox.Show("Remove the saved copy on this PC? Keyboard settings are not erased.","Delete profile",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;profileStore.Delete(profile);loadingProfile=true;var old=savedProfiles.FirstOrDefault(p=>p.Id==profile.Id);if(old is not null)savedProfiles.Remove(old);ProfileList.SelectedIndex=-1;loadingProfile=false;profile=new();dirty=false;undo.Clear();redo.Clear();SyncProfileControls();});
    void ImportProfileClicked(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        if(!LeaveProfile())return;
        var dialog=new OpenFileDialog{Filter="Keyboard profiles (*.waive;*.cmf;*.gpr;*.json)|*.waive;*.cmf;*.gpr;*.json",Multiselect=true};
        if(dialog.ShowDialog()!=true)return;
        var imports=dialog.FileNames.SelectMany(ProfileTransfer.Import).ToArray();
        foreach(var imported in imports){imported.Id=Guid.NewGuid();profileStore.Save(imported);savedProfiles.Add(imported.Clone());}
        if(imports.Length>0){loadingProfile=true;ProfileList.SelectedItem=savedProfiles.First(p=>p.Id==imports[0].Id);loadingProfile=false;profile=imports[0].Clone();dirty=false;undo.Clear();redo.Clear();SyncProfileControls();}
        SetResult($"Imported {imports.Length} profile(s). The original files were left in place.");
    });
    static string FileName(string name)=>string.Concat(name.Where(c=>!Path.GetInvalidFileNameChars().Contains(c)));
    void ExportProfileClicked(object sender,RoutedEventArgs e)=>Guard(()=>{profile.Validate();var dialog=new SaveFileDialog{Filter="Complete WaiveGK6XPlus profile (*.waive)|*.waive|GK6+ profile archive (*.gpr)|*.gpr|GK6+ profile only (*.cmf)|*.cmf",FileName=FileName(profile.Name)+".waive"};if(dialog.ShowDialog()==true){ProfileTransfer.Export(dialog.FileName,profile,defaultKeys);SetResult("Profile exported. WaiveGK6XPlus and GPR bundles include macros and lighting effects.");}});
    void LayerChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ready||loadingProfile||LayerSelector.SelectedItem is not LayerChoice choice)return;
        BeforeEdit();returnLayer=choice.Mode;profile.ReturnLayer=choice.Mode;if(StoredOption.IsChecked==true)profile.Mode=choice.Mode;Changed();RefreshModeText();
    }
    void SelectAllClicked(object sender,RoutedEventArgs e){selectedKeys.Clear();selectedKeys.UnionWith(Enumerable.Range(0,112));RefreshSelection();}
    void ClearSelectionClicked(object sender,RoutedEventArgs e){selectedKeys.Clear();RefreshSelection();}
    void SelectKey(int index)
    {
        if(busy)return;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)&&anchor>=0)
        {
            int first=keycaps.FindIndex(k=>slotsByKey[k]==anchor),last=keycaps.FindIndex(k=>slotsByKey[k]==index);
            if(first>=0&&last>=0){selectedKeys.Clear();foreach(var key in keycaps.Skip(Math.Min(first,last)).Take(Math.Abs(first-last)+1))selectedKeys.Add(slotsByKey[key]);}
        }
        else if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){if(!selectedKeys.Add(index))selectedKeys.Remove(index);}
        else{selectedKeys.Clear();selectedKeys.Add(index);}
        anchor=index;RefreshSelection();
    }
    void RefreshSelection()
    {
        SelectionText.Text=selectedKeys.Count==112?"All keys selected":$"{selectedKeys.Count} {(selectedKeys.Count==1?"key":"keys")} selected";
        if(selectedKeys.Count==1)
        {
            int index=selectedKeys.First();loadingProfile=true;HexBox.Text=profile.KeyColors.GetValueOrDefault(index,profile.Color);color=Rgb.Parse(HexBox.Text);validColor=true;loadingProfile=false;
            BindingSummary.Text=EditFn.IsChecked==true?profile.FnKeys.GetValueOrDefault(index,"Default Fn action"):profile.Keys.GetValueOrDefault(index,KeyCatalog.Decode(defaultKeys.GetValueOrDefault(index)));
        }
        else BindingSummary.Text="";
        RefreshPreview();
    }
    void PaintSelection()
    {
        if(selectedKeys.Count==0)return;
        BeforeEdit();
        if(selectedKeys.Count==112){profile.Color=color.Hex;profile.KeyColors.Clear();}
        else foreach(var index in selectedKeys)profile.KeyColors[index]=color.Hex;
        keyEffectPreview=null;RemoveMotionFromKeys(selectedKeys);
        var loading=loadingProfile;loadingProfile=true;EffectChoice.SelectedItem=EffectChoice.Items.Cast<EffectChoiceItem>().FirstOrDefault(e=>e.Id==profile.EffectId);loadingProfile=loading;
        Changed();
    }
    void AssignBindingClicked(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        var indices=selectedKeys.Where(i=>i!=0).ToArray();if(indices.Length==0)throw new FormatException("Select a key other than Fn.");
        string binding=BindingChoice.SelectedItem is ActionChoice choice && BindingChoice.Text==choice.Name?choice.Value:BindingChoice.Text;
        if(!binding.StartsWith("macro:"))KeyCatalog.Encode(binding);
        if(EditFn.IsChecked==true&&profile.Mode<2)throw new FormatException("Choose onboard layer 2, 3, or 4 to edit Fn assignments.");
        BeforeEdit();var target=EditFn.IsChecked==true?profile.FnKeys:profile.Keys;foreach(var index in indices)target[index]=binding;
        Changed();RefreshSelection();SetResult("Key assignments edited. Save or Apply when ready.");
    });
    void ClearBindingClicked(object sender,RoutedEventArgs e)=>Guard(()=>{BeforeEdit();var target=EditFn.IsChecked==true?profile.FnKeys:profile.Keys;foreach(var index in selectedKeys)target.Remove(index);Changed();RefreshSelection();});
    void UndoClicked(object sender,RoutedEventArgs e){if(busy||undo.Count==0)return;redo.Add(JsonSerializer.Serialize(profile));profile=JsonSerializer.Deserialize<KeyboardProfile>(undo[^1])!;undo.RemoveAt(undo.Count-1);dirty=true;SyncProfileControls();}
    void RedoClicked(object sender,RoutedEventArgs e){if(busy||redo.Count==0)return;undo.Add(JsonSerializer.Serialize(profile));profile=JsonSerializer.Deserialize<KeyboardProfile>(redo[^1])!;redo.RemoveAt(redo.Count-1);dirty=true;SyncProfileControls();}
    void MacrosClicked(object sender,RoutedEventArgs e)=>Guard(()=>{var copy=profile.Clone();var editor=new MacroWindow(copy){Owner=this};if(editor.ShowDialog()==true){BeforeEdit();profile=copy;RefreshLibraries();Changed();}});
    void EffectsClicked(object sender,RoutedEventArgs e)=>Guard(()=>{var copy=profile.Clone();var editor=new EffectWindow(copy){Owner=this};if(editor.ShowDialog()==true){BeforeEdit();profile=copy;RefreshLibraries();Changed();}});
    void EffectChanged(object sender,SelectionChangedEventArgs e){if(!ready||loadingProfile||EffectChoice.SelectedItem is not EffectChoiceItem effect)return;BeforeEdit();keyEffectPreview=null;profile.EffectId=effect.Id;Changed();}
    void AssignKeyEffectClicked(object sender,RoutedEventArgs e)=>Guard(()=>{if(KeyEffectChoice.SelectedItem is not EffectChoiceItem{Id:Guid id})throw new FormatException("Choose a lighting effect first.");BeforeEdit();foreach(var key in selectedKeys)profile.KeyEffects[key]=id;Changed();});
    void ClearKeyEffectClicked(object sender,RoutedEventArgs e){BeforeEdit();foreach(var key in selectedKeys)profile.KeyEffects.Remove(key);Changed();}
    async Task StopPlaybackAsync(){var active=playback;playback=null;if(active is not null)await active.DisposeAsync();}
    void FnViewChanged(object sender,RoutedEventArgs e){if(ready)RefreshSelection();}
    void PreviewOptionChanged(object sender,RoutedEventArgs e){if(AnimatePreview.IsChecked!=true)keyEffectPreview=null;if(ready)RefreshPreview();}
    void UpdatePreviewTimer()
    {
        if(keyEffectPreview is not null&&previewClock.ElapsedMilliseconds>=keyEffectPreview.DurationTicks*50L)keyEffectPreview=null;
        bool animate=AnimatePreview.IsChecked==true&&(profile.EffectId is not null||keyEffectPreview is not null)&&WindowState!=WindowState.Minimized&&!closing;
        if(animate)previewTimer.Start();else previewTimer.Stop();
    }
    void BrowseTargetClicked(object sender,RoutedEventArgs e){var dialog=new OpenFileDialog{Filter="Files and programs|*.*"};if(dialog.ShowDialog()==true)OpenTarget.Text=dialog.FileName;}
    void AssignOpenClicked(object sender,RoutedEventArgs e)=>Guard(()=>{if(profile.Mode!=0)throw new FormatException("Choose Live profile on the Lighting tab first.");HostAction.Validate(OpenTarget.Text.Trim());var indices=selectedKeys.Where(i=>i!=0).ToArray();if(indices.Length==0)throw new FormatException("Select a key other than Fn.");BeforeEdit();foreach(var index in indices)profile.Keys[index]="open:"+OpenTarget.Text.Trim();Changed();RefreshSelection();});
}


