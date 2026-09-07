# Rich Piggies (SL-RP) Backend Responses

## 1. Initialization Response (`initData`)

Sent upon connecting or initializing the game session. Contains layout rules, bets, symbol payout info, initial meter values, and active feature state.

```json
{
  "id": "initData",
  "gameData": {
    "lines": [
      [0, 0, 0, 0, 0],
      [1, 1, 1, 1, 1],
      [2, 2, 2, 2, 2]
    ],
    "bets": [0.01, 0.1, 1, 1.5, 2, 3],
    "totalLines": 25
  },
  "features": {
    "mysteryReveal": {
      "enabled": true
    },
    "bluePig": {
      "defaultMeter": 9,
      "maxMeter": 15
    },
    "yellowPig": {
      "freeSpinsCount": 8,
      "jackpotLevels": {
        "Maxi": 5,
        "Mega": 6,
        "Grand": 5,
        "Major": 4,
        "Minor": 2,
        "Mini": 2
      },
      "defaultJackpotMultipliers": {
        "Mega": 5000,
        "Grand": 1000,
        "Major": 200,
        "Maxi": 50,
        "Minor": 20,
        "Mini": 10
      }
    },
    "redPig": {
      "defaultWilds": 15,
      "maxMeter": 100,
      "freeSpinsCount": 10,
      "wildCountBuckets": [15, 30, 45, 60, 75, 100]
    },
    "minMatchCount": 3,
    "activeFeature": null,
    "freeSpinsRemaining": 0,
    "meters": {
      "blue": 9,
      "yellow": {
        "Mega": 5000,
        "Grand": 1000,
        "Major": 200,
        "Maxi": 50,
        "Minor": 20,
        "Mini": 10
      },
      "red": 15
    }
  },
  "uiData": {
    "paylines": {
      "symbols": [
        {
          "id": 0,
          "name": "BusinessPig",
          "payout": { "3": 20, "4": 125, "5": 200 },
          "description": ""
        },
        {
          "id": 10,
          "name": "Wild",
          "description": "Substitutes for all symbols except Blue Coin, Yellow Coin and Red Coin."
        },
        {
          "id": 11,
          "name": "Mystery",
          "description": "Reveals a random paying symbol or Wild before paylines are evaluated."
        }
      ]
    }
  },
  "player": {
    "balance": 1000.00
  }
}
```

---

## 2. Standard Spin Response (Base Spin)

Returned when a spin completes. `matrix` contains symbol ID strings (3 rows × 5 columns).

```json
{
  "success": true,
  "id": "ResultData",
  "matrix": [
    ["0", "5", "6", "7", "8"],
    ["9", "1", "4", "2", "0"],
    ["3", "5", "6", "7", "8"]
  ],
  "payload": {
    "winAmount": 2.50,
    "lineWins": [
      {
        "lineIndex": 0,
        "symbolId": 0,
        "symbolName": "BusinessPig",
        "matchCount": 3,
        "payout": 20,
        "winAmount": 2.50,
        "positions": ["0,0", "0,1", "0,2"]
      }
    ],
    "jackpotWin": [],
    "triggeredFeatures": [],
    "activeFeature": null,
    "freeSpinsRemaining": 0,
    "mysteryReveals": [],
    "coinOverlays": [],
    "meters": {
      "blue": 9,
      "yellow": {
        "Mega": 0,
        "Grand": 0,
        "Major": 0,
        "Maxi": 0,
        "Minor": 0,
        "Mini": 0
      },
      "red": 15
    }
  },
  "player": {
    "balance": 1001.50
  }
}
```

---

## 3. Spin with Mystery Box Reveal (`mysteryReveals`)

When Mystery Box symbols (ID `"11"`) land on the reels:
- `matrix` reflects the final revealed symbol IDs (e.g. `"0"` for `BusinessPig`).
- `mysteryReveals` provides the exact grid `position` `[row, col]`, `revealedSymbolId`, and `revealedSymbolName` so the frontend can play mystery box flip/reveal animations.

```json
{
  "success": true,
  "id": "ResultData",
  "matrix": [
    ["0", "0", "0", "7", "8"],
    ["9", "1", "4", "2", "0"],
    ["3", "5", "6", "7", "8"]
  ],
  "payload": {
    "winAmount": 5.00,
    "lineWins": [
      {
        "lineIndex": 0,
        "symbolId": 0,
        "symbolName": "BusinessPig",
        "matchCount": 3,
        "payout": 20,
        "winAmount": 5.00,
        "positions": ["0,0", "0,1", "0,2"]
      }
    ],
    "jackpotWin": [],
    "triggeredFeatures": [],
    "activeFeature": null,
    "freeSpinsRemaining": 0,
    "mysteryReveals": [
      {
        "position": [0, 1],
        "revealedSymbolId": 0,
        "revealedSymbolName": "BusinessPig"
      }
    ],
    "coinOverlays": [],
    "meters": {
      "blue": 9,
      "yellow": {
        "Mega": 0,
        "Grand": 0,
        "Major": 0,
        "Maxi": 0,
        "Minor": 0,
        "Mini": 0
      },
      "red": 15
    }
  },
  "player": {
    "balance": 1004.00
  }
}
```

---

## 4. Spin with Coin Overlays (`coinOverlays`)

When a coin overlay drops:
- `coinOverlays`: Array of `{ position: [row, col], coin: "BlueCoin" | "YellowCoin" | "RedCoin", coinId: number }`.
- `meters`: Immediately updated to reflect coin collection.
- *Note:* Coin overlays are automatically suppressed during active free spins / pig features.

```json
{
  "success": true,
  "id": "ResultData",
  "matrix": [
    ["0", "5", "6", "7", "8"],
    ["9", "1", "4", "2", "0"],
    ["3", "5", "6", "7", "8"]
  ],
  "payload": {
    "winAmount": 0,
    "lineWins": [],
    "jackpotWin": [],
    "triggeredFeatures": [],
    "activeFeature": null,
    "freeSpinsRemaining": 0,
    "mysteryReveals": [],
    "coinOverlays": [
      {
        "position": [0, 1],
        "coin": "BlueCoin",
        "coinId": 12
      },
      {
        "position": [1, 3],
        "coin": "YellowCoin",
        "coinId": 13
      }
    ],
    "meters": {
      "blue": 10,
      "yellow": {
        "Mega": 0,
        "Grand": 0.052,
        "Major": 0,
        "Maxi": 0,
        "Minor": 0,
        "Mini": 0
      },
      "red": 15
    }
  },
  "player": {
    "balance": 999.00
  }
}
```

---

## 5. Feature Triggers & Active Free Spins

### A. Blue Feature Trigger (`triggeredFeatures: ["bluePig"]`)

When Blue Free Spins feature triggers:
- `triggeredFeatures`: `["bluePig"]`
- `activeFeature`: `"blue"`
- `freeSpinsRemaining`: Free spins count (e.g. `10`)

```json
{
  "success": true,
  "id": "ResultData",
  "matrix": [
    ["0", "5", "6", "7", "8"],
    ["9", "1", "4", "2", "0"],
    ["3", "5", "6", "7", "8"]
  ],
  "payload": {
    "winAmount": 0,
    "lineWins": [],
    "jackpotWin": [],
    "triggeredFeatures": ["bluePig"],
    "activeFeature": "blue",
    "freeSpinsRemaining": 10,
    "mysteryReveals": [],
    "coinOverlays": [],
    "meters": {
      "blue": 9,
      "yellow": {
        "Mega": 0,
        "Grand": 0,
        "Major": 0,
        "Maxi": 0,
        "Minor": 0,
        "Mini": 0
      },
      "red": 15
    }
  },
  "player": {
    "balance": 999.00
  }
}
```

---

### B. Yellow Feature Trigger & Active Spins (`yellowFSCollections` & `jackpotWin`)

When Yellow Pig Feature triggers / runs:
- `triggeredFeatures`: `["yellowPig"]` on trigger spin.
- `activeFeature`: `"yellow"`.
- `yellowFSCollections`: Present in response during Yellow Free Spins, tracking collected coins per jackpot tier towards full level caps (`Maxi: 5`, `Mega: 6`, `Grand: 5`, `Major: 4`, `Minor: 2`, `Mini: 2`).
- `jackpotWin`: Populated when a jackpot level cap is completed.

#### Spin during Yellow FS (Collecting Jackpot Coins):
Matrix contains jackpot symbol IDs (e.g. `"16"` for `Grand` jackpot symbol):
```json
{
  "success": true,
  "id": "ResultData",
  "matrix": [
    ["16", "5", "6", "7", "8"],
    ["9", "1", "4", "2", "0"],
    ["16", "5", "6", "7", "8"]
  ],
  "payload": {
    "winAmount": 0,
    "lineWins": [],
    "jackpotWin": [],
    "triggeredFeatures": [],
    "activeFeature": "yellow",
    "freeSpinsRemaining": 7,
    "yellowFSCollections": {
      "Mega": 0,
      "Grand": 2,
      "Major": 0,
      "Maxi": 0,
      "Minor": 0,
      "Mini": 0
    },
    "mysteryReveals": [],
    "coinOverlays": [],
    "meters": {
      "blue": 9,
      "yellow": {
        "Mega": 0,
        "Grand": 0.052,
        "Major": 0,
        "Maxi": 0,
        "Minor": 0,
        "Mini": 0
      },
      "red": 15
    }
  },
  "player": {
    "balance": 1000.00
  }
}
```

#### Spin during Yellow FS (Jackpot Level Cap Reached & Awarded):
When `Grand` hits required level 5, `jackpotWin` contains award detail and `triggeredFeatures` includes `"jackpot_Grand"`:
```json
{
  "success": true,
  "id": "ResultData",
  "matrix": [
    ["16", "5", "6", "7", "8"],
    ["9", "1", "4", "2", "0"],
    ["16", "5", "6", "7", "8"]
  ],
  "payload": {
    "winAmount": 1052.00,
    "lineWins": [],
    "jackpotWin": [
      {
        "symbolId": 16,
        "symbolName": "Grand",
        "winAmount": 1052.00,
        "position": [0, 0]
      }
    ],
    "triggeredFeatures": ["jackpot_Grand"],
    "activeFeature": "yellow",
    "freeSpinsRemaining": 4,
    "yellowFSCollections": {
      "Mega": 0,
      "Grand": 0,
      "Major": 0,
      "Maxi": 0,
      "Minor": 0,
      "Mini": 0
    },
    "mysteryReveals": [],
    "coinOverlays": [],
    "meters": {
      "blue": 9,
      "yellow": {
        "Mega": 0,
        "Grand": 0,
        "Major": 0,
        "Maxi": 0,
        "Minor": 0,
        "Mini": 0
      },
      "red": 15
    }
  },
  "player": {
    "balance": 2052.00
  }
}
```

---

### C. Red Feature Trigger & Active Spins (`triggeredFeatures: ["redPig"]`)

When Red Free Spins feature is active:
- Extra `"10"` (`Wild`) symbols land on matrix based on current Red Wild Bucket level (`15` up to `100`).
- `triggeredFeatures`: `["redPig"]` on trigger spin.
- `activeFeature`: `"red"`.
- `freeSpinsRemaining`: Free spins remaining.

```json
{
  "success": true,
  "id": "ResultData",
  "matrix": [
    ["10", "10", "0", "7", "8"],
    ["10", "10", "4", "2", "0"],
    ["3", "10", "6", "7", "8"]
  ],
  "payload": {
    "winAmount": 15.00,
    "lineWins": [
      {
        "lineIndex": 0,
        "symbolId": 0,
        "symbolName": "BusinessPig",
        "matchCount": 3,
        "payout": 20,
        "winAmount": 15.00,
        "positions": ["0,0", "0,1", "0,2"]
      }
    ],
    "jackpotWin": [],
    "triggeredFeatures": [],
    "activeFeature": "red",
    "freeSpinsRemaining": 9,
    "mysteryReveals": [],
    "coinOverlays": [],
    "meters": {
      "blue": 9,
      "yellow": {
        "Mega": 0,
        "Grand": 0,
        "Major": 0,
        "Maxi": 0,
        "Minor": 0,
        "Mini": 0
      },
      "red": 15
    }
  },
  "player": {
    "balance": 1015.00
  }
}
```
