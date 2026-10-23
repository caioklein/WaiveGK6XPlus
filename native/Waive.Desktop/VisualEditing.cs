using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Waive.Core;

namespace Waive.Desktop;

public partial class MainWindow
{
    KeySelectionStroke? selectionStroke;
    Point previousDrag;
    bool colorGesture;
    readonly List<KeyBounds> keyBounds=[];
    readonly Dictionary<string,Border> presetCards=[];
    LightingEffect? keyEffectPreview;

    void InitializeVisualEditing()
    {
        Picker.GestureStarted+=BeginColorEdit;
        Picker.GestureCompleted+=EndColorEdit;
        Picker.ColorChanged+=c=>HexBox.Text=c.Hex;
        KeyboardCanvas.MouseLeftButtonDown+=(_,e)=>{BeginKeyDrag(-1,e.GetPosition(KeyboardCanvas),Keyboard.Modifiers);e.Handled=true;};
        KeyboardCanvas.MouseMove+=(_,e)=>{if(selectionStroke is not null){if(e.LeftButton!=MouseButtonState.Pressed)EndKeyDrag();else ContinueKeyDrag(e.GetPosition(KeyboardCanvas));}};
        KeyboardCanvas.MouseLeftButtonUp+=(_,e)=>{if(selectionStroke is not null){ContinueKeyDrag(e.GetPosition(KeyboardCanvas));EndKeyDrag();e.Handled=true;}};
        KeyboardCanvas.LostMouseCapture+=(_,_)=>selectionStroke=null;
        Deactivated+=(_,_)=>EndKeyDrag();
        AddPreset("solid","Solid color","One color, no motion",["#68E4CC","#68E4CC","#68E4CC"]);
        AddPreset("rainbow","Rainbow","Colors flow through hues",["#F26D95","#F8CB62","#68E4CC","#759DFF"]);
        AddPreset("breathing","Breathing","Gently fade in and out",["#162C31","#68E4CC","#162C31"]);
        AddPreset("pulse","Key-press glow","Light up when pressed",["#172430","#F8CB62","#172430"]);
        Picker.SetColor(color);
    }
    void BeginColorEdit(){BeforeEdit();colorGesture=true;}
    void EndColorEdit()=>colorGesture=false;
    void AddPreset(string kind,string title,string description,string[] colors)
    {
        var stack=new StackPanel();var strip=new Border{Height=9,CornerRadius=new(4),Margin=new(0,0,0,7)};
        var brush=new LinearGradientBrush{StartPoint=new(0,.5),EndPoint=new(1,.5)};
        for(int i=0;i<colors.Length;i++)brush.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(colors[i]),i/(double)(colors.Length-1)));
        strip.Background=brush;stack.Children.Add(strip);
        stack.Children.Add(new TextBlock{Text=title,FontSize=13,FontWeight=FontWeights.SemiBold});
        stack.Children.Add(new TextBlock{Text=description,FontSize=10,Foreground=Brush("#A6B8C8"),Margin=new(0,4,0,0)});
        var card=new Border{Child=stack,BorderThickness=new(1),BorderBrush=Brush("#354858"),CornerRadius=new(7),Padding=new(10),Background=Brush("#14212D")};
        presetCards[kind]=card;
        var button=new Button{Content=card,Padding=new(0),Margin=new(0,0,7,7),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=description};
        System.Windows.Automation.AutomationProperties.SetName(button,title+" lighting preset");
        button.Click+=(_,_)=>Guard(()=>ApplyLightingPreset(kind));PresetPanel.Children.Add(button);
    }
    void BeginKeyDrag(int index,Point point,ModifierKeys modifiers)
    {
        if(busy)return;
        bool remove=modifiers.HasFlag(ModifierKeys.Alt)||(modifiers.HasFlag(ModifierKeys.Control)&&index>=0&&selectedKeys.Contains(index));
        if(modifiers.HasFlag(ModifierKeys.Shift)){if(index>=0)SelectKey(index);remove=false;}
        else if(!modifiers.HasFlag(ModifierKeys.Control)&&!modifiers.HasFlag(ModifierKeys.Alt))selectedKeys.Clear();
        selectionStroke=new(selectedKeys,remove);if(index>=0){selectionStroke.Visit(index);anchor=index;}previousDrag=point;
        if(Mouse.LeftButton==MouseButtonState.Pressed)KeyboardCanvas.CaptureMouse();
        RefreshSelection();
    }
    void ContinueKeyDrag(Point point)
    {
        if(selectionStroke is null)return;
        bool changed=false;
        foreach(var key in keyBounds)if(key.Crosses(previousDrag.X,previousDrag.Y,point.X,point.Y))changed|=selectionStroke.Visit(key.Index);
        previousDrag=point;if(changed)RefreshSelection();
    }
    void EndKeyDrag(){selectionStroke=null;if(KeyboardCanvas.IsMouseCaptured)KeyboardCanvas.ReleaseMouseCapture();}
    int[] LightingTargets()=>WholeKeyboardScope.IsChecked==true?Enumerable.Range(0,112).ToArray():selectedKeys.Order().ToArray();
    Dictionary<int,string> CurrentKeyColors()=>Enumerable.Range(0,112).ToDictionary(k=>k,k=>profile.KeyColors.GetValueOrDefault(k,profile.Color));
    LightingEffect EditableLook()
    {
        var effect=profile.Effects.FirstOrDefault(e=>e.Id==profile.EffectId&&e.EditableZones);
        if(effect is not null)return effect;
        effect=new(){Name="Custom keyboard lighting",EditableZones=true,Frames=[new(){Count=100,Data=CurrentKeyColors()}]};
        profile.Effects.Add(effect);profile.EffectId=effect.Id;return effect;
    }
    void RemoveMotionFromKeys(IEnumerable<int> indices)
    {
        var effect=profile.Effects.FirstOrDefault(e=>e.Id==profile.EffectId);
        if(effect is null)return;
        if(!effect.EditableZones){profile.EffectId=null;return;}
        var keys=indices.ToHashSet();
        foreach(var rule in effect.Rules)rule.Keys.RemoveAll(keys.Contains);
        effect.Rules.RemoveAll(r=>r.Keys.Count==0);
        foreach(var frame in effect.Frames)foreach(var key in keys)frame.Data[key]=profile.KeyColors.GetValueOrDefault(key,profile.Color);
        if(effect.Rules.Count==0)profile.EffectId=null;
    }
    void ApplyLightingPreset(string kind)
    {
        var targets=LightingTargets();if(targets.Length==0)throw new FormatException("Drag across the keyboard to select some keys first.");
        BeforeEdit();keyEffectPreview=null;
        if(kind=="solid")
        {
            foreach(var key in targets){profile.KeyColors[key]=color.Hex;profile.KeyEffects.Remove(key);}
            RemoveMotionFromKeys(targets);
        }
        else if(kind=="pulse")
        {
            var effect=new LightingEffect{Name="Key-press glow · "+color.Hex,Frames=[new(){Count=20,Data=CurrentKeyColors()}],Rules=[new(){Type=2,Color=color.Hex,Count=8,StayCount=2,Keys=targets.ToList()}]};
            profile.Effects.Add(effect);foreach(var key in targets)profile.KeyEffects[key]=effect.Id;
            keyEffectPreview=effect;previewClock.Restart();AnimatePreview.IsChecked=true;
        }
        else
        {
            var effect=EditableLook();var keys=targets.ToHashSet();
            foreach(var rule in effect.Rules)rule.Keys.RemoveAll(keys.Contains);effect.Rules.RemoveAll(r=>r.Keys.Count==0);
            effect.Rules.Add(new(){Type=kind=="rainbow"?1:2,Color=kind=="rainbow"?"#FF4040":color.Hex,Count=kind=="rainbow"?72:20,StayCount=5,Keys=targets.ToList()});
            AnimatePreview.IsChecked=true;
        }
        RefreshLibraries();Changed();RefreshSelection();SetResult("Lighting preview updated. Apply to keyboard when ready.");
    }
    void RefreshColorControls()
    {
        Picker.SetColor(color);SelectedColorChip.Background=Brush(color.Hex);SelectedColorText.Text=color.Hex;
        var colors=selectedKeys.Select(k=>profile.KeyColors.GetValueOrDefault(k,profile.Color)).Distinct().Take(2).Count();
        ColorSelectionHint.Text=selectedKeys.Count==0?"Select keys to paint":colors>1?"Mixed colors · picking a shade replaces the selection's colors":"Picking a shade paints the selected keys";
        var active=profile.Effects.FirstOrDefault(e=>e.Id==profile.EffectId);
        LightingStatus.Text=active is null?"Solid colors":active.EditableZones?string.Join(" + ",active.Rules.Select(r=>r.Type==1?"Rainbow":"Breathing").Distinct()):active.Name;
        KeyGlowStatus.Text=selectedKeys.Any(k=>profile.KeyEffects.ContainsKey(k))?"◆ Key-press glow assigned to selected keys":"No key-press glow on this selection";
        foreach(var (kind,card) in presetCards)
        {
            bool on=kind=="solid"?active is null:kind=="pulse"?selectedKeys.Any(k=>profile.KeyEffects.ContainsKey(k)):active?.EditableZones==true&&active.Rules.Any(r=>r.Type==(kind=="rainbow"?1:2)&&r.Keys.Intersect(selectedKeys).Any());
            card.BorderBrush=Brush(on?"#68E4CC":"#354858");card.Background=Brush(on?"#213C43":"#14212D");
        }
    }
    void LightingScopeChanged(object sender,RoutedEventArgs e){if(ready)RefreshColorControls();}
    void PreviewKeyGlowClicked(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        var id=selectedKeys.Select(k=>profile.KeyEffects.GetValueOrDefault(k)).FirstOrDefault(id=>id!=Guid.Empty);
        keyEffectPreview=profile.Effects.FirstOrDefault(e=>e.Id==id)??throw new FormatException("Choose a key-press effect for the selected keys first.");
        previewClock.Restart();AnimatePreview.IsChecked=true;RefreshPreview();
    });
}
