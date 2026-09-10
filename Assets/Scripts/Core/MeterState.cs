using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Client-side mirror of the three persistent meters, plus the logic that works out which
/// revealed coin moved which meter.
///
/// The server never says "this coin fed the Grand meter" — it sends the coins
/// (payload.coinOverlays) and the meter values AFTER the spin (payload.meters), and the
/// client infers the rest by diffing against the values it had before. Everything here is
/// PRESENTATION ONLY: the server value is always the truth, and the last coin of a beat
/// always lands the display exactly on it (see the cumulative-target note below).
/// </summary>
internal class MeterSnapshot
{
  internal int blue;
  internal int red;
  internal Dictionary<string, double> yellow = new Dictionary<string, double>();

  internal static MeterSnapshot From(ServerMeters meters)
  {
    var snap = new MeterSnapshot();
    if (meters == null) return snap;

    snap.blue = meters.blue;
    snap.red = meters.red;

    if (meters.yellow != null)
      foreach (var kv in meters.yellow) snap.yellow[kv.Key] = kv.Value;

    return snap;
  }

  internal double Yellow(string tier)
  {
    if (tier == null || yellow == null) return 0;
    return yellow.TryGetValue(tier, out double v) ? v : 0;
  }
}

/// <summary>One jackpot tier ticking up as a single coin lands on it.</summary>
internal class JackpotAward
{
  /// <summary>Meter dictionary key — "Mega", "Grand", "Major", "Maxi", "Minor", "Mini".</summary>
  internal string tier;

  /// <summary>
  /// What the tier's text should read once this coin has landed. A CUMULATIVE target, not a
  /// delta — see <see cref="MeterDeltaAllocator"/>.
  /// </summary>
  internal double valueAfter;
}

/// <summary>
/// What one revealed coin does when it reaches its pig. Built in server order, one per
/// entry of payload.coinOverlays.
/// </summary>
internal class CoinPlan
{
  internal int row;
  internal int col;

  /// <summary>12 (Blue), 13 (Yellow) or 14 (Red).</summary>
  internal int coinSymbolId;

  /// <summary>
  /// False when the meter did not move for this coin — the server rolled no award. The coin
  /// still flies and the pig still jumps; nothing else happens.
  /// </summary>
  internal bool movesMeter;

  /// <summary>Blue/Red only: the value the meter text reads once this coin lands.</summary>
  internal int meterValueAfter;

  /// <summary>
  /// This coin fed the Free Spins trigger rather than its meter. It still flies and still
  /// jumps its pig; <see cref="movesMeter"/> is false, so no text ticks. Only ever set on the
  /// authoritative path — the meter diff cannot tell a triggering coin from a coin the server
  /// awarded nothing for.
  /// </summary>
  internal bool triggersFeature;

  /// <summary>
  /// Yellow only: the jackpot coins this one sends onward. Normally 0 or 1 entries; more
  /// only in the pathological case where more tiers moved than there were coins.
  /// </summary>
  internal List<JackpotAward> jackpotAwards = new List<JackpotAward>();
}

/// <summary>
/// Turns a spin's coinOverlays into the per-coin plans the flight beat runs.
///
/// The backend now states outright what each coin did — payload.coinOverlays carries
/// <c>action</c>, <c>meterValueAfter</c> for blue/red and
/// <c>addedToJackpot</c>/<c>jackpotMultiplierAdded</c> for yellow — so the client no longer
/// has to infer it. That matters beyond tidiness: a coin with <c>action: "triggered"</c> fed
/// the Free Spins trigger and moves NO meter, which is indistinguishable by diffing from a
/// coin the server simply awarded nothing for.
///
/// <see cref="MeterDeltaAllocator"/> is kept as a fallback for a payload that arrives without
/// those fields, and taking it is reported loudly — the shape is still in flux, and a silent
/// downgrade to guessing is exactly the failure that would go unnoticed.
/// </summary>
internal static class CoinPlanBuilder
{
  internal static List<CoinPlan> Build(MeterSnapshot previous, ServerMeters next,
                                       List<ServerCoinOverlay> coinOverlays)
  {
    if (coinOverlays == null || coinOverlays.Count == 0) return new List<CoinPlan>();

    if (!HasAuthoritativeFields(coinOverlays))
      return MeterDeltaAllocator.Build(previous, next, coinOverlays);

    return BuildFromServer(previous, coinOverlays);
  }

  /// <summary>
  /// Whether every overlay carries what the authoritative path needs. All or nothing: a
  /// mixed payload means the shape changed under us, and half-guessing would produce a
  /// worse result than guessing consistently.
  /// </summary>
  private static bool HasAuthoritativeFields(List<ServerCoinOverlay> coinOverlays)
  {
    for (int i = 0; i < coinOverlays.Count; i++)
    {
      var overlay = coinOverlays[i];
      if (overlay == null) continue;

      if (string.IsNullOrEmpty(overlay.action))
      {
        Debug.LogWarning($"[Meters] coinOverlays[{i}] has no \"action\" field, so the client " +
                         "cannot tell which coin fed which meter — falling back to diffing " +
                         "payload.meters. A coin that TRIGGERED free spins will be " +
                         "indistinguishable from one that won nothing. Check whether the " +
                         "backend payload shape changed again.");
        return false;
      }
    }

    return true;
  }

  private static List<CoinPlan> BuildFromServer(MeterSnapshot previous,
                                                List<ServerCoinOverlay> coinOverlays)
  {
    previous = previous ?? new MeterSnapshot();

    var plans = new List<CoinPlan>();

    // Jackpot multipliers arrive as DELTAS while the meter text wants a cumulative value, so
    // each tier is walked forward from what is already on screen. Several yellow coins in one
    // spin may feed the same tier, and each must land the text on its own running total.
    var runningJackpot = new Dictionary<string, double>();

    for (int i = 0; i < coinOverlays.Count; i++)
    {
      var overlay = coinOverlays[i];
      var plan = MeterDeltaAllocator.BuildPlan(overlay);
      if (plan == null) continue;

      plans.Add(plan);

      string action = overlay.action;

      if (string.Equals(action, "triggered", System.StringComparison.OrdinalIgnoreCase))
      {
        // Fed the Free Spins trigger, not a meter. It still flies and still jumps its pig.
        plan.triggersFeature = true;
        plan.movesMeter = false;
        continue;
      }

      if (!string.Equals(action, "added_to_meter", System.StringComparison.OrdinalIgnoreCase))
      {
        Debug.LogWarning($"[Meters] coinOverlays[{i}] has an unrecognised action " +
                         $"\"{action}\". The coin will fly and jump its pig, but no meter " +
                         "will move. Add the new action to CoinPlanBuilder.");
        plan.movesMeter = false;
        continue;
      }

      if (plan.coinSymbolId == RichPiggiesSymbols.YellowCoin)
        ApplyJackpotAward(plan, overlay, previous, runningJackpot, i);
      else
        ApplyIntegerMeter(plan, overlay, i);
    }

    return plans;
  }

  /// <summary>Blue / Red: the server states the meter's absolute value after this coin.</summary>
  private static void ApplyIntegerMeter(CoinPlan plan, ServerCoinOverlay overlay, int index)
  {
    if (!overlay.meterValueAfter.HasValue)
    {
      Debug.LogWarning($"[Meters] coinOverlays[{index}] says \"added_to_meter\" but carries no " +
                       "meterValueAfter, so its meter text cannot be ticked as it lands. The " +
                       "end-of-beat resync will still land the meter on the server's value.");
      plan.movesMeter = false;
      return;
    }

    plan.movesMeter = true;
    plan.meterValueAfter = overlay.meterValueAfter.Value;
  }

  /// <summary>
  /// Yellow: the server names the tier and how much it gained. Accumulated onto the tier's
  /// current multiplier, because the meter text shows a running total rather than a delta.
  /// </summary>
  private static void ApplyJackpotAward(CoinPlan plan, ServerCoinOverlay overlay,
                                        MeterSnapshot previous,
                                        Dictionary<string, double> runningJackpot, int index)
  {
    string tier = overlay.addedToJackpot;

    if (string.IsNullOrEmpty(tier))
    {
      Debug.LogWarning($"[Meters] coinOverlays[{index}] is a yellow coin marked " +
                       "\"added_to_meter\" but names no addedToJackpot tier, so no jackpot " +
                       "coin can be sent on. The coin still flies to the Yellow Pig.");
      plan.movesMeter = false;
      return;
    }

    if (!runningJackpot.TryGetValue(tier, out double current))
      current = previous.Yellow(tier);

    double valueAfter = current + (overlay.jackpotMultiplierAdded ?? 0);
    runningJackpot[tier] = valueAfter;

    plan.movesMeter = true;
    plan.jackpotAwards.Add(new JackpotAward { tier = tier, valueAfter = valueAfter });
  }
}

/// <summary>
/// LEGACY fallback: attributes a spin's meter movement to its coins by DIFFING the previous
/// snapshot against payload.meters, for a payload that predates the per-coin fields
/// <see cref="CoinPlanBuilder"/> prefers. Reaching this is reported as a warning.
///
/// Every value produced is a CUMULATIVE TARGET ("after this coin, the text reads X"), never
/// a per-coin delta. That is what keeps the display honest: however the split is guessed,
/// the last coin of each meter is pinned to the server's own value, so a mis-attribution
/// costs an odd-looking intermediate number and nothing more.
/// </summary>
internal static class MeterDeltaAllocator
{
  /// <summary>Canonical tier order — the order the backend config lists them in.</summary>
  internal static readonly string[] TierNames = { "Mega", "Grand", "Major", "Maxi", "Minor", "Mini" };

  internal static List<CoinPlan> Build(MeterSnapshot previous, ServerMeters next,
                                       List<ServerCoinOverlay> coinOverlays)
  {
    var plans = new List<CoinPlan>();
    if (coinOverlays == null || coinOverlays.Count == 0) return plans;

    previous = previous ?? new MeterSnapshot();

    // Index the coins by type, keeping server order within each type.
    var blueCoins = new List<CoinPlan>();
    var redCoins = new List<CoinPlan>();
    var yellowCoins = new List<CoinPlan>();

    foreach (var overlay in coinOverlays)
    {
      var plan = BuildPlan(overlay);
      if (plan == null) continue;

      plans.Add(plan);

      switch (plan.coinSymbolId)
      {
        case RichPiggiesSymbols.BlueCoin: blueCoins.Add(plan); break;
        case RichPiggiesSymbols.RedCoin: redCoins.Add(plan); break;
        case RichPiggiesSymbols.YellowCoin: yellowCoins.Add(plan); break;
      }
    }

    AllocateIntegerMeter(blueCoins, previous.blue, next != null ? next.blue : previous.blue);
    AllocateIntegerMeter(redCoins, previous.red, next != null ? next.red : previous.red);
    AllocateJackpotMeters(yellowCoins, previous, next);

    return plans;
  }

  /// <summary>
  /// Position and coin colour, the part of a plan both paths share. Internal because
  /// <see cref="CoinPlanBuilder"/> needs the same coinId-then-name resolution and duplicating
  /// it would let the two paths disagree about what a malformed overlay means.
  /// </summary>
  internal static CoinPlan BuildPlan(ServerCoinOverlay overlay)
  {
    if (overlay == null) return null;

    if (overlay.position == null || overlay.position.Count < 2)
    {
      Debug.LogError("[Meters] coinOverlays entry has no usable [row, col] position.");
      return null;
    }

    int id = overlay.coinId;

    // coinId is authoritative, but fall back to the readable name if the server ever omits
    // it — a coin at 0 would silently be read as BusinessPig.
    if (!RichPiggiesSymbols.IsCoin(id))
    {
      id = CoinIdFromName(overlay.coin);
      if (!RichPiggiesSymbols.IsCoin(id))
      {
        Debug.LogError($"[Meters] coinOverlays entry has neither a coin id ({overlay.coinId}) " +
                       $"nor a recognised name (\"{overlay.coin}\"). Skipping it.");
        return null;
      }
    }

    return new CoinPlan
    {
      row = overlay.position[0],
      col = overlay.position[1],
      coinSymbolId = id
    };
  }

  private static int CoinIdFromName(string name)
  {
    if (string.IsNullOrEmpty(name)) return -1;
    if (name.Equals("BlueCoin", System.StringComparison.OrdinalIgnoreCase)) return RichPiggiesSymbols.BlueCoin;
    if (name.Equals("YellowCoin", System.StringComparison.OrdinalIgnoreCase)) return RichPiggiesSymbols.YellowCoin;
    if (name.Equals("RedCoin", System.StringComparison.OrdinalIgnoreCase)) return RichPiggiesSymbols.RedCoin;
    return -1;
  }

  /// <summary>
  /// Spread a whole-number meter move (blue free spins, red wilds) across its coins.
  ///
  /// The integer form <c>from + delta * i / n</c> is monotone and lands exactly on
  /// <paramref name="to"/> at i == n, so two blue coins on a +2 move read 10 then 11, and a
  /// red +30 across two coins reads +15 then +15 — without the split ever drifting off the
  /// server's total.
  /// </summary>
  private static void AllocateIntegerMeter(List<CoinPlan> coins, int from, int to)
  {
    int n = coins.Count;
    if (n == 0) return;

    int delta = to - from;
    if (delta == 0)
    {
      // Coins landed but the server awarded nothing. They still fly; the text holds.
      foreach (var plan in coins) plan.meterValueAfter = to;
      return;
    }

    for (int i = 0; i < n; i++)
    {
      int valueAfter = from + (delta * (i + 1)) / n;

      // A coin whose share rounded to nothing gets no text change of its own — the next
      // coin carries it. Only meaningful when there are more coins than units of delta.
      coins[i].movesMeter = i == 0 ? valueAfter != from : valueAfter != coins[i - 1].meterValueAfter;
      coins[i].meterValueAfter = valueAfter;
    }
  }

  /// <summary>
  /// Attribute the six jackpot tiers to the yellow coins.
  ///
  /// Two shapes, because the server tells us only the totals:
  ///   coins >= tiers — each tier is SHARED by the coins mapped to it and its delta is split
  ///                    cumulatively, so two coins into Grand read half then the full total.
  ///   tiers  > coins — a coin carries more than one tier and sends more than one jackpot
  ///                    coin onward. Shouldn't happen; warned about.
  /// A yellow coin that ends up with no tier still flies and still jumps the pig.
  /// </summary>
  private static void AllocateJackpotMeters(List<CoinPlan> coins, MeterSnapshot previous, ServerMeters next)
  {
    int n = coins.Count;
    if (n == 0) return;
    if (next == null || next.yellow == null) return;

    // Tiers that actually moved, in canonical order.
    var movedTiers = new List<string>();
    foreach (string tier in TierNames)
    {
      double before = previous.Yellow(tier);
      double after = next.yellow.TryGetValue(tier, out double v) ? v : before;
      if (after > before) movedTiers.Add(tier);
    }

    int m = movedTiers.Count;
    if (m == 0) return;

    if (m > n)
    {
      Debug.LogWarning($"[Meters] {m} jackpot tiers moved but only {n} yellow coin(s) landed. " +
                       "Some coins will send more than one jackpot coin onward.");
    }

    // Map tier -> the coins that feed it, preserving coin order.
    var coinsPerTier = new Dictionary<string, List<CoinPlan>>();
    foreach (string tier in movedTiers) coinsPerTier[tier] = new List<CoinPlan>();

    if (n <= m)
    {
      // Every tier gets exactly one coin; a coin may carry several tiers.
      for (int j = 0; j < m; j++) coinsPerTier[movedTiers[j]].Add(coins[j % n]);
    }
    else
    {
      // Every coin gets a tier; a tier may be fed by several coins.
      for (int i = 0; i < n; i++) coinsPerTier[movedTiers[i % m]].Add(coins[i]);
    }

    foreach (string tier in movedTiers)
    {
      var feeders = coinsPerTier[tier];
      if (feeders.Count == 0) continue;

      double from = previous.Yellow(tier);
      double to = next.yellow[tier];
      double delta = to - from;

      for (int k = 0; k < feeders.Count; k++)
      {
        // Last feeder is pinned to the server value rather than accumulated, so a split
        // across an odd delta can never leave the text a fraction off.
        double valueAfter = (k == feeders.Count - 1) ? to : from + delta * (k + 1) / feeders.Count;

        feeders[k].movesMeter = true;
        feeders[k].jackpotAwards.Add(new JackpotAward { tier = tier, valueAfter = valueAfter });
      }
    }
  }
}
