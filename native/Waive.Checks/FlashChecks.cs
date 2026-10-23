using System.Text.Json;
using Waive.Core;

static class FlashChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        using var captures=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"reference","native-flash-replies.json")));
        foreach(var item in captures.RootElement.EnumerateArray())
        {
            var request=Convert.FromHexString(item.GetProperty("request").GetString()!);var response=Convert.FromHexString(item.GetProperty("response").GetString()!);
            var command=new Command(request[0],request[1]);bool accepted;
            try{Protocol.RequireAck(command,response);accepted=true;}catch(IOException){accepted=false;}
            check(command.MatchesResponse(response)&&accepted==item.GetProperty("accepted").GetBoolean(),"captured flash reply: "+item.GetProperty("name").GetString());
        }
        check(!new Command(0x27,4).MatchesResponse([0x24,1,0]),"flash ACK cannot complete a different opcode");
        try{Protocol.RequireAck(new(0x27,4),[0x27,0,0]);throw new Exception("Zero data-write status accepted");}catch(IOException){check(true,"erase-only zero status does not accept failed data writes");}
        var profile=new KeyboardProfile{Mode=4};var defaults=new Dictionary<int,uint>();
        var plan=ProfileCompiler.StoredPlan(profile,defaults);
        check(plan.All(s=>s.Command.Code is not (0x22 or 0x31 or 0x25)&&!(s.Command.Code==0x21&&s.Command.Header is 1 or 4 or 7)),"lighting-only profile preserves onboard key and macro regions");
        check(plan.All(s=>s.Command.Code!=0x24)&&plan.Any(s=>s.Command.Code==0x27),"static lighting uses supported effect storage, not the legacy RGB buffer");
        profile.Mode=1;plan=ProfileCompiler.StoredPlan(profile,defaults);
        check(plan.All(s=>s.Command.Code==0x27||(s.Command.Code==0x21&&s.Command.Header==6)),"built-in layer changes only lighting-effect storage");
        var effect=ProfileCompiler.Compile(profile,defaults,onboard:true).EffectBytes;
        check(Enumerable.Range(1,4).All(i=>effect.AsSpan(0,16).SequenceEqual(effect.AsSpan(i*16,16))),"all five built-in effect slots reference the chosen look");
        using var fake=new FakeTransport();var controller=new LightingController(fake);await controller.ApplyProfileStoredAsync(profile,defaults,112);
        var firstWrite=fake.Commands.FindIndex(c=>c.IsStoredWrite);var firstMode=fake.Commands.FindIndex(c=>c==Protocol.Mode(5));
        check(firstMode>=0&&firstMode<firstWrite&&fake.Commands.Last()==Protocol.Mode(1),"flash enters configuration mode and returns to onboard mode");
        fake.Commands.Clear();await controller.RestoreOnboardAsync();
        check(fake.Commands.SequenceEqual(new[]{Protocol.Mode(1)}),"onboard return never waits for unsupported host-report commands");
    }
}
