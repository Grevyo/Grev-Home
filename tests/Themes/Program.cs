using System.IO;
using GrevHome.Presentation;
using GrevHome.Storage;

// Theme resolution across the Admin's machine scope and each GrevID's private scope.
var root = Path.Combine(Path.GetTempPath(), "GrevHome-themes-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new AppPaths(root);
    const string grevId = "GTESTThemes234";
    paths.EnsureProfileLayout(grevId);
    var service = new ThemeService(paths);

    // Fresh machine: everyone sees Grev Default.
    var machine = await service.LoadAsync();
    var profile = await service.LoadForProfileAsync(grevId);
    Check(service.ResolveForProfile(profile, machine).Id == ThemeCatalog.DefaultThemeId, "A fresh machine must use Grev Default");

    // The Admin creates a machine theme and makes it the default: a profile with no override follows it.
    var shared = ThemeCatalog.Default with { Id = "custom-family-1", Name = "Family", Accent = "#FF9A5A", IsBuiltIn = false };
    await service.SaveCustomThemeAsync(shared);
    await service.SetActiveThemeAsync(shared.Id);
    machine = await service.LoadAsync();
    Check(service.ResolveForProfile(profile, machine).Id == shared.Id, "A profile without an override must follow the machine default");

    // A profile can pick the Admin's machine theme for itself.
    var choices = service.ResolveAllForProfile(profile, machine);
    Check(choices.Any(theme => theme.Id == shared.Id), "Machine custom themes must be selectable from a profile");
    await service.SetActiveThemeAsync(ThemeCatalog.BuiltIn[2].Id);
    machine = await service.LoadAsync();
    await service.SetActiveThemeAsync(shared.Id, grevId);
    profile = await service.LoadForProfileAsync(grevId);
    Check(service.ResolveForProfile(profile, machine).Id == shared.Id, "A profile override may point at a machine theme");

    // If the Admin deletes that theme, the profile falls back to the machine default, not Grev Default.
    await service.DeleteCustomThemeAsync(shared.Id);
    machine = await service.LoadAsync();
    profile = await service.LoadForProfileAsync(grevId);
    Check(service.ResolveForProfile(profile, machine).Id == ThemeCatalog.BuiltIn[2].Id, "A missing override must fall back to the machine default");

    // Profile themes stay private to that GrevID.
    var mine = ThemeCatalog.Default with { Id = "custom-mine-1", Name = "Mine", IsBuiltIn = false };
    await service.SaveCustomThemeAsync(mine, grevId);
    profile = await service.LoadForProfileAsync(grevId);
    machine = await service.LoadAsync();
    Check(service.ResolveAll(machine).All(theme => theme.Id != mine.Id), "A profile theme must not appear in the machine scope");
    Check(service.ResolveAllForProfile(profile, machine).Any(theme => theme.Id == mine.Id), "A profile must see its own themes");

    // Export/import round trip never writes or activates anything by itself.
    var exported = await service.ExportThemeAsync(mine, paths.Downloads);
    var imported = await service.ImportThemeAsync(exported);
    Check(imported.Accent == mine.Accent && !imported.IsBuiltIn, "Import must return the exported colors");
    Check((await service.LoadForProfileAsync(grevId)).CustomThemes.Count == 1, "Import must not save a theme on its own");

    // Built-ins can never be overwritten or deleted, and broken files never break the list.
    await Expect<InvalidOperationException>(() => service.SaveCustomThemeAsync(ThemeCatalog.Default), "Built-ins must not be overwritten");
    await Expect<InvalidOperationException>(() => service.DeleteCustomThemeAsync(ThemeCatalog.DefaultThemeId), "Built-ins must not be deleted");
    await File.WriteAllTextAsync(Path.Combine(paths.GetProfileThemes(grevId), "broken.json"), "{ not json");
    Check((await service.LoadForProfileAsync(grevId)).CustomThemes.Count == 1, "A broken theme file must be skipped");

    Console.WriteLine("Theme tests passed: machine default, shared machine themes, override fallback, private profile themes, import/export and built-in protection.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

static void Check(bool value, string message) { if (!value) throw new Exception(message); }

static async Task Expect<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception(message);
}
