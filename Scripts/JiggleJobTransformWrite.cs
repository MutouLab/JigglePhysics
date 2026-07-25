using Unity.Burst;
using Unity.Collections;
using UnityEngine.Jobs;

namespace GatorDragonGames.JigglePhysics {

[BurstCompile]
public struct JiggleJobTransformWrite : IJobParallelForTransform {
    public NativeArray<JiggleTransform> previousLocalPoses;
    [ReadOnly] public NativeArray<JiggleTransform> inputInterpolatedPoses;

    public JiggleJobTransformWrite(JiggleMemoryBus bus) {
        previousLocalPoses = bus.previousLocalRestPoseTransforms;
        inputInterpolatedPoses = bus.interpolationOutputPoses;
    }

    public void UpdateArrays(JiggleMemoryBus bus) {
        previousLocalPoses = bus.previousLocalRestPoseTransforms;
        inputInterpolatedPoses = bus.interpolationOutputPoses;
    }

    public void Execute(int index, TransformAccess transform) {
        if (!transform.isValid) {
            return;
        }

        var pose = inputInterpolatedPoses[index];
        if (pose.isVirtual) {
            return;
        }

        transform.SetPositionAndRotation(pose.position, pose.rotation);
        // Backward-compat gate for the squash feature: only ever touch localScale when the source point actually
        // opted in (squash > 0), so rigs that leave squash at its default of 0 never have their scale written,
        // regardless of what pose.scale happens to contain.
        if (pose.writeScale) {
            transform.localScale = pose.scale;
        }
        transform.GetLocalPositionAndRotation(out var localPosition, out var localRotation);

        var previousLocalPose = previousLocalPoses[index];
        previousLocalPose.position = localPosition;
        previousLocalPose.rotation = localRotation;
        previousLocalPoses[index] = previousLocalPose;
    }
}

}