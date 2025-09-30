using Robust.Shared.GameStates;

namespace Content.Goobstation.Shared.Fax;

[RegisterComponent, NetworkedComponent]
public sealed partial class InkCartridgeComponent : Component
{
    [DataField] public string SolutionName = "ink";
}
