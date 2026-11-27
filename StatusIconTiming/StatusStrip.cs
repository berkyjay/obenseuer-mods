using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace StatusIconTiming;

// With the HUD icons appearing later, the Character tab shows every status icon all the time, in the
// strip along its top. That strip ("Navigation") holds five blank, disabled buttons the game never
// uses; it sits above the skills area, not in it, so the skills don't move. The buttons are hidden
// and copies of the HUD's own icons go in their place, so the look and the hover tooltip
// (PlayerStatsToolTipInfo, which reads the live value) are the game's. Only the icons are copied, not
// the HUD's Stats object, which also holds the full-screen damage flash. This drives the copies itself
// instead of registering them with PlayerStats, so they never play damage or death effects.
internal sealed class StatusStrip : MonoBehaviour
{
    private const string StripPath = "Navigation";
    private const string ButtonsPath = "Navigation/Buttons";
    private const float IconSize = 40f;
    private const float Spacing = 24f;
    private const float RefreshSeconds = 0.5f;
    private const float PulseSeconds = 0.05f;

    private enum Kind { Health, Bleeding, Hunger, Thirst, Tiredness, Toilet, Mood, Hygiene, Alcohol, Mushroom, Smokes, Gambling, Duck }

    // PlayerStatsUI field holding each HUD icon, in the order shown here.
    private static readonly (Kind kind, string field)[] Order =
    {
        (Kind.Health, "HealthBar"), (Kind.Bleeding, "BleedingBar"), (Kind.Hunger, "HungerBar"), (Kind.Thirst, "ThirstBar"),
        (Kind.Tiredness, "TirednessBar"), (Kind.Toilet, "BowelBar"), (Kind.Mood, "DepressionBar"), (Kind.Hygiene, "HygieneBar"),
        (Kind.Alcohol, "AlcoholNeedBar"), (Kind.Mushroom, "MushroomNeedBar"), (Kind.Smokes, "SmokesNeedBar"),
        (Kind.Gambling, "GamblingBar"), (Kind.Duck, "DuckBar"),
    };

    private static readonly AccessTools.FieldRef<PlayerStatsUI, Color> HudRed = AccessTools.FieldRefAccess<PlayerStatsUI, Color>("RedColor");
    private static readonly AccessTools.FieldRef<PlayerStatsUI, Color> HudGreen = AccessTools.FieldRefAccess<PlayerStatsUI, Color>("GreenColor");

    private sealed class Icon
    {
        internal Kind Kind;
        internal PlayerStatUI Stat;
        internal bool Critical;
    }

    private readonly List<Icon> icons = new List<Icon>();
    private Color green;
    private Color red;
    private float nextRefresh;
    private float nextPulse;
    private bool pulsed;

    internal static void Attach(CharacterPanel panel)
    {
        if (panel.GetComponent<StatusStrip>() != null)
        {
            return;
        }
        PlayerStatsUI hud = PlayerStats.instance?.playerStatsUIs.FirstOrDefault(ui => ui != null);
        Transform strip = panel.transform.Find(StripPath);
        Transform buttons = panel.transform.Find(ButtonsPath);
        if (hud == null || strip == null || buttons == null)
        {
            Plugin.Log.LogWarning($"Character tab status icons not added (HUD {(hud != null ? "found" : "missing")}, strip {(strip != null ? "found" : "missing")}).");
            return;
        }
        panel.gameObject.AddComponent<StatusStrip>().Build(hud, strip, (RectTransform)buttons);
    }

    private void Build(PlayerStatsUI hud, Transform strip, RectTransform buttons)
    {
        green = HudGreen(hud);
        red = HudRed(hud);

        // A row the same size and place as the button row.
        var row = new GameObject("Status Icons", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(strip, false);
        row.anchorMin = buttons.anchorMin;
        row.anchorMax = buttons.anchorMax;
        row.pivot = buttons.pivot;
        row.anchoredPosition = buttons.anchoredPosition;
        row.sizeDelta = buttons.sizeDelta;
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = Spacing;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        buttons.gameObject.SetActive(false);

        var traverse = Traverse.Create(hud);
        foreach ((Kind kind, string field) in Order)
        {
            PlayerStatUI source = traverse.Field(field).GetValue<PlayerStatUI>();
            if (source == null)
            {
                continue;
            }
            PlayerStatUI copy = Instantiate(source, row, false);
            copy.name = "Status " + kind;
            var rect = (RectTransform)copy.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(IconSize, IconSize);
            rect.localScale = Vector3.one;
            Settle(copy);
            icons.Add(new Icon { Kind = kind, Stat = copy });
        }
        Refresh();
    }

    // On the HUD each icon slides in and out with its animator and shows "Hunger!" and trend arrows
    // beside it. Here it just sits in place, fully shown, with only the icon and its tooltip.
    private static void Settle(PlayerStatUI stat)
    {
        // A stopped animator leaves the copy as the scene saved it: slid off to the side ("Stat" sits
        // at -154) and faded out. Put the moving part back and make it opaque.
        foreach (Animator animator in stat.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled = false;
        }
        foreach (Transform child in stat.transform)
        {
            if (child is RectTransform moving)
            {
                moving.anchoredPosition = Vector2.zero;
                moving.localScale = Vector3.one;
            }
        }
        foreach (CanvasGroup group in stat.GetComponentsInChildren<CanvasGroup>(true))
        {
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }
        // The icon's own canvases may be set to draw in a fixed order for the HUD, which in the menu
        // could put them behind the page. Let them draw in the menu's order instead.
        foreach (Canvas canvas in stat.GetComponentsInChildren<Canvas>(true))
        {
            canvas.overrideSorting = false;
        }
        foreach (Component part in new Component[] { stat.arrowUp, stat.arrowDown, stat.text })
        {
            if (part != null)
            {
                part.gameObject.SetActive(false);
            }
        }
    }

    private void OnEnable() => nextRefresh = 0f;

    private void Update()
    {
        try
        {
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                Refresh();
            }
            if (Time.unscaledTime >= nextPulse)
            {
                nextPulse = Time.unscaledTime + PulseSeconds;
                pulsed = !pulsed;
                Vector3 scale = pulsed ? new Vector3(1.1f, 1.1f, 1.1f) : Vector3.one;
                foreach (Icon icon in icons)
                {
                    icon.Stat.image.rectTransform.localScale = icon.Critical ? scale : Vector3.one;
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
            enabled = false;
        }
    }

    private void Refresh()
    {
        PlayerStats stats = PlayerStats.instance;
        if (stats == null)
        {
            return;
        }
        foreach (Icon icon in icons)
        {
            bool shown = Applies(icon.Kind, stats);
            if (icon.Stat.gameObject.activeSelf != shown)
            {
                icon.Stat.gameObject.SetActive(shown);
            }
            if (!shown)
            {
                continue;
            }
            Color color = ColorOf(icon.Kind, stats, out icon.Critical);
            color.a = 1f;
            icon.Stat.image.color = color;
            // An open tooltip reads its value when it opens; this keeps it current while hovered.
            if (icon.Stat.playerStatsToolTipInfo != null)
            {
                icon.Stat.playerStatsToolTipInfo.UpdateCurrentToolTip();
            }
        }
    }

    // The addictions and ducks only show once you have that addiction.
    private static bool Applies(Kind kind, PlayerStats s) => kind switch
    {
        Kind.Alcohol => s.addictions.AlcoholAddiction > 0f,
        Kind.Mushroom => s.addictions.MushroomAddiction > 0f,
        Kind.Smokes => s.addictions.SmokingAddiction > 0f,
        Kind.Gambling => s.addictions.GamblingAddiction > 0f,
        Kind.Duck => s.duck.HasDuckAddiction(),
        _ => true,
    };

    // The HUD's colours (PlayerStatsUI.BarUpdate/UpdateColor) and pulse points (SetIconScales).
    // Bleeding is the exception: the HUD starts it half-way to red because it only shows while you
    // bleed, so here it's green when there's no bleeding.
    private Color ColorOf(Kind kind, PlayerStats s, out bool critical)
    {
        var h = s.hungerAndThirst;
        switch (kind)
        {
            case Kind.Health:
                critical = s.health.currentStatus < 10f;
                return Rising(s.health.currentStatus, 1);
            case Kind.Hygiene:
                critical = s.hygiene.Hygiene < 10f;
                return Rising(s.hygiene.Hygiene, 1);
            case Kind.Bleeding:
                critical = s.health.Bleeding > 50f;
                return s.health.Bleeding < 1f ? green : Falling(s.health.Bleeding, 50);
            default:
                float value = NeedValue(kind, s);
                critical = value > 90f;
                return Falling(value, 1);
        }
    }

    private static float NeedValue(Kind kind, PlayerStats s) => kind switch
    {
        Kind.Hunger => s.hungerAndThirst.Hunger,
        Kind.Thirst => s.hungerAndThirst.Thirst,
        Kind.Tiredness => s.tiredness.currentStatus,
        Kind.Toilet => Mathf.Max(s.hungerAndThirst.Bowel, s.hungerAndThirst.Bladder),
        Kind.Mood => s.mentalHealth.effectiveMentalHealth,
        Kind.Alcohol => s.addictions.AlcoholNeed,
        Kind.Mushroom => s.addictions.MushroomNeed,
        Kind.Smokes => s.addictions.SmokingNeed,
        Kind.Gambling => s.addictions.GamblingNeed,
        Kind.Duck => s.duck.currentStatus,
        _ => 0f,
    };

    // Needs go from green at 0 to red at 100; health and hygiene the other way.
    private Color Falling(float value, int offset) => Color.Lerp(green, red, (value + offset) * 0.01f);

    private Color Rising(float value, int offset) => Color.Lerp(red, green, (value + offset) * 0.01f);
}

[HarmonyPatch(typeof(CharacterPanel), "OnEnable")]
internal static class AddStatusStrip
{
    private static void Postfix(CharacterPanel __instance)
    {
        // The skills page still works without the icons; finishing OnEnable matters more.
        try
        {
            StatusStrip.Attach(__instance);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(exception);
        }
    }
}
