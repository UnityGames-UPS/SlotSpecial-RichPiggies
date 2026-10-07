using System.Collections.Generic;

/// <summary>
/// Rich Piggies symbol id map, transcribed from the backend's Config.json
/// (<c>Assets/Scripts/JSON/Config.json</c>, game id "SL-RP").
///
/// Ids fall into four bands and only the first two ever sit on a reel strip:
///
///   0-10   PAYING + WILD — the only symbols the reels actually draw during a spin.
///   11     MYSTERY       — the locker tile. It reveals one of 0-10 underneath, and may
///                          additionally stamp a coin (12-14) on top, both BEFORE pays
///                          are evaluated. Never pays as itself.
///   12-14  COINS         — Blue / Yellow / Red. reelsInstance is 0 on every reel, so
///                          they never land on their own: they only arrive as a Mystery
///                          overlay. They drive the three meters and the Free Spins
///                          trigger; WILD does NOT substitute for them.
///   15-20  JACKPOTS      — Mega / Grand / Major / Maxi / Minor / Mini. Also reelsInstance
///                          0 everywhere: revealed by Mystery during Yellow Free Spins to
///                          fill the six jackpot meters.
///
/// The server is authoritative. These constants exist so the client stops carrying magic
/// numbers; verify them against <c>GameConfig.symbols</c> at runtime rather than trusting
/// them blindly if the backend ever renumbers.
/// </summary>
public static class RichPiggiesSymbols
{
  // ── Paying symbols (0-9) ───────────────────────────────────────────────────
  public const int BusinessPig = 0;      // 3/4/5 → 20 / 125 / 200
  public const int LadyPig = 1;          // 3/4/5 → 15 /  60 / 125
  public const int BeachPig = 2;         // 3/4/5 → 15 /  40 / 100
  public const int DiamondPendant = 3;   // 3/4/5 → 10 /  25 /  60
  public const int Yacht = 4;            // 3/4/5 → 10 /  25 /  60
  public const int A = 5;                // 3/4/5 → 10 /  25 /  60
  public const int K = 6;                // 3/4/5 → 10 /  25 /  60
  public const int Q = 7;                // 3/4/5 →  5 /  20 /  60
  public const int J = 8;                // 3/4/5 →  5 /  20 /  60
  public const int Ten = 9;              // 3/4/5 →  5 /  20 /  60

  // ── Wild + Mystery ─────────────────────────────────────────────────────────
  public const int Wild = 10;
  public const int Mystery = 11;

  // ── Coins — Mystery overlay only, never on the strip ───────────────────────
  public const int BlueCoin = 12;
  public const int YellowCoin = 13;
  public const int RedCoin = 14;

  // ── Jackpot symbols — Mystery reveal during Yellow Free Spins ──────────────
  public const int Mega = 15;    // pays 5000 on a single symbol
  public const int Grand = 16;   // 1000
  public const int Major = 17;   //  200
  public const int Maxi = 18;    //   50
  public const int Minor = 19;   //   20
  public const int Mini = 20;    //   10

  public const int TotalSymbolCount = 21;

  /// <summary>Ids the reels may draw while spinning — everything else is reveal-only.</summary>
  public static readonly int[] ReelSymbolIds =
  {
    BusinessPig, LadyPig, BeachPig, DiamondPendant, Yacht,
    A, K, Q, J, Ten, Wild, Mystery
  };

  /// <summary>Ids the blur may show. Mystery is excluded so the locker only appears on a stop.</summary>
  public static readonly int[] SpinFillerSymbolIds =
  {
    BusinessPig, LadyPig, BeachPig, DiamondPendant, Yacht,
    A, K, Q, J, Ten, Wild
  };

  /// <summary>Blue / Yellow / Red, in meter order.</summary>
  public static readonly int[] CoinSymbolIds = { BlueCoin, YellowCoin, RedCoin };

  /// <summary>Mega, Grand, Major, Maxi, Minor, Mini — the order the config lists them in.</summary>
  public static readonly int[] JackpotSymbolIds = { Mega, Grand, Major, Maxi, Minor, Mini };

  /// <summary>Spaces each jackpot meter must fill before it awards (config: yellowPig.jackpotLevels).</summary>
  public static readonly Dictionary<int, int> JackpotMeterSpaces = new Dictionary<int, int>
  {
    { Mega, 6 }, { Grand, 5 }, { Maxi, 5 }, { Major, 4 }, { Minor, 2 }, { Mini, 2 }
  };

  public static bool IsCoin(int id) => id >= BlueCoin && id <= RedCoin;
  public static bool IsJackpot(int id) => id >= Mega && id <= Mini;
  public static bool IsPaying(int id) => id >= BusinessPig && id <= Ten;

  /// <summary>
  /// Server meter key for a jackpot symbol id — "Mega" ... "Mini", CASE-SENSITIVE, the same
  /// keys yellowFSCollections and meters.yellow use. Null for any non-jackpot id.
  /// </summary>
  public static string JackpotTierName(int id)
  {
    switch (id)
    {
      case Mega: return "Mega";
      case Grand: return "Grand";
      case Major: return "Major";
      case Maxi: return "Maxi";
      case Minor: return "Minor";
      case Mini: return "Mini";
      default: return null;
    }
  }

  /// <summary>WILD substitutes for everything except the three coins (and itself/Mystery).</summary>
  public static bool WildSubstitutesFor(int id) => IsPaying(id);
}
