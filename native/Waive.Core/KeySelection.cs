namespace Waive.Core;

public readonly record struct KeyBounds(int Index,double X,double Y,double Width,double Height)
{
    public bool Crosses(double x0,double y0,double x1,double y1)
    {
        double start=0,end=1,dx=x1-x0,dy=y1-y0;
        bool Clip(double p,double q)
        {
            if(p==0)return q>=0;
            var t=q/p;
            if(p<0){if(t>end)return false;start=Math.Max(start,t);}
            else {if(t<start)return false;end=Math.Min(end,t);}
            return true;
        }
        return Clip(-dx,x0-X)&&Clip(dx,X+Width-x0)&&Clip(-dy,y0-Y)&&Clip(dy,Y+Height-y0);
    }
}

public sealed class KeySelectionStroke(HashSet<int> selected,bool remove)
{
    readonly HashSet<int> visited=[];
    public bool Visit(int index)
    {
        if(!visited.Add(index))return false;
        return remove?selected.Remove(index):selected.Add(index);
    }
}
