using EggSharp.GameData.Farm;
using FarmElement = Ei.ShellDB.Types.FarmElement;

namespace EggIncognito.Core.Services.Farm;

public readonly record struct CameraShot(Vec3 Focus, float Distance, float Height);

public static class FarmCameraEngine {
    public const string FocusLocator = "FarmScene::getCameraFocus 0x1000a4790";
    public const string InfoLocator = "FarmScene::getCameraInfo 0x1000a4e04";

    public static CameraShot Shot(FarmState state, FarmPlacementData data, FarmElement element, int index) {
        var focus = Focus(state, data, element, index);
        int slot = (int)element - 1;
        float distance = Pick(data.CameraDistance, slot);
        float height = Pick(data.CameraHeight, slot);
        return new CameraShot(focus, distance, height);
    }

    public static CameraShot Compose(CameraShot shot, float topUiStart, FarmPlacementData data) {
        float divisor = data.CameraUiDivisor == 0f ? 40f : data.CameraUiDivisor;
        float t = topUiStart / divisor;
        var focus = shot.Focus with { Y = shot.Height + shot.Focus.Y + (t * data.CameraUiHeightScale) };
        return new CameraShot(focus, shot.Distance + (t * data.CameraUiDistanceScale), shot.Height);
    }

    public static Vec3 Focus(FarmState state, FarmPlacementData data, FarmElement element, int index) {
        var extents = FarmPlacementEngine.ResolveExtents(state, data);
        return element switch {
            FarmElement.HenHouse => FarmPlacementEngine.HabPosition(state, data, index).Plus(data.HabFocusOffset),
            FarmElement.Silo => FarmPlacementEngine.SiloPosition(data, index),
            FarmElement.Depot => Shifted(data.DepotFocusBase, extents.Depot, data),
            FarmElement.Lab => Shifted(data.LabFocusBase, extents.Lab, data),
            FarmElement.Hatchery => new Vec3(
                ((extents.Hatchery - data.FocusExtentPivot) * data.FocusExtentScale) + data.HatcheryFocusBase.X,
                data.HatcheryFocusBase.Y, data.HatcheryFocusBase.Z),
            FarmElement.Hoa => HoaFocus(state, data, extents),
            FarmElement.MissionControl => FarmPlacementEngine.MissionControlPosition(state, data, extents),
            FarmElement.FuelTank => FarmPlacementEngine.FuelTankPosition(state, data, extents)
                .Plus(data.FuelTankFocusOffset),
            _ => Static(data, element)
        };
    }

    private static Vec3 HoaFocus(FarmState state, FarmPlacementData data, FarmExtents extents) {
        var pos = FarmPlacementEngine.HoaPosition(state, data, extents);
        return pos with { X = pos.X + data.HoaFocusExtra };
    }

    private static Vec3 Shifted(Vec3 basePos, float extent, FarmPlacementData data) =>
        basePos with { X = basePos.X + ((extent - data.FocusExtentPivot) * data.FocusExtentScale) };

    private static Vec3 Static(FarmPlacementData data, FarmElement element) {
        int slot = (int)element - 1;
        return slot >= 0 && slot < data.CameraStaticFocus.Count ? data.CameraStaticFocus[slot] : Vec3.Zero;
    }

    private static float Pick(IReadOnlyList<float> table, int index) =>
        index >= 0 && index < table.Count ? table[index] : 0f;
}
