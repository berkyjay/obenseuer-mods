using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace StatusIconTiming;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static ConfigEntry<int> AppearAt;

    private void Awake()
    {
        Log = Logger;
        AppearAt = Config.Bind("Icons", "AppearAt", 66,
            new ConfigDescription(
                "How high a need has to get, out of 100, before its icon appears. The game's own value is 33. " +
                "Covers hunger, thirst, tiredness, toilet, depression and the alcohol, mushroom, smoking and gambling needs. " +
                "Hygiene counts down, so it appears at 100 minus this. Health, bleeding and the duck need keep the game's rules.",
                new AcceptableValueRange<int>(1, 99)));
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded, icons appear at {AppearAt.Value}");
    }

    // Read on every clock tick, so a changed value takes effect straight away.
    public static float NeedThreshold() => AppearAt.Value;

    public static float FallingThreshold() => 100 - AppearAt.Value;
}

// PlayerStatsUI.BarUpdate decides for each icon whether it's hidden, with the thresholds written
// into the code: "need < 33" for the nine needs (toilet checks bowel and bladder, so ten in all),
// "health > 66" and "hygiene > 50". The needs and hygiene are swapped for calls that read the config.
// Health keeps the game's 66, so damage is still flagged early. Its number is still
// counted, as a check that the method is the one this patch was written for.
[HarmonyPatch(typeof(PlayerStatsUI), nameof(PlayerStatsUI.BarUpdate))]
internal static class MoveThresholds
{
    private const int ExpectedNeeds = 10;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        List<CodeInstruction> needs = code.Where(i => IsFloat(i, 33f)).ToList();
        List<CodeInstruction> health = code.Where(i => IsFloat(i, 66f)).ToList();
        List<CodeInstruction> hygiene = code.Where(i => IsFloat(i, 50f)).ToList();

        // A game update that changes the method would make a blind swap hit the wrong numbers.
        // Leave the game's rules in place instead.
        if (needs.Count != ExpectedNeeds || health.Count != 1 || hygiene.Count != 1)
        {
            Plugin.Log.LogWarning(
                $"PlayerStatsUI.BarUpdate has changed (found {needs.Count}x 33, {health.Count}x 66, {hygiene.Count}x 50; " +
                $"expected {ExpectedNeeds}, 1, 1). Status icons keep the game's thresholds.");
            return code;
        }

        MethodInfo needThreshold = AccessTools.Method(typeof(Plugin), nameof(Plugin.NeedThreshold));
        MethodInfo fallingThreshold = AccessTools.Method(typeof(Plugin), nameof(Plugin.FallingThreshold));
        // Changing the instruction in place keeps any jump labels attached to it.
        foreach (CodeInstruction instruction in needs)
        {
            instruction.opcode = OpCodes.Call;
            instruction.operand = needThreshold;
        }
        foreach (CodeInstruction instruction in hygiene)
        {
            instruction.opcode = OpCodes.Call;
            instruction.operand = fallingThreshold;
        }
        Plugin.Log.LogInfo("Status icon thresholds patched");
        return code;
    }

    private static bool IsFloat(CodeInstruction instruction, float value) =>
        instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float f && f == value;
}
