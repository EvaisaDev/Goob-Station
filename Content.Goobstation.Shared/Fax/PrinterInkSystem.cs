using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;
using Vector3 = Robust.Shared.Maths.Vector3;
using Content.Shared.Chemistry.Components;
using Robust.Shared.GameObjects;

namespace Content.Goobstation.Shared.Fax;

public sealed class PrinterInkSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private enum Channel { C, M, Y, K }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrinterInkComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<PrinterInkComponent, ComponentRemove>(OnRemove);
    }

    private void OnInit(EntityUid uid, PrinterInkComponent comp, ComponentInit args)
    {
        AddSlotIfMissing(uid, comp.SlotCId, comp.SlotC);
        AddSlotIfMissing(uid, comp.SlotMId, comp.SlotM);
        AddSlotIfMissing(uid, comp.SlotYId, comp.SlotY);
        AddSlotIfMissing(uid, comp.SlotKId, comp.SlotK);
    }

    private void OnRemove(EntityUid uid, PrinterInkComponent comp, ComponentRemove args)
    {
        _slots.RemoveItemSlot(uid, comp.SlotC);
        _slots.RemoveItemSlot(uid, comp.SlotM);
        _slots.RemoveItemSlot(uid, comp.SlotY);
        _slots.RemoveItemSlot(uid, comp.SlotK);
    }

    public string AdjustAndConsume(EntityUid fax, string raw)
    {
        if (!TryComp<PrinterInkComponent>(fax, out var comp))
            return raw;

        var parsed = FormattedMessage.FromMarkupPermissive(raw, out _);
        if (parsed.IsEmpty)
            return raw;

        var sC = ChannelScale(fax, comp, Channel.C);
        var sM = ChannelScale(fax, comp, Channel.M);
        var sY = ChannelScale(fax, comp, Channel.Y);
        var sK = ChannelScale(fax, comp, Channel.K);

        var colC = CartridgeColor(fax, comp, Channel.C);
        var colM = CartridgeColor(fax, comp, Channel.M);
        var colY = CartridgeColor(fax, comp, Channel.Y);
        var colK = CartridgeColor(fax, comp, Channel.K);

        var colorStack = new Stack<Color>();
        var current = Color.Black;
        var segs = new List<(Color color, string text)>();

        foreach (var node in parsed.Nodes)
        {
            if (node.Name == null)
            {
                if (node.Value.TryGetString(out var text) && !string.IsNullOrEmpty(text))
                    segs.Add((current, text));
                continue;
            }

            if (node.Name == "color")
            {
                if (node.Value.TryGetColor(out var col))
                {
                    colorStack.Push(current);
                    current = col ?? Color.Black;
                }
                else
                {
                    current = colorStack.Count > 0 ? colorStack.Pop() : Color.Black;
                }
            }
        }

        if (segs.Count == 0)
        {
            var (c, m, y, k) = RgbToCmyk(current);
            var runes = CountRunes(raw);
            var units = comp.UnitsPerCharacter * runes;
            var cUse = c * sC * units;
            var mUse = m * sM * units;
            var yUse = y * sY * units;
            var kUse = k * sK * units;
            if (cUse > 0) RemoveAmount(fax, comp, Channel.C, cUse);
            if (mUse > 0) RemoveAmount(fax, comp, Channel.M, mUse);
            if (yUse > 0) RemoveAmount(fax, comp, Channel.Y, yUse);
            if (kUse > 0) RemoveAmount(fax, comp, Channel.K, kUse);

            var adj = MixChannels(colC, colM, colY, colK, c * sC, m * sM, y * sY, k * sK);
            var hex = ToHex(adj);
            return $"[color={hex}]{raw}[/color]";
        }

        var sb = new StringBuilder(segs.Count * 32);
        float useC = 0f, useM = 0f, useY = 0f, useK = 0f;

        foreach (var (col, text) in segs)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                sb.Append(text);
                continue;
            }

            var (c, m, y, k) = RgbToCmyk(col);
            var runes = CountRunes(text);
            var units = comp.UnitsPerCharacter * runes;
            useC += c * sC * units;
            useM += m * sM * units;
            useY += y * sY * units;
            useK += k * sK * units;

            var adj = MixChannels(colC, colM, colY, colK, c * sC, m * sM, y * sY, k * sK);
            sb.Append("[color=").Append(ToHex(adj)).Append(']').Append(text).Append("[/color]");
        }

        if (useC > 0f) RemoveAmount(fax, comp, Channel.C, useC);
        if (useM > 0f) RemoveAmount(fax, comp, Channel.M, useM);
        if (useY > 0f) RemoveAmount(fax, comp, Channel.Y, useY);
        if (useK > 0f) RemoveAmount(fax, comp, Channel.K, useK);

        return sb.ToString();
    }

    private float ChannelScale(EntityUid fax, PrinterInkComponent comp, Channel ch)
    {
        if (!TryGetSolutionForChannel(fax, comp, ch, out _, out var solution))
            return 0f;

        var qty = solution!.Volume;
        if (qty <= FixedPoint2.Zero)
            return 0f;

        var max = solution.MaxVolume;
        if (max <= FixedPoint2.Zero)
            return 1f;

        var p = qty.Float() / max.Float();
        if (p >= 0.10f)
            return 1f;

        var s = p / 0.10f;
        return MathF.Max(0f, MathF.Min(1f, s));
    }

    private void RemoveAmount(EntityUid fax, PrinterInkComponent comp, Channel ch, float amount)
    {
        if (amount <= 0f)
            return;

        if (!TryGetSolutionForChannel(fax, comp, ch, out var solEnt, out var solution))
            return;

        var total = solution!.Volume.Float();
        if (total <= 0f)
            return;

        var frac = MathF.Min(1f, amount / total);
        var list = solution.Contents;
        for (var i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            var take = entry.Quantity.Float() * frac;
            if (take > 0f)
                _solutions.RemoveReagent(solEnt!.Value, entry.Reagent, FixedPoint2.New(take));
        }
    }

    private Color CartridgeColor(EntityUid fax, PrinterInkComponent comp, Channel ch)
    {
        if (!TryGetSolutionForChannel(fax, comp, ch, out _, out var solution))
            return Color.White;

        var list = solution!.Contents;
        var total = 0f;
        var ar = 0f; var ag = 0f; var ab = 0f;

        for (var i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            var q = entry.Quantity.Float();
            if (q <= 0f)
                continue;

            var rp = _proto.Index<ReagentPrototype>(entry.Reagent.Prototype);
            var col = rp.SubstanceColor;
            ar += col.R * q;
            ag += col.G * q;
            ab += col.B * q;
            total += q;
        }

        if (total <= 0f)
            return Color.White;

        var r = Math.Clamp(ar / total, 0f, 1f);
        var g = Math.Clamp(ag / total, 0f, 1f);
        var b = Math.Clamp(ab / total, 0f, 1f);
        return new Color(r, g, b);
    }

    private static (float c, float m, float y, float k) RgbToCmyk(Color col)
    {
        var r = Math.Clamp(col.R, 0f, 1f);
        var g = Math.Clamp(col.G, 0f, 1f);
        var b = Math.Clamp(col.B, 0f, 1f);
        var k = 1f - MathF.Max(r, MathF.Max(g, b));
        var denom = 1f - k;
        var c = denom <= 0f ? 0f : (1f - r - k) / denom;
        var m = denom <= 0f ? 0f : (1f - g - k) / denom;
        var y = denom <= 0f ? 0f : (1f - b - k) / denom;
        return (Math.Clamp(c, 0f, 1f), Math.Clamp(m, 0f, 1f), Math.Clamp(y, 0f, 1f), Math.Clamp(k, 0f, 1f));
    }

    private static Color MixChannels(Color colC, Color colM, Color colY, Color colK, float c, float m, float y, float k)
    {
        var rgb = new System.Numerics.Vector3(1f, 1f, 1f);
        var inks = new (Color col, float amt)[] { (colC, c), (colM, m), (colY, y), (colK, k) };

        foreach (var (col, amtRaw) in inks)
        {
            var amt = Math.Clamp(amtRaw, 0f, 1f);
            var ink = new System.Numerics.Vector3(col.R, col.G, col.B);
            rgb *= System.Numerics.Vector3.Lerp(System.Numerics.Vector3.One, ink, amt);
        }

        return new Color(
            Math.Clamp(rgb.X, 0f, 1f),
            Math.Clamp(rgb.Y, 0f, 1f),
            Math.Clamp(rgb.Z, 0f, 1f));
    }

    private static string ToHex(Color col)
    {
        var r = (int) Math.Clamp(MathF.Round(col.R * 255f), 0f, 255f);
        var g = (int) Math.Clamp(MathF.Round(col.G * 255f), 0f, 255f);
        var b = (int) Math.Clamp(MathF.Round(col.B * 255f), 0f, 255f);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private void AddSlotIfMissing(EntityUid uid, string id, ItemSlot slot)
    {
        if (!_slots.TryGetSlot(uid, id, out _))
            _slots.AddItemSlot(uid, id, slot);
    }

    private static int CountRunes(string s)
    {
        var fm = FormattedMessage.FromUnformatted(s);
        var e = fm.EnumerateRunes();
        var n = 0;
        foreach (var _ in e) n++;
        return n;
    }

    private static string GetInkId(PrinterInkComponent comp, Channel ch) =>
        ch switch { Channel.C => comp.SlotCId, Channel.M => comp.SlotMId, Channel.Y => comp.SlotYId, _ => comp.SlotKId };

    private bool TryGetSolutionForChannel(EntityUid fax, PrinterInkComponent comp, Channel ch, out Entity<SolutionComponent>? solEnt, out Solution? solution)
    {
        solEnt = null;
        solution = null;
        var id = GetInkId(comp, ch);
        if (!_slots.TryGetSlot(fax, id, out var slot))
            return false;
        var item = slot.Item;
        if (item is null)
            return false;
        if (!_solutions.TryGetSolution(item.Value, comp.SolutionName, out solEnt, out solution))
            return false;
        return true;
    }
}
