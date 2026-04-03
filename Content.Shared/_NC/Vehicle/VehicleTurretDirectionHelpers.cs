using Robust.Shared.Maths;

namespace Content.Shared._NC.Vehicle;

public static class VehicleTurretDirectionHelpers
{
    public static Direction GetRenderAlignedCardinalDir(Angle angle)
    {
        var reduced = angle.Reduced();
        var theta = reduced.Theta;

        if (theta < 0)
            theta += MathHelper.TwoPi;

        if (theta < MathHelper.PiOver4 || theta >= 7 * MathHelper.PiOver4)
            return Direction.South;

        if (theta < 3 * MathHelper.PiOver4)
            return Direction.East;

        if (theta < 5 * MathHelper.PiOver4)
            return Direction.North;

        return Direction.West;
    }
}
