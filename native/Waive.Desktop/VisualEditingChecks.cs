using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Waive.Core;

namespace Waive.Desktop;

public partial class MainWindow
{
    // Runs only under the explicit --smoke flag. Exercises the editor without Apply.
    void CheckVisualEditing(Dictionary<string,bool> checks,string output)
    {
        var saved=profile.Clone();
        var row=keyBounds.GroupBy(k=>k.Y).First(g=>g.Count()>=5).OrderBy(k=>k.X).Take(5).ToArray();
        static Point Center(KeyBounds key)=>new(key.X+key.Width/2,key.Y+key.Height/2);
        BeginKeyDrag(-1,new Point(row[0].X-5,Center(row[0]).Y),ModifierKeys.None);ContinueKeyDrag(Center(row[0]));EndKeyDrag();
        checks["dragCanStartBetweenKeys"]=selectedKeys.SetEquals(new[]{row[0].Index});
        BeginKeyDrag(row[0].Index,Center(row[0]),ModifierKeys.None);ContinueKeyDrag(Center(row[2]));EndKeyDrag();
        checks["dragSelectsIntermediateKeys"]=selectedKeys.SetEquals(row.Take(3).Select(k=>k.Index));
        BeginKeyDrag(row[4].Index,Center(row[4]),ModifierKeys.Control);EndKeyDrag();
        checks["controlDragAddsToSelection"]=selectedKeys.Count==4&&selectedKeys.Contains(row[4].Index);
        BeginKeyDrag(row[1].Index,Center(row[1]),ModifierKeys.Alt);ContinueKeyDrag(Center(row[1]));EndKeyDrag();
        checks["altDragRemovesWithoutToggling"]=selectedKeys.Count==3&&!selectedKeys.Contains(row[1].Index);
        var selection=selectedKeys.ToHashSet();ContinueKeyDrag(Center(row[3]));
        checks["releasedDragCannotChangeSelection"]=selectedKeys.SetEquals(selection)&&!KeyboardCanvas.IsMouseCaptured;
        int undoCount=undo.Count;BeginColorEdit();HexBox.Text="#FF4040";HexBox.Text="#FF6040";HexBox.Text="#FF8040";EndColorEdit();
        checks["colorGestureCreatesOneUndo"]=undo.Count==undoCount+1;
        checks["groupColorOnlyPaintsSelection"]=selection.All(k=>profile.KeyColors.GetValueOrDefault(k)=="#FF8040")&&!profile.KeyColors.ContainsKey(row[3].Index);
        SelectionScope.IsChecked=true;ApplyLightingPreset("rainbow");
        var look=profile.Effects.Single(e=>e.Id==profile.EffectId);
        checks["rainbowTargetsSelectedKeys"]=look.EditableZones&&look.Rules.Single().Keys.ToHashSet().SetEquals(selection);
        var outside=row[3].Index;
        checks["rainbowPreservesOtherKeyColors"]=look.FrameAt(7)[outside]==profile.KeyColors.GetValueOrDefault(outside,profile.Color);
        selectedKeys.Clear();selectedKeys.Add(outside);RefreshSelection();ApplyLightingPreset("breathing");
        checks["differentGroupsKeepDifferentEffects"]=look.Rules.Count==2&&look.Rules.First(r=>r.Type==1).Keys.ToHashSet().SetEquals(selection)&&look.Rules.First(r=>r.Type==2).Keys.SequenceEqual(new[]{outside});
        HexBox.Text="#5599FF";
        checks["paintingStopsMotionOnlyOnPaintedKeys"]=look.Rules.Count==1&&look.Rules[0].Type==1&&look.Frames[0].Data[outside]=="#5599FF";
        ApplyLightingPreset("pulse");
        checks["keyGlowAssignedToSelection"]=profile.KeyEffects.Count==1&&profile.KeyEffects.ContainsKey(outside);
        var cap=keycaps.First(k=>slotsByKey[k]==outside);
        checks["keyGlowBadgeVisible"]=((Border)((Grid)cap.Child).Children[1]).Visibility==Visibility.Visible;
        UndoClicked(this,new RoutedEventArgs());checks["undoRemovesKeyGlow"]=!profile.KeyEffects.ContainsKey(outside);
        RedoClicked(this,new RoutedEventArgs());checks["redoRestoresKeyGlow"]=profile.KeyEffects.ContainsKey(outside);
        checks["pickerBlackAndWhiteEndpoints"]=Picker.ColorAt(1,0)==new Rgb(0,0,0)&&Picker.ColorAt(0,1)==new Rgb(255,255,255);
        var source=PresentationSource.FromVisual(Picker);
        if(source is not null)
        {
            var before=color;Picker.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Down){RoutedEvent=Keyboard.KeyDownEvent});
            checks["pickerSupportsKeyboard"]=color!=before;
        }
        var compile=profile.Clone();compile.Mode=2;ProfileCompiler.StoredPlan(compile,defaultKeys);
        checks["visualPresetsCompileForOnboard"]=true;
        profile=new(){Color="#000000"};SyncProfileControls();
        var q=keyBounds.Single(k=>k.Index==26);var p=keyBounds.Single(k=>k.Index==25);
        BeginKeyDrag(26,Center(q),ModifierKeys.None);ContinueKeyDrag(Center(p));EndKeyDrag();HexBox.Text="#FF0000";
        checks["dragQtoPSelectsOnlyLetterRow"]=selectedKeys.SetEquals(new[]{26,32,14,27,29,34,30,18,24,25});
        int[] Lit(byte[] frame)=>Enumerable.Range(0,frame.Length/4).Where(i=>frame[i*4]!=0||frame[i*4+1]!=0||frame[i*4+2]!=0).ToArray();
        var frame=ProfileCompiler.Frame(profile,null,0);
        var qCap=keycaps.Single(k=>slotsByKey[k]==26);var oneCap=keycaps.Single(k=>slotsByKey[k]==36);
        checks["previewAndPayloadAgreeOnQtoP"]=Lit(frame).SequenceEqual(Enumerable.Range(46,10))&&((System.Windows.Media.SolidColorBrush)qCap.Background).Color.R>150&&((System.Windows.Media.SolidColorBrush)oneCap.Background).Color.R<40;
        ApplyLightingPreset("rainbow");look=profile.Effects.Single(e=>e.Id==profile.EffectId);
        checks["QtoPRainbowUsesLetterLEDs"]=Lit(ProfileCompiler.Frame(profile,look,8)).SequenceEqual(Enumerable.Range(46,10));
        ApplyLightingPreset("pulse");var pulse=profile.Effects.Single(e=>e.Id==profile.KeyEffects[26]);
        checks["QtoPGlowUsesLetterLEDs"]=Lit(ProfileCompiler.Frame(profile,pulse,8)).SequenceEqual(Enumerable.Range(46,10));
        keyEffectPreview=null;AnimatePreview.IsChecked=false;
        RefreshSelection();CaptureSmokeImage(System.IO.Path.Combine(output,"drag-selection-and-effects.png"));
        profile=saved;keyEffectPreview=null;AnimatePreview.IsChecked=false;SyncProfileControls();SelectAllClicked(this,new RoutedEventArgs());
    }
}
