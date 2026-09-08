using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

#region Server Communication Models

[Serializable]
public class InitData
{
  public string id = "initData";
  public ServerGameData gameData;
  public ServerFeatures features;
  public ServerUIData uiData;
  public ServerPlayer player;
  public JackpotData jackpotData;
}

[Serializable]
public class JackpotData
{
  public JackpotValues values;//jackpotfeature

}

[Serializable]
public class JackpotValues
{
  public string miniJackpot;
  public string minorJackpot;
  public string majorJackpot;
  public string grandJackpot;
}

[Serializable]
public class JackpotSyncData
{
  public string gameId;
  public JackpotValues values;
}

[Serializable]
public class ServerGameData
{
  public List<List<int>> lines;
  public List<double> bets;

  /// <summary>
  /// Lines per spin — total stake is bet x this. Rich Piggies init does NOT send it, so the
  /// default stands and matches the 25 fixed paylines.
  /// </summary>
  public double creditDivisor = 25;

  public int totalLines;
}

/// <summary>
/// Rich Piggies init "features" block. Note this is a mix of static feature configuration
/// (the pig blocks) and live per-player state (activeFeature / freeSpinsRemaining / meters).
/// </summary>
[Serializable]
public class ServerFeatures
{
  public MysteryRevealFeature mysteryReveal;
  public BluePigFeature bluePig;
  public YellowPigFeature yellowPig;
  public RedPigFeature redPig;

  /// <summary>Minimum symbols on adjacent reels from the leftmost reel for a line to pay.</summary>
  public int minMatchCount;

  // Live state — carried so nothing is lost; nothing reads these yet.

  /// <summary>
  /// Features currently running for this player. Init sends an array (empty when idle);
  /// the spin payload sends a single string, so the converter accepts both.
  /// </summary>
  [JsonConverter(typeof(StringOrArrayConverter))]
  public List<string> activeFeature;

  public int freeSpinsRemaining;
  public ServerMeters meters;
}

/// <summary>
/// Reads a JSON field that may be a string, an array of strings, or null into a
/// <see cref="List{String}"/>. The server has shipped <c>activeFeature</c> as all three.
/// </summary>
public class StringOrArrayConverter : JsonConverter
{
  public override bool CanConvert(Type objectType) => objectType == typeof(List<string>);

  public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
                                  JsonSerializer serializer)
  {
    switch (reader.TokenType)
    {
      case JsonToken.Null:
        return null;
      case JsonToken.StartArray:
        return serializer.Deserialize<List<string>>(reader);
      default:
        var single = reader.Value?.ToString();
        return string.IsNullOrEmpty(single) ? new List<string>() : new List<string> { single };
    }
  }

  public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
  {
    serializer.Serialize(writer, value);
  }
}

[Serializable]
public class MysteryRevealFeature
{
  public bool enabled;
}

[Serializable]
public class BluePigFeature
{
  public int defaultMeter;
  public int maxMeter;
}

[Serializable]
public class YellowPigFeature
{
  public int freeSpinsCount;
  /// <summary>Spaces each jackpot meter must fill before it awards, keyed by tier name.</summary>
  public Dictionary<string, int> jackpotLevels;
  public Dictionary<string, double> defaultJackpotMultipliers;
}

[Serializable]
public class RedPigFeature
{
  public int defaultWilds;
  public int maxMeter;
  public int freeSpinsCount;
  public List<int> wildCountBuckets;
}

/// <summary>The three persistent meters. Blue = free spins, red = wilds, yellow = jackpots.</summary>
[Serializable]
public class ServerMeters
{
  public int blue;
  public int red;
  public Dictionary<string, double> yellow;
}

[Serializable]
public class ExtraSpinsData
{
  [JsonProperty("2")] public int _2; // Keep for safety/compatibility with UI
  [JsonProperty("3")] public int _3;
  [JsonProperty("4")] public int _4;
  [JsonProperty("5")] public int _5;
}

[Serializable]
public class ServerUIData
{
  public PaylineData paylines;
}

[Serializable]
public class PaylineData
{
  public List<ServerSymbolInfo> symbols;
}

[Serializable]
public class ServerSymbolInfo
{
  public int id;
  public string name;
  public string description;

  /// <summary>
  /// Payout keyed by match count: { "3": 20, "4": 125, "5": 200 }. The jackpot symbols
  /// (ids 15-20) carry a single "1" key instead, since they pay on one symbol.
  /// Symbols with no payout at all (Wild, Mystery, the coins) send no key.
  /// </summary>
  public Dictionary<string, double> payout;
}


[Serializable]
public class ServerPlayer
{
  public double balance;
}

// ============================================================================
// FIXED: Server Response Models - Must match actual server JSON structure
// ============================================================================

[Serializable]
public class ServerSpinResponse
{
  public string id = "ResultData";
  public bool success;
  public List<List<string>> matrix; // Root level matrix sent by server
  public ServerPlayerBalance player;
  public ServerPayload payload;
}

[Serializable]
public class ServerPlayerBalance
{
  public double? balance; // Nullable because server sends null
}

[Serializable]
public class ServerPayload
{
  public double winAmount;

  /// <summary>One entry per winning payline. Empty on a losing spin.</summary>
  public List<ServerLineWin> lineWins;

  /// <summary>Mystery tiles that opened this spin, as [row, col].</summary>
  public List<ServerMysteryReveal> mysteryReveals;

  /// <summary>Coins stamped on top of a landed cell. Drives the coin flight beat.</summary>
  public List<ServerCoinOverlay> coinOverlays;

  /// <summary>Populated only when a jackpot tier's meter filled and paid out.</summary>
  public List<ServerJackpotWin> jackpotWin;

  /// <summary>e.g. ["bluePig"] on a trigger spin, ["jackpot_Grand"] on a jackpot award.</summary>
  public List<string> triggeredFeatures;

  /// <summary>
  /// Features running while a free-spin round is live — "blue" / "yellow" / "red";
  /// empty or null in the base game. Same shape as the init block's field.
  /// </summary>
  [JsonConverter(typeof(StringOrArrayConverter))]
  public List<string> activeFeature;
  public int freeSpinsRemaining;

  /// <summary>
  /// Coins collected per jackpot tier during Yellow free spins, against the tier's space
  /// count. Present only while activeFeature is "yellow". Parsed; not rendered yet.
  /// </summary>
  public Dictionary<string, int> yellowFSCollections;

  /// <summary>Meter values AFTER this spin. The client diffs these to attribute coins.</summary>
  public ServerMeters meters;
}

/// <summary>
/// A coin stamped on a landed cell. Per BackendResponses.md section 4 these may arrive
/// with or without a matching entry in <see cref="ServerPayload.mysteryReveals"/> — a coin
/// on a Mystery cell stays hidden until that locker opens, one on a plain cell is visible
/// as soon as the column parks.
/// </summary>
[Serializable]
public class ServerCoinOverlay
{
  /// <summary>[row, col] of the cell the coin sits on.</summary>
  public List<int> position;

  /// <summary>"BlueCoin" / "YellowCoin" / "RedCoin".</summary>
  public string coin;

  /// <summary>Symbol id, 12-14. Authoritative; <see cref="coin"/> is the readable form.</summary>
  public int coinId;
}

[Serializable]
public class ServerJackpotWin
{
  public int symbolId;
  public string symbolName;
  public double winAmount;

  /// <summary>[row, col] of the cell that completed the tier.</summary>
  public List<int> position;
}

/// <summary>
/// One winning payline. <see cref="lineIndex"/> is a REAL index into
/// GameConfig.paylines — unlike the CNY ways-wins path, which produced a synthetic
/// incrementing id.
/// </summary>
[Serializable]
public class ServerLineWin
{
  public int lineIndex;
  public int symbolId;
  public string symbolName;
  public int matchCount;

  /// <summary>Paytable value in credits for this match count.</summary>
  public double payout;

  /// <summary>Cash won on this line (payout x bet).</summary>
  public double winAmount;

  /// <summary>
  /// Winning cells as "row,col" strings, ordered left-to-right from the leftmost reel:
  /// [ "0,0", "0,1", "0,2" ]. NOTE the row-first order — the client's resultMatrix is
  /// the other way round (column-major).
  /// </summary>
  public List<string> positions;
}

[Serializable]
public class ServerMysteryReveal
{
  /// <summary>[row, col] of the Mystery tile that was revealed.</summary>
  public List<int> position;
  public int revealedSymbolId;
  public string revealedSymbolName;
}

// ============================================================================
// Client-Side Spin Request
// ============================================================================

[Serializable]
public class SpinRequest
{
  public string type = "SPIN";
  public SpinPayload payload;
}

[Serializable]
public class SpinPayload
{
  public int betIndex;
  public bool isFreeSpin;
}




#endregion

#region Game Configuration (Client Side Converted)

[Serializable]
public class GameConfig
{
  public int reelCount = 5;
  public int rowCount = 3;
  public int symbolCount = RichPiggiesSymbols.TotalSymbolCount;
  public int paylineCount = 25;
  public List<List<int>> paylines;
  public List<double> availableBets;
  public List<SymbolInfo> symbols;

  // Wild configuration
  public int wildSymbolId = RichPiggiesSymbols.Wild;

  // The locker tile. Reveals a paying symbol / Wild, and may stamp a coin on top,
  // both BEFORE pays are evaluated — see the Mystery reveal beat in SlotView.
  public int mysterySymbolId = RichPiggiesSymbols.Mystery;

  // Blue / Yellow / Red. These never land on the strip; they arrive as a Mystery
  // overlay only. Any combination of them triggers Free Spins.
  public int blueCoinSymbolId = RichPiggiesSymbols.BlueCoin;
  public int yellowCoinSymbolId = RichPiggiesSymbols.YellowCoin;
  public int redCoinSymbolId = RichPiggiesSymbols.RedCoin;

  // Minimum symbols on adjacent reels from the leftmost reel for a line to pay.
  public int minMatchCount = 3;

  // [CNY] Rich Piggies has no single scatter — the three coins replace it. Kept only
  // so the remaining CNY UI paths compile; do not build new logic on it.
  public int scatterSymbolId = -1;

  public int betMultiplier = 1;      // CNY is cash-bet based, multiplier default is 1
  public double creditDivisor = 25;  // Credit divisor sent in initData
  public int maxWinMultiplier = 10000;
  public int minWinMultiplier = 10;
  public int initialFreeSpins = 12;
  public ExtraSpinsData extraSpinsData; // Keep to avoid compilation error in UI

  /// <summary>
  /// The raw init "features" block — pig meter configuration plus the live meter state.
  /// Carried verbatim; the meters / jackpot / Mystery presentation that reads it is still
  /// to be built.
  /// </summary>
  public ServerFeatures features;
}

[Serializable]
public class SymbolInfo
{
  public int id;
  public string name;

  /// <summary>Payouts ordered by match count DESCENDING — index 0 is the longest run.</summary>
  public List<double> multipliers;

  /// <summary>
  /// Match count for each entry in <see cref="multipliers"/>, same order. Usually 5/4/3,
  /// but the jackpot symbols pay on a single symbol so theirs is just { 1 }.
  /// </summary>
  public List<int> matchCounts;

  public bool isWild;
  public bool isScatter;
  public int wildMultiplier = 1;
  public int minMatch;
}

#endregion

#region Player & Game State (Client Side)

[Serializable]
public class PlayerData
{
  public double balance;
  public int currentBetIndex;
}

[Serializable]
public class SpinResult
{
  public List<List<int>> resultMatrix;  // Client uses int matrix
  public double winAmount;
  public double grandTotalWin;
  public List<WinLine> winLines;

  // Mystery tiles that opened this spin. resultMatrix already holds the REVEALED symbol at
  // each of these positions — this list only says which cells were behind a locker, so the
  // client can cover them and play the reveal.
  public List<ServerMysteryReveal> mysteryReveals;

  // Coins stamped on landed cells this spin, in server order. The order matters: the
  // client attributes meter movement to coins by walking this list against the meter diff.
  public List<ServerCoinOverlay> coinOverlays;

  // Meter values AFTER this spin, server-authoritative. Diffed against the client's cached
  // snapshot to work out which coin moved which meter and by how much.
  public ServerMeters meters;

  public List<ServerJackpotWin> jackpotWins;
  public List<string> triggeredFeatures;
  public List<string> activeFeature;
  public Dictionary<string, int> yellowFSCollections;

  public PlayerData playerData;
  public FreeSpinData freeSpinData;
  public ScatterData scatterData;
  public OverlayScatterData overlayScatterData; // Keep for safety/UI compilation
  public Dictionary<string, int> stickyWilds;  // Keep for safety/UI compilation

  // Server-authoritative free spin state
  public int serverSpinsRemaining;
  public int serverSpinsUsed;
  public int serverTotalSpins;
  public double serverTotalRoundWin;
  public bool isRoundOver;

  // Server-authoritative wheel data
  public USpinResultData uSpinData;
  public MoneyBagResultData moneyBagData;

  public double GetMoneyBagWin()
  {
    return (moneyBagData != null && moneyBagData.triggered) ? moneyBagData.winInCash : 0;
  }

  public double GetUSpinCashWin()
  {
    return (uSpinData != null && uSpinData.triggered && uSpinData.type == "MULTIPLIER") ? uSpinData.winInCash : 0;
  }

  public double GetTotalFeatureDeferredWins()
  {
    return GetMoneyBagWin() + GetUSpinCashWin();
  }
}

[Serializable]
public class WinLine
{
  public int lineId;
  public int symbolId;
  public List<int> positions;  // Flat list: [row * 5 + col]
  public double winAmount;
}

[Serializable]
public class FreeSpinData
{
  public bool isTriggered;
  public int spinsAwarded;
  public int remainingSpins;
  public bool isBought;
}

[Serializable]
public class ScatterData
{
  public bool isTriggered;
  public int scatterCount;
  public double winAmount;
}

[Serializable]
public class OverlayScatterData
{
  public bool isTriggered;
  public int count;
  public int extraSpins;
  public List<List<int>> positions;
}

[Serializable]
public class USpinResultData
{
  public bool triggered;
  public int sliceIndex;
  public string type;
  public double multiplierAwarded;
  public int freeGamesAwarded;
  public double winInCash;
}

[Serializable]
public class MoneyBagResultData
{
  public bool triggered;
  public int pickedIndex;
  public List<int> revealed;
  public int creditsAwarded;
  public double winInCash;
}

#endregion

#region Platform Communication

[Serializable]
public class AuthData
{
  public string token;
  public string socketURL;
  public string nameSpace;
}

#endregion

#region Enums

public enum GameState
{
  Initializing,
  Idle,
  Spinning,
  Stopping,
  ShowingWin,
  FreeSpinMode
}

public enum SpinSpeed
{
  Normal,
  Turbo,
  QuickSpin
}

public enum WinPopupType
{
  RegularWin,         // Normal credit win (multiplier < 500x)
  BigWin,             // Big win (multiplier >= 500x)
  FreeSpinTrigger,    // Free spins awarded from wheel
  MoneyBagCollect,    // Money bag feature collect
  FreeSpinComplete    // All free spins completed
}

#endregion

#region Helper Classes for Conversion

/// <summary>
/// Converts server data to client GameConfig
/// </summary>
public static class InitDataConverter
{
  internal static GameConfig ConvertToGameConfig(InitData serverData)
  {
    var config = new GameConfig
    {
      reelCount = DeriveReelCount(serverData.gameData),
      rowCount = DeriveRowCount(serverData.gameData),
      symbolCount = serverData.uiData.paylines.symbols.Count,
      paylineCount = (serverData.gameData.lines != null && serverData.gameData.lines.Count > 0)
          ? serverData.gameData.lines.Count
          : serverData.gameData.totalLines,
      paylines = serverData.gameData.lines,
      // minMatchCount now arrives under features, not gameData — applied below.
      availableBets = serverData.gameData.bets,
      creditDivisor = (serverData.gameData != null && serverData.gameData.creditDivisor > 0) ? serverData.gameData.creditDivisor : 25,
      symbols = new List<SymbolInfo>()
    };

    foreach (var serverSymbol in serverData.uiData.paylines.symbols)
    {
      var symbolInfo = new SymbolInfo
      {
        id = serverSymbol.id,
        name = serverSymbol.name,
        multipliers = new List<double>(),
        matchCounts = new List<int>(),
        isWild = serverSymbol.name.ToLower() == "wild",
        // [CNY] Rich Piggies has no scatter symbol; the three coins carry the trigger.
        isScatter = false
      };

      // payout arrives keyed by match count — { "3": 20, "4": 125, "5": 200 }, or a single
      // "1" key on the jackpot symbols. Order descending so multipliers[0] is the longest
      // match, which is what the paytable UI walks. matchCounts keeps the real key so the
      // UI does not have to assume the top row is a 5-of-a-kind.
      if (serverSymbol.payout != null)
      {
        var entries = new List<KeyValuePair<int, double>>();
        foreach (var kv in serverSymbol.payout)
        {
          if (int.TryParse(kv.Key, out int matchCount))
            entries.Add(new KeyValuePair<int, double>(matchCount, kv.Value));
          else
            UnityEngine.Debug.LogError($"[InitDataConverter] Symbol '{serverSymbol.name}' has a non-numeric payout key '{kv.Key}'.");
        }
        entries.Sort((a, b) => b.Key.CompareTo(a.Key));

        foreach (var entry in entries)
        {
          symbolInfo.matchCounts.Add(entry.Key);
          symbolInfo.multipliers.Add(entry.Value);
        }

        // Shortest paying run is this symbol's minimum match.
        if (entries.Count > 0) symbolInfo.minMatch = entries[entries.Count - 1].Key;
      }
      config.symbols.Add(symbolInfo);

      // Resolve the special ids by the backend's own names rather than trusting the
      // RichPiggiesSymbols constants, so a server-side renumber can't silently desync.
      switch (serverSymbol.name.ToLower())
      {
        case "wild": config.wildSymbolId = symbolInfo.id; break;
        case "mystery": config.mysterySymbolId = symbolInfo.id; break;
        case "bluecoin": config.blueCoinSymbolId = symbolInfo.id; break;
        case "yellowcoin": config.yellowCoinSymbolId = symbolInfo.id; break;
        case "redcoin": config.redCoinSymbolId = symbolInfo.id; break;
      }
    }

    if (serverData.features != null)
    {
      if (serverData.features.minMatchCount > 0)
        config.minMatchCount = serverData.features.minMatchCount;

      // The pig feature blocks and the live meters are carried on GameConfig but not yet
      // read by anything — the meters, jackpot and Mystery presentation are still to build.
      config.features = serverData.features;
    }

    return config;
  }

  /// <summary>Columns: the width of a payline, else 5.</summary>
  private static int DeriveReelCount(ServerGameData gameData)
  {
    if (gameData?.lines != null && gameData.lines.Count > 0 && gameData.lines[0] != null)
      return gameData.lines[0].Count;
    return 5;
  }

  /// <summary>
  /// Rows: one past the highest row index any payline touches — 3 for Rich Piggies, whose
  /// lines only ever name rows 0/1/2. Replaces the old
  /// "totalLines == 243 ? 3 : 1024 ? 4 : 3" guess, which returned 3 for a 25-line game
  /// only by falling through to its default branch.
  /// </summary>
  private static int DeriveRowCount(ServerGameData gameData)
  {
    int maxRow = -1;
    if (gameData?.lines != null)
    {
      foreach (var line in gameData.lines)
      {
        if (line == null) continue;
        foreach (int row in line) if (row > maxRow) maxRow = row;
      }
    }
    return maxRow >= 0 ? maxRow + 1 : 3;
  }

  internal static PlayerData ConvertToPlayerData(ServerPlayer serverPlayer, int defaultBetIndex = 0)
  {
    return new PlayerData
    {
      balance = serverPlayer.balance,
      currentBetIndex = defaultBetIndex
    };
  }

  /// <summary>
  /// Converts server response to client SpinResult
  /// </summary>
  internal static SpinResult ConvertServerResponseToSpinResult(ServerSpinResponse serverResponse, double currentBalance, double betAmount, GameConfig gameConfig)
  {
    var payload = serverResponse.payload;

    double winAmountVal = payload != null ? payload.winAmount : 0;
    double totalPay = (gameConfig != null && gameConfig.creditDivisor > 0) ? betAmount * gameConfig.creditDivisor : betAmount * 25;
    double newBalance = serverResponse.player?.balance ?? CalculateNewBalance(currentBalance, totalPay, winAmountVal);

    // Free spins are server-driven. Rich Piggies reports only how many remain; the round
    // is over the moment that hits zero while a feature is active.
    int spinsRemaining = payload != null ? payload.freeSpinsRemaining : 0;
    bool inFeature = payload?.activeFeature != null && payload.activeFeature.Count > 0;

    var result = new SpinResult
    {
      resultMatrix = ConvertReelsToMatrix(serverResponse.matrix, gameConfig),
      winAmount = winAmountVal,
      grandTotalWin = winAmountVal,
      winLines = ConvertLineWins(payload?.lineWins, gameConfig),

      // Carried verbatim — positions are [row, col] and index the SAME grid as resultMatrix,
      // which already contains the revealed symbol at each of them.
      mysteryReveals = payload?.mysteryReveals,
      coinOverlays = payload?.coinOverlays,

      // Meter state and feature signalling. meters is what the coin flight beat diffs
      // against its cached snapshot to decide which meter each coin moved.
      meters = payload?.meters,
      jackpotWins = payload?.jackpotWin,
      triggeredFeatures = payload?.triggeredFeatures,
      activeFeature = payload?.activeFeature,
      yellowFSCollections = payload?.yellowFSCollections,

      playerData = new PlayerData
      {
        balance = newBalance,
        currentBetIndex = 0
      },

      // [CNY] Free-spin / scatter / feature payloads are re-authored once the piggy
      // trigger lands. Rich Piggies signals a feature through payload.activeFeature.
      freeSpinData = null,
      scatterData = null,
      overlayScatterData = null,
      stickyWilds = null,
      uSpinData = null,
      moneyBagData = null,

      serverSpinsRemaining = spinsRemaining,
      serverSpinsUsed = 0,
      serverTotalSpins = 0,
      serverTotalRoundWin = winAmountVal,
      isRoundOver = inFeature && spinsRemaining <= 0
    };

    return result;
  }

  /// <summary>
  /// Flatten payload.lineWins into the client WinLine model.
  ///
  /// Server positions are "row,col" strings ordered left-to-right from the leftmost reel:
  /// [ "0,0", "0,1", "0,2" ]. The client encodes a cell as a row-major flat index,
  /// <c>flat = row * reelCount + col</c>, which SlotView.DecodeFlatIndex inverts.
  /// (SpinResult.resultMatrix is the other way round — column-major.)
  ///
  /// Order is preserved, so positions[0] is always the leftmost reel of the run.
  /// </summary>
  private static List<WinLine> ConvertLineWins(List<ServerLineWin> serverLineWins, GameConfig gameConfig)
  {
    var winLines = new List<WinLine>();
    if (serverLineWins == null) return winLines;

    int reelCount = (gameConfig != null && gameConfig.reelCount > 0) ? gameConfig.reelCount : 5;
    int rowCount = (gameConfig != null && gameConfig.rowCount > 0) ? gameConfig.rowCount : 3;

    foreach (var serverWin in serverLineWins)
    {
      if (serverWin == null) continue;

      var positions = new List<int>();

      if (serverWin.positions != null)
      {
        foreach (string cell in serverWin.positions)
        {
          if (!TryParseCell(cell, reelCount, rowCount, out int flatIndex))
          {
            UnityEngine.Debug.LogError(
                $"[InitDataConverter] Line {serverWin.lineIndex} has an unusable position '{cell}' — skipped.");
            continue;
          }
          positions.Add(flatIndex);
        }
      }

      if (positions.Count == 0)
      {
        UnityEngine.Debug.LogError($"[InitDataConverter] Line {serverWin.lineIndex} produced no usable positions — dropped.");
        continue;
      }

      winLines.Add(new WinLine
      {
        // A real index into GameConfig.paylines, unlike the CNY synthetic counter.
        lineId = serverWin.lineIndex,
        symbolId = serverWin.symbolId,
        positions = positions,
        winAmount = serverWin.winAmount
      });
    }

    return winLines;
  }

  /// <summary>Parse a "row,col" cell into a row-major flat index. False on anything malformed.</summary>
  private static bool TryParseCell(string cell, int reelCount, int rowCount, out int flatIndex)
  {
    flatIndex = -1;
    if (string.IsNullOrEmpty(cell)) return false;

    int comma = cell.IndexOf(',');
    if (comma <= 0 || comma >= cell.Length - 1) return false;

    if (!int.TryParse(cell.Substring(0, comma).Trim(), out int row)) return false;
    if (!int.TryParse(cell.Substring(comma + 1).Trim(), out int col)) return false;

    if (row < 0 || row >= rowCount || col < 0 || col >= reelCount) return false;

    flatIndex = row * reelCount + col;
    return true;
  }

  /// <summary>
  /// Transpose the server's ROW-major matrix (3 rows x 5 cols) into the client's
  /// COLUMN-major resultMatrix[col][row], which is what SlotView indexes by reel.
  /// </summary>
  private static List<List<int>> ConvertReelsToMatrix(List<List<string>> serverMatrix, GameConfig gameConfig)
  {
    var sourceReels = serverMatrix;
    int rowCount = gameConfig != null ? gameConfig.rowCount : 3;

    if (sourceReels == null || sourceReels.Count == 0)
    {
      UnityEngine.Debug.LogError("Invalid server reels/matrix: sourceReels is null or empty");
      return GenerateDefaultMatrix(rowCount);
    }

    int totalRows = sourceReels.Count;
    int totalCols = sourceReels[0].Count;

    var matrix = new List<List<int>>();

    for (int col = 0; col < totalCols; col++)
    {
      var column = new List<int>();
      for (int row = 0; row < totalRows; row++)
      {
        if (col >= sourceReels[row].Count)
        {
          UnityEngine.Debug.LogError($"Invalid server data at row {row}, col {col}");
          column.Add(0);
          continue;
        }

        string symbolStr = sourceReels[row][col];
        if (!int.TryParse(symbolStr, out int symbolId))
        {
          UnityEngine.Debug.LogError($"Failed to parse symbol: {symbolStr}");
          column.Add(0);
          continue;
        }

        column.Add(symbolId);
      }
      matrix.Add(column);
    }

    return matrix;
  }

  private static List<List<int>> GenerateDefaultMatrix(int rowCount)
  {
    var matrix = new List<List<int>>();
    for (int col = 0; col < 5; col++)
    {
      var column = new List<int>();
      for (int row = 0; row < rowCount; row++)
      {
        column.Add(0);
      }
      matrix.Add(column);
    }
    return matrix;
  }

  private static double CalculateNewBalance(double currentBalance, double totalPay, double winAmount)
  {
    return currentBalance + winAmount;
  }
}

#endregion
