using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrevHome.Storage;

namespace GrevHome.Input;

public enum ControllerShortcutAction
{
    ReturnHome,
    Overlay
}

public enum ControllerButton
{
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    Menu,
    View,
    LeftThumb,
    RightThumb,
    LeftShoulder,
    RightShoulder,
    A,
    B,
    X,
    Y,
    LeftTrigger,
    RightTrigger
}

public sealed record ControllerShortcutBinding(
    string Id,
    ControllerShortcutAction Action,
    IReadOnlyList<ControllerButton> Buttons,
    int HoldMilliseconds,
    bool Enabled = true,
    byte TriggerThreshold = 160);

public sealed record ControllerShortcutConfiguration(
    int Version,
    IReadOnlyList<ControllerShortcutBinding> Bindings);

public sealed record ControllerShortcutEventArgs(
    int ControllerIndex,
    ControllerShortcutAction Action,
    string BindingId);

public sealed class ControllerShortcutService
{
    private const int CurrentVersion = 1;
    private const int MaximumButtonsPerBinding = 8;
    private const int MaximumHoldMilliseconds = 5000;

    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public ControllerShortcutService(AppPaths paths)
    {
        _paths = paths;
    }

    public ControllerShortcutConfiguration LoadOrCreate()
    {
        _paths.EnsureMachineLayout();

        if (!File.Exists(_paths.ControllerShortcutsFile))
        {
            var defaults = CreateDefaults();
            try
            {
                Save(defaults);
            }
            catch (IOException)
            {
                // The runtime can still use safe in-memory defaults when the settings file cannot be written.
            }
            catch (UnauthorizedAccessException)
            {
                // The runtime can still use safe in-memory defaults when the settings file cannot be written.
            }

            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_paths.ControllerShortcutsFile);
            var loaded = JsonSerializer.Deserialize<ControllerShortcutConfiguration>(json, _jsonOptions);
            return NormalizeAndValidate(loaded);
        }
        catch (JsonException)
        {
            return CreateDefaults();
        }
        catch (InvalidOperationException)
        {
            return CreateDefaults();
        }
        catch (IOException)
        {
            return CreateDefaults();
        }
        catch (UnauthorizedAccessException)
        {
            return CreateDefaults();
        }
    }

    public void Save(ControllerShortcutConfiguration configuration)
    {
        var validated = NormalizeAndValidate(configuration);
        _paths.EnsureMachineLayout();

        var temporaryPath = _paths.ControllerShortcutsFile + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(validated, _jsonOptions));
            File.Move(temporaryPath, _paths.ControllerShortcutsFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static ControllerShortcutConfiguration CreateDefaults() =>
        new(
            CurrentVersion,
            new[]
            {
                new ControllerShortcutBinding(
                    "return-home-default",
                    ControllerShortcutAction.ReturnHome,
                    new[]
                    {
                        ControllerButton.LeftShoulder,
                        ControllerButton.RightShoulder,
                        ControllerButton.View
                    },
                    HoldMilliseconds: 700),
                new ControllerShortcutBinding(
                    "overlay-default",
                    ControllerShortcutAction.Overlay,
                    new[]
                    {
                        ControllerButton.LeftShoulder,
                        ControllerButton.RightShoulder,
                        ControllerButton.Menu
                    },
                    HoldMilliseconds: 450)
            });

    private static ControllerShortcutConfiguration NormalizeAndValidate(
        ControllerShortcutConfiguration? configuration)
    {
        if (configuration is null || configuration.Version != CurrentVersion)
        {
            throw new InvalidOperationException("Unsupported controller shortcut configuration.");
        }

        var sourceBindings = configuration.Bindings?.ToArray() ?? Array.Empty<ControllerShortcutBinding>();
        var normalizedBindings = new List<ControllerShortcutBinding>(sourceBindings.Length);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenCombinations = new Dictionary<string, ControllerShortcutAction>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in sourceBindings)
        {
            var id = binding.Id?.Trim() ?? string.Empty;
            if (id.Length == 0 || id.Length > 64 || !seenIds.Add(id))
            {
                throw new InvalidOperationException("Controller shortcut binding IDs must be unique and 1-64 characters long.");
            }

            if (!Enum.IsDefined(binding.Action))
            {
                throw new InvalidOperationException("Controller shortcut contains an unknown system action.");
            }

            var buttons = binding.Buttons?.Distinct().ToArray() ?? Array.Empty<ControllerButton>();
            if (buttons.Length == 0 || buttons.Length > MaximumButtonsPerBinding || buttons.Any(button => !Enum.IsDefined(button)))
            {
                throw new InvalidOperationException($"Controller shortcuts must contain between 1 and {MaximumButtonsPerBinding} known controller inputs.");
            }

            if (binding.HoldMilliseconds is < 0 or > MaximumHoldMilliseconds)
            {
                throw new InvalidOperationException($"Controller shortcut hold time must be between 0 and {MaximumHoldMilliseconds} ms.");
            }

            if (binding.TriggerThreshold == 0)
            {
                throw new InvalidOperationException("Trigger threshold must be greater than zero.");
            }

            var combinationKey = string.Join(
                "+",
                buttons.OrderBy(button => button.ToString(), StringComparer.Ordinal));

            if (seenCombinations.TryGetValue(combinationKey, out var existingAction))
            {
                var label = existingAction == binding.Action
                    ? "That controller combination is already configured for this action."
                    : "The same controller combination cannot trigger two different Grev system actions.";
                throw new InvalidOperationException(label);
            }

            seenCombinations[combinationKey] = binding.Action;
            normalizedBindings.Add(binding with { Id = id, Buttons = buttons });
        }

        if (!normalizedBindings.Any(binding => binding.Enabled && binding.Action == ControllerShortcutAction.ReturnHome))
        {
            throw new InvalidOperationException("At least one enabled Return Home shortcut is required.");
        }

        return new ControllerShortcutConfiguration(CurrentVersion, normalizedBindings);
    }
}
