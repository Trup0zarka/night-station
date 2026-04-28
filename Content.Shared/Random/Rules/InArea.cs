using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Content.Shared.Area;

namespace Content.Shared.Random.Rules;

/// <summary>
/// Returns true if entity is in specified area.
/// </summary>
public sealed partial class InArea : RulesRule
{
    [DataField("id")]
    public string ID = "";

    public override bool Check(EntityManager entManager, EntityUid uid)
    {
        var areaSys = entManager.System<AreaSystem>();
        var currentArea = areaSys.GetAreaForEntity(uid);

        return currentArea == ID;
    }
}


