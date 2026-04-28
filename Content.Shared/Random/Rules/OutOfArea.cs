using Content.Shared.Area;

namespace Content.Shared.Random.Rules;

/// <summary>
/// Returns true if entity is NOT in any area.
/// </summary>
public sealed partial class OutOfArea : RulesRule
{
    public override bool Check(EntityManager entManager, EntityUid uid)
    {
        var areaSys = entManager.System<AreaSystem>();
        var currentArea = areaSys.GetAreaForEntity(uid);

        return currentArea == null;
    }
}


