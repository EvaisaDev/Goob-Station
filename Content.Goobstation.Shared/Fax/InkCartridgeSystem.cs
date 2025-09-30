using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;

namespace Content.Goobstation.Shared.Fax;

public sealed class InkCartridgeSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InkCartridgeComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, InkCartridgeComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!_solutions.TryGetSolution(uid, comp.SolutionName, out _, out var solution))
            return;

        var cur = solution.Volume.Float();
        var max = solution.MaxVolume.Float();
        var pct = max <= 0 ? 0 : (cur / max) * 100f;

        args.PushMarkup($"{cur:0.#}/{max:0.#}u ({pct:0}%)");
    }
}
