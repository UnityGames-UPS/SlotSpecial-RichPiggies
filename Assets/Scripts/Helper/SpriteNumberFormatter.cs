using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Renders a number into TMP sprite tags for this game's custom number sheets.
///
/// Every sheet in Assets/Fonts/CustomTextFonts is laid out "1 2 3 4 / 5 6 7 8 / 9 0 . ,",
/// so the slice named _0 is the glyph "1", not "0". Whoever authored the assets compensated
/// by ordering the sprite CHARACTER table _9, _0, _1 ... _8, _10, _11 — and it is that table
/// index, not the slice name, that a &lt;sprite=N&gt; tag resolves against. The net effect is
/// the identity mapping below, and all seven sheets share the same table, so one formatter
/// serves every one of them:
///
///     digit d -> &lt;sprite=d&gt;      '.' -> &lt;sprite=10&gt;      ',' -> &lt;sprite=11&gt;
///
/// Do not "simplify" this by assuming slice _N is digit N. It isn't, and the result is a
/// number that is off by one digit everywhere without looking obviously broken.
/// </summary>
internal static class SpriteNumberFormatter
{
  private const int DecimalPointSpriteIndex = 10;
  private const int GroupSeparatorSpriteIndex = 11;

  // The sheets carry digits, a point and a comma — nothing else. Warn once rather than on
  // every frame a stray character shows up, and never emit a tag we know has no glyph.
  private static bool warnedUnsupportedCharacter;

  /// <summary>
  /// Format <paramref name="value"/> as a run of sprite tags.
  ///
  /// Decimals are a MAXIMUM, not a fixed width: trailing zeros are dropped, so 2 renders as
  /// "2" and 2.5 as "2.5" rather than "2.00" / "2.50". That matches the convention the HUD
  /// already uses (UIManager.FormatAmount is ToString("0.###")) — keep the two in step, or a
  /// win will read one way in the popup and another way in the win field.
  ///
  /// The one deliberate difference is grouping: the HUD's "0.###" has no thousands
  /// separators, while this defaults to adding them, because these sheets ship a comma glyph
  /// specifically for it. Pass grouping:false to match the HUD exactly.
  /// </summary>
  /// <param name="maxDecimals">Upper bound on decimal places. 3 gives exactly "0.###".</param>
  /// <param name="grouping">Thousands separators, rendered with the sheet's comma glyph.</param>
  /// <param name="tint">
  /// Emit <c>tint=1</c> on every tag, which makes the glyphs take the TMP_Text's own colour
  /// instead of the sprite's. Needed by any label that is FADED — DOFade writes the text
  /// colour, and an untinted sprite ignores it, so the number stays fully opaque while the
  /// rest of the label fades. Leave it off where the label is only shown and hidden.
  /// </param>
  internal static string Format(double value, int maxDecimals = 3, bool grouping = true,
                                bool tint = false)
  {
    if (maxDecimals < 0) maxDecimals = 0;

    // InvariantCulture, always. The sprite indices are fixed to '.' as the decimal point and
    // ',' as the group separator; a device locale that swaps the two (de-DE, fr-FR) would
    // otherwise render "1.234,50" through glyphs that mean the opposite.
    string pattern = (grouping ? "#,0" : "0")
                   + (maxDecimals > 0 ? "." + new string('#', maxDecimals) : string.Empty);

    return Render(value.ToString(pattern, CultureInfo.InvariantCulture), tint);
  }

  /// <summary>
  /// Format with EXACTLY <paramref name="decimals"/> decimal places, trailing zeros kept —
  /// 2 at 2 decimals renders as "2.00", not "2".
  ///
  /// This is for a number being ANIMATED. <see cref="Format"/>'s dropped trailing zeros make
  /// a counting label jitter: the digit count changes as the value climbs, so the number
  /// visibly changes width and the decimal point slides around from frame to frame. Pin the
  /// width for the whole count instead, using <see cref="DecimalsUsed"/> on the final value
  /// to decide it — then the count ends on exactly what <see cref="Format"/> would have
  /// produced anyway, and nothing shifts on the way there.
  /// </summary>
  internal static string FormatFixed(double value, int decimals, bool grouping = true,
                                     bool tint = false)
  {
    if (decimals < 0) decimals = 0;

    string pattern = (grouping ? "#,0" : "0")
                   + (decimals > 0 ? "." + new string('0', decimals) : string.Empty);

    return Render(value.ToString(pattern, CultureInfo.InvariantCulture), tint);
  }

  /// <summary>
  /// How many decimal places <paramref name="value"/> actually needs, from 0 to
  /// <paramref name="maxDecimals"/> — i.e. what <see cref="Format"/> would end up showing.
  /// Feed it to <see cref="FormatFixed"/> to hold that width steady across an animation.
  /// </summary>
  internal static int DecimalsUsed(double value, int maxDecimals = 3)
  {
    if (maxDecimals <= 0) return 0;

    string plain = value.ToString("0." + new string('#', maxDecimals),
                                  CultureInfo.InvariantCulture);

    int point = plain.IndexOf('.');
    return point < 0 ? 0 : plain.Length - point - 1;
  }

  private static string Render(string plain, bool tint)
  {
    var builder = new StringBuilder(plain.Length * 11);

    foreach (char c in plain)
    {
      if (c >= '0' && c <= '9') AppendSprite(builder, c - '0', tint);
      else if (c == '.') AppendSprite(builder, DecimalPointSpriteIndex, tint);
      else if (c == ',') AppendSprite(builder, GroupSeparatorSpriteIndex, tint);
      else if (!warnedUnsupportedCharacter)
      {
        warnedUnsupportedCharacter = true;
        Debug.LogWarning($"[SpriteNumberFormatter] '{c}' in \"{plain}\" has no glyph in the " +
                         "custom number sheets and was dropped. These sheets carry digits, " +
                         "'.' and ',' only — no minus sign and no currency symbol.");
      }
    }

    return builder.ToString();
  }

  /// <summary>Write a value to one text. For UI that is shared across both orientations.</summary>
  internal static void Apply(TMP_Text text, double value, int maxDecimals = 3,
                             bool grouping = true, bool tint = false)
  {
    if (text != null) text.text = Format(value, maxDecimals, grouping, tint);
  }

  /// <summary>
  /// Write a value at a FIXED decimal width. For a label whose number is animating — see
  /// <see cref="FormatFixed"/>.
  /// </summary>
  internal static void ApplyFixed(TMP_Text text, double value, int decimals,
                                  bool grouping = true, bool tint = false)
  {
    if (text != null) text.text = FormatFixed(value, decimals, grouping, tint);
  }

  /// <summary>
  /// Write a value to a landscape/portrait text pair, the way UIManager.SetTMPText does:
  /// null-safe, and BOTH refs updated unconditionally so the off-screen one is already
  /// correct the moment the device rotates.
  /// </summary>
  internal static void Apply(TMP_Text landscape, TMP_Text portrait, double value,
                             int maxDecimals = 3, bool grouping = true, bool tint = false)
  {
    if (landscape == null && portrait == null) return;

    string formatted = Format(value, maxDecimals, grouping, tint);
    if (landscape != null) landscape.text = formatted;
    if (portrait != null) portrait.text = formatted;
  }

  /// <summary>Blank one text.</summary>
  internal static void Clear(TMP_Text text)
  {
    if (text != null) text.text = string.Empty;
  }

  /// <summary>Blank both refs of a landscape/portrait pair.</summary>
  internal static void Clear(TMP_Text landscape, TMP_Text portrait)
  {
    if (landscape != null) landscape.text = string.Empty;
    if (portrait != null) portrait.text = string.Empty;
  }

  private static void AppendSprite(StringBuilder builder, int index, bool tint)
  {
    builder.Append("<sprite=").Append(index);
    if (tint) builder.Append(" tint=1");
    builder.Append('>');
  }
}
