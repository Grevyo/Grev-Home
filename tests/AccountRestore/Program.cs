using System.Text.Json;
using System.IO;
using GrevHome.Online;
using GrevHome.Runtime;
using GrevHome.Storage;
using GrevHome.Profiles;

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
    var polluted = new PlaytimeSnapshot(2,new Dictionary<string,AppPlaytimeStat> {
        ["steam"]=new("steam","Steam",360000,9,DateTimeOffset.FromUnixTimeSeconds(990)),
        ["discord"]=new("discord","Discord",180000,4,DateTimeOffset.FromUnixTimeSeconds(991)),
        ["steam.game.123"]=new("steam.game.123","Real Steam Game",600,1,DateTimeOffset.FromUnixTimeSeconds(992)),
        ["pcsx2"]=local.Apps["pcsx2"]
    });
    await File.WriteAllTextAsync(paths.GetProfilePlaytimeFile(grevId),JsonSerializer.Serialize(polluted));
    Check(await playtime.RemoveLegacyLauncherAggregatesAsync(grevId),"Legacy launcher cleanup must report a repair");
    var repairedLocal=await playtime.GetLocalForGrevIdAsync(grevId);
    Check(!repairedLocal.Apps.ContainsKey("steam") && !repairedLocal.Apps.ContainsKey("discord"),"Steam and Discord launcher aggregates must be removed");
    Check(repairedLocal.Apps.ContainsKey("steam.game.123") && repairedLocal.Apps.ContainsKey("pcsx2"),"Games and emulators must remain untouched");
    Check(!await playtime.RemoveLegacyLauncherAggregatesAsync(grevId),"Launcher repair must be idempotent");
    await playtime.RecordSessionAsync("steam","Steam",[new(Guid.NewGuid(),grevId,"Test",GrevHome.Sessions.AccountKind.Local)],TimeSpan.FromSeconds(60),DateTimeOffset.UtcNow);
    Check(!await new PlaytimeService(paths).RemoveLegacyLauncherAggregatesAsync(grevId),"Repair marker must survive later session writes and service recreation");
    Check((await playtime.GetLocalForGrevIdAsync(grevId)).Apps["steam"].TotalSeconds==60,"New foreground usage must survive the one-time repair");
    await File.WriteAllTextAsync(paths.GetProfilePlaytimeFile(grevId),JsonSerializer.Serialize(local));
    for(var i=0;i<3;i++)
    {
        var display=await playtime.GetForGrevIdAsync(grevId);
        Check(display.Apps["pcsx2"].TotalSeconds==210,"Local unsynced delta plus remote source, no double counting");
        Check(display.Apps["pcsx2"].SessionCount==2,"Session totals must merge by source");
        Check((await playtime.GetLocalForGrevIdAsync(grevId)).Apps["pcsx2"].TotalSeconds==90,"Cloud must never enter upload snapshot");
    }
    cloud = cloud with {
        SharedProgression = new SharedAccountProgression(1143,3,500,143),
        Achievements = [new CloudAchievement("site:test","Website award","Earned on Grev.dad","Website",1000),
            new CloudAchievement("grev-home:first-session","Home: First Boot","First session","Grev Home",1000)]
    };
    await GrevDadAccountDataStore.SaveAsync(paths,grevId,cloud,default);
    var statsService = new ProfileStatsService([new GrevHomeProfileStatsSource(playtime)],paths);
    for(var i=0;i<3;i++)
    {
        var stats = await statsService.GetAsync(grevId,[]);
        Check(stats.Progression.TotalXp==1143 && stats.Progression.Level==3,"Linked profile must use website balance and level rule without recrediting cloud XP");
        Check(stats.Milestones.Count(m=>m.MilestoneId=="first-session")==1,"Mirrored Home milestone must not be duplicated");
        Check(stats.Milestones.Any(m=>m.MilestoneId=="site:test" && m.IsEarned),"Website achievement must appear locally");
    }
    await File.WriteAllTextAsync(paths.GetProfilePlaytimeFile(grevId),JsonSerializer.Serialize(new PlaytimeSnapshot(2,new Dictionary<string,AppPlaytimeStat>())));
    Check((await playtime.GetForGrevIdAsync(grevId)).Apps["pcsx2"].TotalSeconds==180,"Empty local data must preserve cloud history");
    await File.WriteAllTextAsync(Path.Combine(connection,"link.json"),"{\"account\":{\"userId\":\"account-b\"}}");
    Check((await playtime.GetForGrevIdAsync(grevId)).Apps.Count==0,"Another account must never see cached account data");
    Check(!(await statsService.GetAsync(grevId,[])).Milestones.Any(m=>m.MilestoneId=="site:test"),"Another account must not inherit shared achievements");
    File.Delete(Path.Combine(connection,"link.json"));
    Check((await playtime.GetForGrevIdAsync(grevId)).Apps.Count==0,"Unlinked profile must not read cloud cache");
    // Exact shape grev.dad's GET /api/grev-home/account-data returns (src/grev-home-sync.ts),
    // including the cloud save list a newly linked PC uses to offer restores.
    const string serverAccountData = """
        {"ok":true,"apiVersion":1,"userId":"account-a","username":"Joe","displayName":"Joe","accountCreatedAt":100,
         "downloadedAt":1000,"sharedProgression":{"totalXp":1143,"level":3,"xpPerLevel":500,"homeTotalXp":143},
         "achievements":[{"id":"site:test","name":"Website award","description":"Earned","source":"Website","awardedAt":1000}],
         "sources":[{"grevId":"GTESTLOCAL","profileCreatedAt":200,"totalSeconds":60,"completedSessions":1,"uniqueApps":1,
           "apps":[{"appId":"pcsx2","appName":"PCSX2","totalSeconds":60,"sessionCount":1,"lastPlayedAt":900}],"updatedAt":1000}],
         "cloudSaves":[{"appId":"pcsx2","sizeBytes":2048,"updatedAtUtc":"2026-10-01T12:00:00.123Z"}]}
        """;
    var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var parsed = JsonSerializer.Deserialize<GrevDadAccountData>(serverAccountData, webJson)!;
    Check(parsed.CloudSaves is [{ AppId: "pcsx2", SizeBytes: 2048 }], "Cloud save summaries must parse from account-data");
    Check(parsed.CloudSaves![0].UpdatedAtUtc == DateTimeOffset.Parse("2026-10-01T12:00:00.123Z"), "Cloud save timestamps must keep millisecond precision");
    await File.WriteAllTextAsync(Path.Combine(connection,"link.json"),"{\"account\":{\"userId\":\"account-a\"}}");
    await GrevDadAccountDataStore.SaveAsync(paths, grevId, parsed, default);
    Check((await GrevDadAccountDataStore.ReadAsync(paths, grevId))?.CloudSaves?.Length == 1, "Cloud save summaries must survive the local account-data cache");
    var olderServer = JsonSerializer.Deserialize<GrevDadAccountData>(
        """{"ok":true,"apiVersion":1,"userId":"account-a","username":"Joe","displayName":"Joe","accountCreatedAt":100,"downloadedAt":1000,"sources":[]}""",
        webJson)!;
    Check(olderServer.CloudSaves is null, "Account data from a server without cloud saves must still parse");

    Console.WriteLine("Account restore tests passed: source merge, offline delta, replay, empty local data, account isolation and cloud save listing.");
}
finally { Directory.Delete(root,true); }

static void Check(bool value,string message) { if(!value) throw new Exception(message); }
