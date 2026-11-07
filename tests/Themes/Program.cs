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

    // Layouts: every console-style preset is valid and readable.
    foreach (var builtIn in ThemeCatalog.BuiltIn)
    {
        builtIn.Validate();
        Check(builtIn.GetContrastWarnings().Count == 0, $"{builtIn.Name} must pass the contrast checks: {string.Join(" ", builtIn.GetContrastWarnings())}");
    }
    foreach (var id in new[] { "ps5-style", "ps4-style", "xbox360-blades", "xbox360-tabs", "xbox-one-style", "wii-style", "switch-style" })
        Check(ThemeCatalog.FindBuiltIn(id)?.Layout is not null, $"Missing console-style preset {id}");

    // Classic reproduces the original Home exactly, and a theme saved before layouts existed uses it.
    Check(ThemeLayout.Classic.TileSize == (285, 145) && ThemeLayout.Classic.TileSpacing == 8 && ThemeLayout.Classic.EffectiveCornerRadius == 0,
        "Classic layout must keep the original 285 x 145 tiles");
    var legacyPath = Path.Combine(paths.GetProfileThemes(grevId), "legacy.json");
    await File.WriteAllTextAsync(legacyPath, """
        {"Id":"custom-legacy","Name":"Legacy","WindowBackground":"#090C12","CardBackground":"#11151E","CardBorder":"#3A465F",
         "Surface":"#151923","SurfaceHover":"#20283A","Accent":"#7EA6FF","Muted":"#97A0B3","AdminRole":"#D8B65A",
         "StandardRole":"#D94B55","GuestRole":"#747B88","IsBuiltIn":false}
        """);
    var legacy = (await service.LoadForProfileAsync(grevId)).CustomThemes.Single(theme => theme.Id == "custom-legacy");
    Check(legacy.Layout is null && legacy.EffectiveLayout == ThemeLayout.Classic && legacy.EffectiveText == "#FFFFFF",
        "A theme saved before layouts existed must load with the classic layout and white text");

    // A customised layout on top of a preset survives save and reload.
    var basedOnSwitch = ThemeCatalog.FindBuiltIn("switch-style")! with
    {
        Id = "custom-my-switch",
        Name = "My Switch",
        IsBuiltIn = false,
        Layout = ThemeCatalog.FindBuiltIn("switch-style")!.Layout! with { TileShape = HomeTileShape.Circle, TileScalePercent = 120, FontFamily = "Bahnschrift" }
    };
    await service.SaveCustomThemeAsync(basedOnSwitch, grevId);
    var reloaded = (await service.LoadForProfileAsync(grevId)).CustomThemes.Single(theme => theme.Id == "custom-my-switch");
    Check(reloaded.Layout == basedOnSwitch.Layout, "A custom layout must round-trip through save and load");
    Check(reloaded.EffectiveLayout.EffectiveCornerRadius == reloaded.EffectiveLayout.TileSize.Width / 2, "Circle tiles must be fully round");

    // Out-of-range layouts are refused when imported.
    var broken = basedOnSwitch with { Layout = basedOnSwitch.Layout! with { TileScalePercent = 500 } };
    await Expect<InvalidOperationException>(async () => broken.Validate(), "An out-of-range tile size must be rejected");
    await Expect<InvalidOperationException>(async () => (basedOnSwitch with { Layout = basedOnSwitch.Layout! with { FontFamily = "Comic Sans MS" } }).Validate(),
        "Unsupported fonts must be rejected");

    // Every Theme Creator option, from every preset, produces a valid layout and stays in range.
    foreach (var preset in ThemeCatalog.BuiltIn)
    {
        foreach (var option in ThemeLayoutOptions.All)
        {
            var layout = preset.EffectiveLayout;
            for (var step = 0; step < 20; step++)
            {
                layout = option.Next(layout);
                layout.Validate();
                if (option.Previous is not null) option.Previous(layout).Validate();
                Check(!string.IsNullOrWhiteSpace(option.Describe(layout)), $"Option {option.Key} needs a label");
            }
        }
    }
    var sections = ThemeLayoutOptions.All.Single(option => option.Key == "sections");
    var cycled = ThemeLayout.Classic;
    for (var step = 0; step < 4; step++) cycled = sections.Next(cycled);
    Check(cycled.SectionMode == HomeSectionMode.Stacked, "Section modes must cycle back to the start");

    Console.WriteLine("Theme tests passed: machine default, shared machine themes, override fallback, private profile themes, import/export, built-in protection, console-style presets and layouts.");
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
