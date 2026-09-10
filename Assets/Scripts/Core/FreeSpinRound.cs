using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which pigs are involved in a free-spin round. A round may be triggered by any combination
/// of the three, and every triggering pig's effect runs for the whole round — so this is a
/// flags set, not a single value.
/// </summary>
[Flags]
internal enum PigFeature
{
  None = 0,
  Blue = 1,
  Yellow = 2,
  Red = 4
}

/// <summary>
/// Parses the backend's feature names into <see cref="PigFeature"/>.
///
/// The server spells the same feature two ways depending on the field: payload.triggeredFeatures
/// uses "bluePig", payload.activeFeature uses "blue". Both are accepted here so callers never
/// have to care which field a list came from.
/// </summary>
internal static class PigFeatures
{
  // So a rename on the backend is reported once rather than on every spin of a round.
  private static readonly HashSet<string> warnedNames = new HashSet<string>();

  internal static PigFeature Parse(List<string> names)
  {
    var features = PigFeature.None;
    if (names == null) return features;

    foreach (string name in names)
    {
      if (string.IsNullOrEmpty(name)) continue;

      // Jackpot awards ride in triggeredFeatures too ("jackpot_Grand"). They are a Yellow
      // in-round event, not a trigger, and must not be mistaken for one.
      if (name.StartsWith("jackpot", StringComparison.OrdinalIgnoreCase)) continue;

      if (name.StartsWith("blue", StringComparison.OrdinalIgnoreCase)) features |= PigFeature.Blue;
      else if (name.StartsWith("yellow", StringComparison.OrdinalIgnoreCase)) features |= PigFeature.Yellow;
      else if (name.StartsWith("red", StringComparison.OrdinalIgnoreCase)) features |= PigFeature.Red;
      else if (warnedNames.Add(name))
      {
        Debug.LogWarning($"[FreeSpins] Unrecognised feature name \"{name}\" from the server. " +
                         "It will be ignored — if the backend renamed a pig feature, update " +
                         "PigFeatures.Parse.");
      }
    }

    return features;
  }

  internal static bool Has(PigFeature set, PigFeature one) => (set & one) != 0;

  internal static int Count(PigFeature set)
  {
    int count = 0;
    if (Has(set, PigFeature.Blue)) count++;
    if (Has(set, PigFeature.Yellow)) count++;
    if (Has(set, PigFeature.Red)) count++;
    return count;
  }

  /// <summary>The coin symbol id (12/13/14) a feature's pig owns, for PigMeterController lookups.</summary>
  internal static int CoinSymbolId(PigFeature one)
  {
    if (one == PigFeature.Blue) return RichPiggiesSymbols.BlueCoin;
    if (one == PigFeature.Yellow) return RichPiggiesSymbols.YellowCoin;
    return RichPiggiesSymbols.RedCoin;
  }

  /// <summary>All three, in meter order, so callers can iterate without repeating the list.</summary>
  internal static readonly PigFeature[] All = { PigFeature.Blue, PigFeature.Yellow, PigFeature.Red };
}

/// <summary>
/// One free-spin round's bookkeeping — the part the server does NOT send.
///
/// Rich Piggies reports only payload.freeSpinsRemaining. There is no round total, no
/// spins-used and no round-win total in the payload (the converter leaves serverTotalSpins
/// and serverSpinsUsed at 0, and serverTotalRoundWin at the current spin's own win), so the
/// client latches the total from the trigger spin and counts the rest itself.
///
/// Everything here is DISPLAY bookkeeping. The server still decides when the round ends —
/// freeSpinsRemaining hitting zero — and the balance is always its own.
/// </summary>
internal class FreeSpinRound
{
  /// <summary>Every pig that contributed to the trigger. Drives the ribbons and the intro popups.</summary>
  internal PigFeature contributors;

  /// <summary>Latched from the trigger spin's freeSpinsRemaining. The FreeSpinDisplay's "of N".</summary>
  internal int totalSpins;

  /// <summary>Derived each spin as totalSpins - freeSpinsRemaining.</summary>
  internal int spinsUsed;

  /// <summary>
  /// The round's running total, for the bottom bar and the congratulations panel.
  ///
  /// SEEDED with the base spin that triggered the round, not with zero. That spin's line wins
  /// and the 1x total stake the trigger itself awards are part of what the player takes away
  /// from the feature, so the win field carries straight on from the number already on screen
  /// rather than blanking and starting again.
  /// </summary>
  internal double accumulatedWin;

  /// <summary>
  /// Free spins the Blue meter held at the moment of the trigger — what the blue intro popup
  /// shows. Read from the TRIGGER spin's meters, because the spec resets the Blue meter only
  /// at the end of the round.
  /// </summary>
  internal int blueAward;

  /// <summary>Wilds the Red meter held at the trigger. Shown by the red (wild) intro popup.</summary>
  internal int redWildCount;

  /// <summary>
  /// Build a round from the spin that triggered it.
  ///
  /// <paramref name="meters"/> must be the TRIGGER spin's meters. Blue and Red are read here
  /// and not again — once the round ends the server resets them (to 9 and 15), and a popup
  /// reading them late would show the reset value instead of the award.
  ///
  /// <paramref name="triggerWin"/> seeds <see cref="accumulatedWin"/>, so the round total
  /// includes the spin that started it.
  /// </summary>
  internal static FreeSpinRound FromTrigger(PigFeature contributors, int freeSpinsRemaining,
                                            ServerMeters meters, double triggerWin)
  {
    var round = new FreeSpinRound
    {
      contributors = contributors,
      totalSpins = Mathf.Max(0, freeSpinsRemaining),
      accumulatedWin = triggerWin,
      blueAward = meters != null ? meters.blue : 0,
      redWildCount = meters != null ? meters.red : 0
    };

    if (round.totalSpins <= 0)
    {
      Debug.LogError("[FreeSpins] A trigger arrived with freeSpinsRemaining " +
                     $"{freeSpinsRemaining} — the round would end before its first spin. " +
                     "The client cannot invent a spin count; check the backend payload.");
    }

    // A meter still reading its configured default on the spin that triggered that colour
    // means the server reset it in the same response, so the popup below would announce the
    // default instead of the award the player actually earned.
    if (PigFeatures.Has(contributors, PigFeature.Blue) && round.blueAward == BlueDefaultMeter)
    {
      Debug.LogWarning($"[FreeSpins] Blue triggered but its meter reads the default " +
                       $"{BlueDefaultMeter} on the trigger spin. The blue popup will show " +
                       "that value. If the backend now resets the meter on the trigger " +
                       "response rather than at the end of the round, the award has to come " +
                       "from a new field instead.");
    }

    if (PigFeatures.Has(contributors, PigFeature.Red) && round.redWildCount == RedDefaultWilds)
    {
      Debug.LogWarning($"[FreeSpins] Red triggered but its meter reads the default " +
                       $"{RedDefaultWilds} on the trigger spin — see the Blue note above.");
    }

    return round;
  }

  /// <summary>Config defaults (features.bluePig.defaultMeter / redPig.defaultWilds).</summary>
  private const int BlueDefaultMeter = 9;
  private const int RedDefaultWilds = 15;

  /// <summary>
  /// Fold one completed free spin in. <paramref name="freeSpinsRemaining"/> is the server's
  /// own count, so the display can never drift from it.
  /// </summary>
  internal void RecordSpin(double winAmount, int freeSpinsRemaining)
  {
    accumulatedWin += winAmount;
    spinsUsed = Mathf.Clamp(totalSpins - freeSpinsRemaining, 0, totalSpins);
  }
}
