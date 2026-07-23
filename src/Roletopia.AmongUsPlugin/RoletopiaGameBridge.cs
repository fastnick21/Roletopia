using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using Roletopia.Runtime;

namespace Roletopia.AmongUsPlugin;

internal static class RoletopiaGameBridge
{
    private static AmongUsLifecycleController? _lifecycle;
    private static RuntimeCoordinator? _coordinator;
    private static ReflectionAmongUsAdapter? _adapter;
    private static ManualLogSource? _log;
    private static int _mainMenuLateUpdateFrames;
    private static bool _lateMenuMarkerApplied;

    internal static RuntimeCoordinator? Coordinator => _coordinator;

    public static void Initialize(
        AmongUsLifecycleController lifecycle,
        RuntimeCoordinator coordinator,
        ReflectionAmongUsAdapter adapter,
        ManualLogSource log)
    {
        _lifecycle = lifecycle;
        _coordinator = coordinator;
        _adapter = adapter;
        _log = log;
    }

    public static void Reset()
    {
        _lifecycle = null;
        _coordinator = null;
        _adapter = null;
        _log = null;
        _mainMenuLateUpdateFrames = 0;
        _lateMenuMarkerApplied = false;
    }

    public static void LobbyCreated()
    {
        SafeInvoke("lobby creation", () => _lifecycle?.OnLobbyCreated());
    }

    public static void GameStarting()
    {
        SafeInvoke("game start", () => _lifecycle?.OnGameStarting(0));
    }

    public static void MeetingStarted()
    {
        SafeInvoke("meeting start", () => _lifecycle?.OnMeetingStarted());
    }

    public static void MeetingEnded()
    {
        SafeInvoke("meeting end", () => _lifecycle?.OnMeetingEnded());
    }

    public static void TaskCompleted()
    {
        SafeInvoke("task completion", () => _lifecycle?.OnTaskCompleted());
    }

    public static void GameEnded()
    {
        SafeInvoke("game end", () =>
        {
            _lifecycle?.OnGameEnded();
            return true;
        });
    }

    public static void MainMenuStarted(object __instance)
    {
        SafeInvoke("main-menu start", () =>
        {
            _lifecycle?.OnReturnedToMainMenu();
            _mainMenuLateUpdateFrames = 0;
            _lateMenuMarkerApplied = false;
            _log?.LogInfo("Main menu started; delaying visible marker until startup/localization has finished.");
            return true;
        });
    }

    public static void MainMenuLateUpdate(object __instance)
    {
        if (__instance == null || _lateMenuMarkerApplied) return;

        _mainMenuLateUpdateFrames++;
        if (_mainMenuLateUpdateFrames < 120) return;
        if ((_mainMenuLateUpdateFrames - 120) % 120 != 0) return;

        SafeInvoke("late main-menu marker", () =>
        {
            var applied = TryAddMainMenuMarker(__instance);
            if (applied)
            {
                _lateMenuMarkerApplied = true;
                _log?.LogInfo($"Late main-menu marker applied after {_mainMenuLateUpdateFrames} LateUpdate frames.");
            }
            else
            {
                _log?.LogWarning($"Late main-menu marker attempt {_mainMenuLateUpdateFrames} did not find a rendered label; will retry.");
            }
            return applied;
        });
    }

    private static bool TryAddMainMenuMarker(object instance)
    {
        if (instance == null) return false;

        const string marker = "Roletopia 0.1.0-alpha loaded";
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();

        foreach (var memberName in new[] { "newsButton", "settingsButton", "howToPlayButton", "playButton", "creditsButton" })
        {
            var member = type.GetField(memberName, flags) as MemberInfo ?? type.GetProperty(memberName, flags);
            if (member == null) continue;

            var button = ReadMember(instance, member);
            if (button == null) continue;

            if (TryMarkLiveTextComponents(button, marker))
            {
                _log?.LogInfo($"Added LIVE visible Roletopia marker through {type.Name}.{memberName}.");
                return true;
            }
        }

        var preferredNames = new[]
        {
            "version", "build", "creditsButton", "newsButton", "settingsButton",
            "howToPlayButton", "playButton"
        };

        var members = type.GetFields(flags).Cast<MemberInfo>()
            .Concat(type.GetProperties(flags))
            .Where(member => preferredNames.Any(name =>
                member.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(member => Array.FindIndex(preferredNames, name =>
                member.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var member in members)
        {
            var value = ReadMember(instance, member);
            if (value == null) continue;

            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!TrySetTextRecursive(value, marker, flags, visited, 0)) continue;

            _log?.LogInfo($"Added LATE fallback Roletopia marker through {type.Name}.{member.Name}.");
            return true;
        }

        _log?.LogWarning("Could not find a live writable main-menu text component. Roletopia remains loaded; only the visual marker was skipped.");
        return false;
    }

    private static bool TryMarkLiveTextComponents(object button, string marker)
    {
        try
        {
            var tmpType = ResolveType("TMPro.TextMeshPro", "Unity.TextMeshPro");
            var tmpUiType = ResolveType("TMPro.TextMeshProUGUI", "Unity.TextMeshPro");

            return (tmpType != null && TryMarkComponentsOfType(button, tmpType, marker)) ||
                   (tmpUiType != null && TryMarkComponentsOfType(button, tmpUiType, marker));
        }
        catch (Exception exception)
        {
            _log?.LogWarning($"Live menu text lookup failed: {exception.Message}");
            return false;
        }
    }

    private static Type? ResolveType(string fullName, string assemblyName)
    {
        var type = Type.GetType($"{fullName}, {assemblyName}", throwOnError: false);
        if (type != null) return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (type != null) return type;
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool TryMarkComponentsOfType(object button, Type componentType, string marker)
    {
        object? owner = button;
        var gameObjectProperty = button.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (gameObjectProperty?.CanRead == true)
        {
            try { owner = gameObjectProperty.GetValue(button) ?? button; }
            catch { owner = button; }
        }

        var method = owner.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (candidate.Name != "GetComponentsInChildren" || candidate.IsGenericMethod) return false;
                var parameters = candidate.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(Type) &&
                       parameters[1].ParameterType == typeof(bool);
            });

        if (method == null) return false;
        if (method.Invoke(owner, new object[] { componentType, true }) is not IEnumerable components) return false;

        foreach (var component in components)
        {
            if (component == null) continue;
            var textProperty = component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? component.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (textProperty?.CanRead != true || textProperty.CanWrite != true || textProperty.PropertyType != typeof(string)) continue;

            string current;
            try { current = textProperty.GetValue(component) as string ?? string.Empty; }
            catch { continue; }

            if (current.Contains("Roletopia", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.IsNullOrWhiteSpace(current)) continue;

            try
            {
                textProperty.SetValue(component, current + "\n<size=55%>" + marker + "</size>");
                _log?.LogInfo($"Live TMP marker changed '{current}' on {component.GetType().FullName}.");
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static object? ReadMember(object owner, MemberInfo member)
    {
        try
        {
            return member switch
            {
                FieldInfo field => field.GetValue(owner),
                PropertyInfo property when property.GetIndexParameters().Length == 0 && property.CanRead => property.GetValue(owner),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySetTextRecursive(
        object value,
        string marker,
        BindingFlags flags,
        HashSet<object> visited,
        int depth)
    {
        if (depth > 3 || value is string || !visited.Add(value)) return false;

        var valueType = value.GetType();
        var textProperty = valueType.GetProperty("text", flags) ?? valueType.GetProperty("Text", flags);
        if (textProperty?.CanRead == true && textProperty.CanWrite && textProperty.PropertyType == typeof(string))
        {
            try
            {
                var current = textProperty.GetValue(value) as string ?? string.Empty;
                if (!current.Contains("Roletopia", StringComparison.OrdinalIgnoreCase))
                {
                    var replacement = string.IsNullOrWhiteSpace(current)
                        ? marker
                        : current + "\n<size=55%>" + marker + "</size>";
                    textProperty.SetValue(value, replacement);
                }
                return true;
            }
            catch
            {
            }
        }

        var childMembers = valueType.GetFields(flags).Cast<MemberInfo>()
            .Concat(valueType.GetProperties(flags))
            .Where(member =>
                member.Name.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("label", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("title", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("button", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("graphic", StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var childMember in childMembers)
        {
            var child = ReadMember(value, childMember);
            if (child != null && TrySetTextRecursive(child, marker, flags, visited, depth + 1))
                return true;
        }

        return false;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static void SafeInvoke(string eventName, Func<bool?> callback)
    {
        try
        {
            var result = callback();
            _log?.LogDebug($"Handled {eventName}; accepted={result?.ToString() ?? "n/a"}.");
        }
        catch (Exception exception)
        {
            _log?.LogError($"Roletopia failed while handling {eventName}: {exception}");
        }
    }
}
