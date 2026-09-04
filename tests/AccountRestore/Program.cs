using System.Text.Json;
using System.IO;
using GrevHome.Online;
using GrevHome.Runtime;
using GrevHome.Storage;

var root = Path.Combine(Path.GetTempPath(),"GrevHome-account-test-"+Guid.NewGuid().ToString("N"));
var paths = new AppPaths(root);
const string grevId="GTESTLOCAL";
paths.EnsureProfileLayout(grevId);
var connection=Path.Combine(paths.GetProfileConnections(grevId),"GrevDad");
Directory.CreateDirectory(connection);
try
{
    await File.WriteAllTextAsync(Path.Combine(connection,"link.json"),"{\"account\":{\"userId\":\"account-a\"}}");
    var local = new PlaytimeSnapshot(2,new Dictionary<string,AppPlaytimeStat> {
        ["pcsx2"]=new("pcsx2","PCSX2",90,1,DateTimeOffset.FromUnixTimeSeconds(1000))
    });
    await File.WriteAllTextAsync(paths.GetProfilePlaytimeFile(grevId),JsonSerializer.Serialize(local));
    var cloud=new GrevDadAccountData(true,1,"account-a","Joe","Joe",100,1000,[
        new(grevId,200,60,1,1,[new("pcsx2","PCSX2",60,1,900)],1000),
        new("GOTHERPC",300,120,1,1,[new("pcsx2","PCSX2",120,1,950)],1000)
    ]);
    await GrevDadAccountDataStore.SaveAsync(paths,grevId,cloud,default);
    var playtime=new PlaytimeService(paths);
    for(var i=0;i<3;i++)
    {
        var display=await playtime.GetForGrevIdAsync(grevId);
        Check(display.Apps["pcsx2"].TotalSeconds==210,"Local unsynced delta plus remote source, no double counting");
        Check(display.Apps["pcsx2"].SessionCount==2,"Session totals must merge by source");
        Check((await playtime.GetLocalForGrevIdAsync(grevId)).Apps["pcsx2"].TotalSeconds==90,"Cloud must never enter upload snapshot");
    }
    await File.WriteAllTextAsync(paths.GetProfilePlaytimeFile(grevId),JsonSerializer.Serialize(new PlaytimeSnapshot(2,new Dictionary<string,AppPlaytimeStat>())));
    Check((await playtime.GetForGrevIdAsync(grevId)).Apps["pcsx2"].TotalSeconds==180,"Empty local data must preserve cloud history");
    await File.WriteAllTextAsync(Path.Combine(connection,"link.json"),"{\"account\":{\"userId\":\"account-b\"}}");
    Check((await playtime.GetForGrevIdAsync(grevId)).Apps.Count==0,"Another account must never see cached account data");
    File.Delete(Path.Combine(connection,"link.json"));
    Check((await playtime.GetForGrevIdAsync(grevId)).Apps.Count==0,"Unlinked profile must not read cloud cache");
    Console.WriteLine("Account restore tests passed: source merge, offline delta, replay, empty local data and account isolation.");
}
finally { Directory.Delete(root,true); }

static void Check(bool value,string message) { if(!value) throw new Exception(message); }
