using System.Diagnostics;

namespace Waive.Core;

public sealed partial class LightingController
{
    public async Task ApplyProfileLiveAsync(KeyboardProfile profile,IReadOnlyDictionary<int,uint> defaults,int slots,IProgress<int>? progress=null,CancellationToken ct=default)
    {
        if(profile.FnKeys.Count>0)throw new FormatException("Fn remapping is available in onboard layers 2–4.");
        var compiled=ProfileCompiler.Compile(profile,defaults,slots);
        var effect=profile.Effects.FirstOrDefault(e=>e.Id==profile.EffectId);
        var frame=ProfileCompiler.Frame(profile,effect,0);
        await operation.WaitAsync(ct);
        try
        {
            await transport.RequestAsync(Protocol.Ping,ct);
            var mode=await transport.RequestAsync(Protocol.Mode(5),ct);
            if(!Protocol.Mode(5).MatchesResponse(mode))throw new IOException("The keyboard did not enter live mode.");
            await transport.RequestAsync(Protocol.Ping,ct);
            await SendCheckedAsync(Protocol.KeyReports(false),ct);
            foreach(var (offset,data) in Protocol.Chunks(compiled.Keys))await SendCheckedAsync(Protocol.KeyTable(1,offset,data),ct);
            foreach(var (offset,data) in Protocol.Chunks(compiled.KeyEffects.AsSpan(0,slots).ToArray()))await SendCheckedAsync(Protocol.KeyTable(3,offset,data),ct);
            await FrameInternal(frame,ct);
            await SendCheckedAsync(Protocol.KeyReports(compiled.NeedsReports),ct);
            progress?.Report(100);
        }
        finally{operation.Release();}
    }
    public async Task ApplyProfileStoredAsync(KeyboardProfile profile,IReadOnlyDictionary<int,uint> defaults,int slots,IProgress<int>? progress=null,CancellationToken ct=default)
    {
        var plan=ProfileCompiler.StoredPlan(profile,defaults,slots);
        await operation.WaitAsync(ct);
        try
        {
            await transport.RequestAsync(Protocol.Ping,ct);
            var mode=await transport.RequestAsync(Protocol.Mode(5),ct);
            if(!Protocol.Mode(5).MatchesResponse(mode))throw new IOException("The keyboard did not enter configuration mode.");
            await transport.RequestAsync(Protocol.Ping,ct);
            await SendCheckedAsync(Protocol.KeyReports(false),ct);
            for(int i=0;i<plan.Count;i++){await SendCheckedAsync(plan[i].Command,ct);progress?.Report((i+1)*95/plan.Count);await Task.Delay(10,ct);}
            await RestoreOnboardInternal(profile.Mode,ct);progress?.Report(100);
        }
        finally{operation.Release();}
    }
    public async Task WriteFrameAsync(byte[] frame,CancellationToken ct=default)
    {
        if(frame.Length!=132*4)throw new ArgumentException("A live frame needs 132 RGB slots.");
        await operation.WaitAsync(ct);
        try{await FrameInternal(frame,ct);}finally{operation.Release();}
    }
    async Task FrameInternal(byte[] frame,CancellationToken ct)
    {
        foreach(var (offset,data) in Protocol.Chunks(frame))await SendCheckedAsync(Protocol.LiveColor(offset,data),ct);
        await SendCheckedAsync(Protocol.CommitLive,ct);
    }
}

public sealed class LivePlayback(ITransport transport,LightingController controller,KeyboardProfile profile) : IAsyncDisposable
{
    readonly CancellationTokenSource stop=new();
    readonly Dictionary<int,CancellationTokenSource> macros=[];
    readonly object sync=new();
    Task? effectsTask;
    readonly List<Task> tasks=[];
    LightingEffect? pulse;
    long pulseStart;
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly SemaphoreSlim animationSignal=new(0,1);
    public event Action<Exception>? Failed;
    public event Action<string>? OpenRequested;
    public bool HasAnimation => profile.EffectId is not null || profile.KeyEffects.Count>0;
    public void Start()
    {
        if(HasAnimation)effectsTask=Task.Run(RenderAsync);
    }
    public void Key(int index,bool down)
    {
        if(stop.IsCancellationRequested)return;
        if(down&&profile.KeyEffects.TryGetValue(index,out var effectId))
        {
            lock(sync){pulse=profile.Effects.First(e=>e.Id==effectId);pulseStart=clock.ElapsedMilliseconds;}
            if(animationSignal.CurrentCount==0)try{animationSignal.Release();}catch(SemaphoreFullException){}
        }
        if(!profile.Keys.TryGetValue(index,out var binding))return;
        if(binding.StartsWith("open:")){if(down)OpenRequested?.Invoke(binding[5..]);return;}
        if(!binding.StartsWith("macro:"))return;
        var definition=profile.Macros.Single(m=>m.Id==Guid.Parse(binding[6..]));
        lock(sync)
        {
            if(macros.TryGetValue(index,out var active))
            {
                if((!down&&definition.StopMode==2)||(down&&definition.StopMode==3))active.Cancel();
                return;
            }
            if(!down)return;
            // Key and mouse report state is device-global: serialize macro playback.
            if(macros.Count>0)return;
            var cancellation=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);macros[index]=cancellation;
            var task=Task.Run(async()=>{try{await PlayMacroAsync(definition,cancellation.Token);}catch(OperationCanceledException){}catch(Exception error){Failed?.Invoke(error);}finally{lock(sync)macros.Remove(index);cancellation.Dispose();}});
            tasks.Add(task);tasks.RemoveAll(t=>t.IsCompleted);
        }
    }
    async Task RenderAsync()
    {
        var baseEffect=profile.Effects.FirstOrDefault(e=>e.Id==profile.EffectId);
        var lastWasPulse=false;
        try
        {
            while(!stop.IsCancellationRequested)
            {
                var elapsed=clock.ElapsedMilliseconds;
                LightingEffect? effect;long tick;
                lock(sync)
                {
                    if(pulse is not null && elapsed-pulseStart>=pulse.DurationTicks*50L)pulse=null;
                    effect=pulse??baseEffect;tick=pulse is not null?(elapsed-pulseStart)/50:elapsed/50;
                }
                if(effect is null&&!lastWasPulse){await animationSignal.WaitAsync(stop.Token);continue;}
                var frame=ProfileCompiler.Frame(profile,effect,tick);
                lastWasPulse=effect is not null && effect!=baseEffect;
                await controller.WriteFrameAsync(frame,stop.Token);
                await Task.Delay(100,stop.Token);
            }
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested){}
        catch(Exception error){Failed?.Invoke(error);}
    }
    async Task PlayMacroAsync(MacroDefinition macro,CancellationToken ct)
    {
        byte modifier=0,mouse=0;var held=new List<byte>();
        async Task SendKeys(CancellationToken token){var data=new byte[6];data[0]=modifier;held.CopyTo(0,data,1,held.Count);var cmd=new Command(0x15,2,0,data);Protocol.RequireAck(cmd,await transport.RequestAsync(cmd,token));}
        async Task SendMouse(CancellationToken token){var cmd=new Command(0x15,1,0,[mouse]);Protocol.RequireAck(cmd,await transport.RequestAsync(cmd,token));}
        try
        {
            int repetitions=macro.StopMode==1?macro.Repeats:int.MaxValue;
            for(int run=0;run<repetitions;run++)
            {
                ct.ThrowIfCancellationRequested();
                foreach(var e in macro.Events)
                {
                    ct.ThrowIfCancellationRequested();
                    if(e.Kind=="Delay"){await Task.Delay(int.Parse(e.Value),ct);continue;}
                    bool down=e.Kind.EndsWith("Down",StringComparison.Ordinal);
                    if(e.Kind.StartsWith("Mouse")){var mask=(byte)(e.Value=="Left"?1:e.Value=="Right"?2:4);mouse=down?(byte)(mouse|mask):(byte)(mouse&~mask);await SendMouse(ct);}
                    else
                    {
                        var key=KeyCatalog.Normalize(e.Value);
                        if(KeyCatalog.Modifiers.TryGetValue(key,out var bit))modifier=down?(byte)(modifier|bit):(byte)(modifier&~bit);
                        else {var usage=KeyCatalog.Usages[key];if(down&&!held.Contains(usage)){if(held.Count==5)throw new FormatException("A macro cannot hold more than five keys at once.");held.Add(usage);}else if(!down)held.Remove(usage);}
                        await SendKeys(ct);
                    }
                }
                // Release between repeats so a missing final key-up cannot leave a key held.
                modifier=0;mouse=0;held.Clear();await SendKeys(ct);await SendMouse(ct);
                await Task.Delay(5,ct);
            }
        }
        finally
        {
            modifier=0;mouse=0;held.Clear();
            try{await SendKeys(CancellationToken.None);await SendMouse(CancellationToken.None);}catch{/* disconnect */}
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        Task[] active;lock(sync)active=tasks.ToArray();
        if(effectsTask is not null)await effectsTask;
        await Task.WhenAll(active);stop.Dispose();
    }
}
