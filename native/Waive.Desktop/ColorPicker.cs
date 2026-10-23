using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Waive.Core;

namespace Waive.Desktop;

// Native vector gradients: no bitmap allocation or idle rendering timer.
public sealed class ColorPicker : FrameworkElement
{
    double hue=168,saturation=.55,value=.9;
    bool dragging,hueDrag;
    public event Action<Rgb>? ColorChanged;
    public event Action? GestureStarted,GestureCompleted;
    public ColorPicker()
    {
        Height=164;MinWidth=180;Focusable=true;Cursor=Cursors.Cross;
        ToolTip="Drag to pick a shade. The rainbow strip chooses hue. Arrow keys adjust shade; Ctrl+arrows adjust hue.";
        System.Windows.Automation.AutomationProperties.SetName(this,"Color picker. Arrows adjust saturation and lightness; Control arrows adjust hue.");
    }
    Rect Shades=>new(0,0,ActualWidth,Math.Max(1,ActualHeight-32));
    Rect Hues=>new(0,ActualHeight-20,ActualWidth,16);
    public void SetColor(Rgb c)
    {
        double r=c.R/255d,g=c.G/255d,b=c.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        if(delta>0)hue=((max==r?60*((g-b)/delta):max==g?60*((b-r)/delta+2):60*((r-g)/delta+4))+360)%360;
        saturation=max==0?saturation:delta/max;value=max;InvalidateVisual();
    }
    public Rgb ColorAt(double saturation,double value)
    {
        var c=ColorMath.HueShift(new Rgb(255,0,0),hue);
        return new((byte)Math.Round((255+(c.R-255)*saturation)*value),(byte)Math.Round((255+(c.G-255)*saturation)*value),(byte)Math.Round((255+(c.B-255)*saturation)*value));
    }
    protected override void OnRender(DrawingContext dc)
    {
        var pure=ColorMath.HueShift(new Rgb(255,0,0),hue);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(pure.R,pure.G,pure.B)),null,Shades,5,5);
        dc.DrawRectangle(new LinearGradientBrush(Colors.White,Colors.Transparent,0),null,Shades);
        dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent,Colors.Black,90),null,Shades);
        var gradient=new LinearGradientBrush{StartPoint=new(0,.5),EndPoint=new(1,.5)};
        for(int i=0;i<=6;i++){var c=ColorMath.HueShift(new Rgb(255,0,0),i*60);gradient.GradientStops.Add(new(Color.FromRgb(c.R,c.G,c.B),i/6d));}
        dc.DrawRoundedRectangle(gradient,null,Hues,5,5);
        var point=new Point(saturation*Shades.Width,(1-value)*Shades.Height);
        dc.DrawEllipse(null,new Pen(Brushes.Black,4),point,5,5);dc.DrawEllipse(null,new Pen(Brushes.White,2),point,5,5);
        var hueX=hue/360*ActualWidth;dc.DrawRoundedRectangle(null,new Pen(Brushes.Black,4),new Rect(hueX-3,Hues.Y-2,6,20),2,2);dc.DrawRoundedRectangle(null,new Pen(Brushes.White,2),new Rect(hueX-3,Hues.Y-2,6,20),2,2);
        if(IsKeyboardFocused)dc.DrawRectangle(null,new Pen(Brushes.White,1),new Rect(0,0,ActualWidth,ActualHeight));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();var p=e.GetPosition(this);if(!Shades.Contains(p)&&!Hues.Contains(p))return;
        hueDrag=Hues.Contains(p);GestureStarted?.Invoke();dragging=true;CaptureMouse();Pick(p);e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e){if(dragging){if(e.LeftButton!=MouseButtonState.Pressed){End();return;}Pick(e.GetPosition(this));}}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){if(dragging){Pick(e.GetPosition(this));End();e.Handled=true;}}
    protected override void OnLostMouseCapture(MouseEventArgs e){if(dragging){dragging=false;GestureCompleted?.Invoke();}}
    void End(){if(!dragging)return;dragging=false;ReleaseMouseCapture();GestureCompleted?.Invoke();}
    void Pick(Point p)
    {
        if(hueDrag)hue=Math.Clamp(p.X/Math.Max(1,ActualWidth),0,.99999)*360;
        else{saturation=Math.Clamp(p.X/Math.Max(1,Shades.Width),0,1);value=1-Math.Clamp(p.Y/Shades.Height,0,1);}
        ColorChanged?.Invoke(ColorAt(saturation,value));InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))return;
        var direction=e.Key is Key.Right or Key.Up?1:-1;
        GestureStarted?.Invoke();
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control))hue=(hue+direction*3+360)%360;
        else if(e.Key is Key.Left or Key.Right)saturation=Math.Clamp(saturation+direction*.02,0,1);
        else value=Math.Clamp(value+direction*.02,0,1);
        ColorChanged?.Invoke(ColorAt(saturation,value));GestureCompleted?.Invoke();InvalidateVisual();e.Handled=true;
    }
}
