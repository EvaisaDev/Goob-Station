using Robust.Shared.GameStates;
using Content.Shared.Containers.ItemSlots;

namespace Content.Goobstation.Shared.Fax;

[RegisterComponent, NetworkedComponent]
public sealed partial class PrinterInkComponent : Component
{
    [DataField]
    public string SlotCId = "InkC";

    [DataField]
    public string SlotMId = "InkM";

    [DataField]
    public string SlotYId = "InkY";

    [DataField]
    public string SlotKId = "InkK";


    [DataField(required: true)]
    public ItemSlot SlotC = new();

    [DataField(required: true)]
    public ItemSlot SlotM = new();

    [DataField(required: true)]
    public ItemSlot SlotY = new();

    [DataField(required: true)]
    public ItemSlot SlotK = new();

    [DataField] public string SolutionName = "ink";

    [DataField] public float UnitsPerCharacter = 0.05f;

    [DataField] public bool RequireInkToPrint = false;
}
