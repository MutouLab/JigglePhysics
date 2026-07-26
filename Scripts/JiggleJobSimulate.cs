using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GatorDragonGames.JigglePhysics {

[BurstCompile]
public struct JiggleJobSimulate : IJobFor {
    // TODO: doubles are strictly a bad way to track time, probably should be ints or longs.
    public double timeStamp;
    public float3 gravity;
    public int timeIncrements;
    
    public int sceneColliderCount;

    [ReadOnly] [NativeDisableParallelForRestriction]
    public NativeArray<JiggleTransform> inputPoses;

    // The bone's rest/authored local scale, captured when its tree is (re)built (see JiggleMemoryBus.AddTreeToSlice).
    // Only read for squash: it's the base scale ApplyPose's length-driven multiplier is applied to. Also used to
    // find each point's local-space direction to its child (the bone's axis), for squash's per-axis blend.
    [ReadOnly] [NativeDisableParallelForRestriction]
    public NativeArray<JiggleTransform> restPoseTransforms;

    [NativeDisableParallelForRestriction] public NativeArray<PoseData> outputPoses;
    [ReadOnly,NativeDisableParallelForRestriction] public NativeArray<JiggleCollider> personalColliders;
    [ReadOnly,NativeDisableParallelForRestriction] public NativeArray<JiggleCollider> sceneColliders;
    
    [ReadOnly] public NativeHashMap<int2,JiggleGridCell> broadPhaseMap;
    [ReadOnly] public NativeReference<JiggleGridCell> globalCell;

    public NativeArray<JiggleTreeJobData> jiggleTrees;

    private float deltaTimeSquared;

    public JiggleJobSimulate(JiggleMemoryBus bus, float fixedDeltaTime) {
        inputPoses = bus.simulateInputPoses;
        restPoseTransforms = bus.restPoseTransforms;
        jiggleTrees = bus.jiggleTreeStructs;
        outputPoses = bus.simulationOutputPoseData;
        personalColliders = bus.personalColliders;
        sceneColliders = bus.sceneColliders;
        timeStamp = Time.timeAsDouble;
        broadPhaseMap = bus.broadPhaseMap;
        globalCell = bus.globalCell;
        gravity = Physics.gravity;
        sceneColliderCount = 0;
        deltaTimeSquared = fixedDeltaTime * fixedDeltaTime;
        timeIncrements = 1;
    }

    public void UpdateArrays(JiggleMemoryBus bus) {
        inputPoses = bus.simulateInputPoses;
        restPoseTransforms = bus.restPoseTransforms;
        jiggleTrees = bus.jiggleTreeStructs;
        outputPoses = bus.simulationOutputPoseData;
        personalColliders = bus.personalColliders;
        sceneColliders = bus.sceneColliders;
        sceneColliderCount = bus.sceneColliderCount;
        broadPhaseMap = bus.broadPhaseMap;
        globalCell = bus.globalCell;
    }

    public void SetFixedDeltaTime(float fixedDeltaTime) {
        deltaTimeSquared = fixedDeltaTime * fixedDeltaTime;
    }


    private unsafe void Cache(ref JiggleTreeJobData tree) {
        float3 min = new float3(float.MaxValue);
        float3 max = new float3(float.MinValue);
        
        for (int i = 0; i < tree.pointCount; i++) {
            var point = tree.points[i];
            var parameters = tree.parameters[i];
            // Both are accumulated by DepenetrateCollider below and must not carry over once a collider stops
            // touching this point: contactPush keeps this frame's largest push, stepDepenetration the net one.
            point.contactPush = float3.zero;
            point.stepDepenetration = float3.zero;
            if (point.parentIndex == -1) {
                // virtual root particles
                var child = tree.points[point.childrenIndices[0]];
                var childChild = tree.points[child.childrenIndices[0]];
                var childPose = tree.GetInputPose(inputPoses, point.childrenIndices[0]);
                var childChildPose = tree.GetInputPose(inputPoses, child.childrenIndices[0]);
                if (!childChild.hasTransform) {
                    // edge case where it's a singular isolated root bone
                    point.pose = childPose.position - new float3(0f, 0.25f, 0f);
                    point.parentPose = childPose.position - new float3(0f, 0.5f, 0f);
                    point.desiredLengthToParent = 0.25f;
                } else {
                    var diff = childPose.position - childChildPose.position;
                    point.pose = childPose.position + diff;
                    point.parentPose = childPose.position + diff * 2f;
                    point.desiredLengthToParent = math.length(diff);
                }

                point.worldRadius = 0f;
                point.collisionOffset = float3.zero;
                point.workingPosition = point.pose;
            } else if (point.hasTransform) {
                // "real" particles
                var inputPose = tree.GetInputPose(inputPoses, i);
                var parent = tree.points[point.parentIndex];
                point.pose = inputPose.position;
                point.parentPose = parent.pose;
                point.desiredLengthToParent = math.distance(point.pose, parent.pose);
                var averagePointScale = (math.abs(inputPose.scale.x) + math.abs(inputPose.scale.y) + math.abs(inputPose.scale.z)) / 3f;
                point.worldRadius = parameters.collisionRadius * averagePointScale;
                // Rotation doesn't change mid-substep, so resolve the collision proxy offset to world space once
                // here rather than per depenetration check.
                point.collisionOffset = math.rotate(inputPose.rotation, parameters.collisionOffset * averagePointScale);
                var boundsExtent = math.abs(point.collisionOffset) + new float3(point.worldRadius);
                min = math.min(min, point.position-boundsExtent);
                max = math.max(max, point.position+boundsExtent);
            } else {
                // virtual end particles
                var parent = tree.points[point.parentIndex];
                point.pose = (parent.pose * 2f - parent.parentPose);
                point.parentPose = parent.pose;
                point.desiredLengthToParent = math.distance(point.pose, point.parentPose);
                point.worldRadius = 0f;
                point.collisionOffset = float3.zero;
            }
            tree.points[i] = point;
        }

        tree.minExtentPosition = JiggleGridCell.GetKeyForPosition(min);
        tree.maxExtentPosition = JiggleGridCell.GetKeyForPosition(max);
    }

    private unsafe void VerletIntegrate(JiggleTreeJobData tree) {
        
        var rootPosition = tree.points[0].workingPosition;
        var rootLastPosition = tree.points[0].position;
        var rootDelta = rootPosition - rootLastPosition;
        
        for (int i = 0; i < tree.pointCount; i++) {
            var point = tree.points[i];
            var parameters = tree.parameters + i;
            if (point.parentIndex == -1) {
                continue;
            }
            point.lastPosition += rootDelta * parameters->ignoreRootMotion;
            point.position += rootDelta * parameters->ignoreRootMotion;
            tree.points[i] = point;
        }
        
        for (int i = 0; i < tree.pointCount; i++) {
            var point = tree.points[i];
            if (point.parentIndex == -1) {
                continue;
            }
            var parent = tree.points[point.parentIndex];

            //point->debug = pointLocalPosition;

            var inverseScaleFactor = (1f / timeIncrements);
            var delta = (point.position - point.lastPosition)*inverseScaleFactor;
            var parentDelta = (parent.position - parent.lastPosition)*inverseScaleFactor;
            var localSpaceVelocity = delta - parentDelta;
            var velocity = delta - localSpaceVelocity;
            if (parent.parentIndex != -1) {
                var parentParameters = tree.parameters+point.parentIndex;
                point.workingPosition = point.position
                                         + velocity * (1f - parentParameters->airDrag)
                                         +localSpaceVelocity * (1f - parentParameters->drag)
                                         + gravity * parentParameters->gravityMultiplier * deltaTimeSquared * inverseScaleFactor;
            } else {
                var parameters = tree.parameters + i;
                point.workingPosition = point.position
                                         + velocity * (1f - parameters->airDrag)
                                         +localSpaceVelocity * (1f - parameters->drag)
                                         + gravity * parameters->gravityMultiplier * deltaTimeSquared * inverseScaleFactor;
            }
            tree.points[i] = point;
        }
    }

    quaternion FromToRotationFromNormalizedVectors(float3 from, float3 to) {
        var axis = math.normalizesafe(math.cross(from, to), new float3(0f, 0f, 1f));
        var angle = math.acos(math.clamp(math.dot(from, to), -1f, 1f));
        return quaternion.AxisAngle(axis, angle);
    }

    float float3Angle(float3 a, float3 b) {
        return math.acos(math.dot(
                    math.normalizesafe(a, new float3(0,0,1)), 
                    math.normalizesafe(b, new float3(0,0,1))
                    ));
    }
    

    private unsafe float3 DoDepenetration(JiggleSimulatedPoint* point, JiggleSimulatedPoint* otherPoint, JigglePointParameters* otherPointParameters, JiggleCollider collider) {
        if (!collider.enabled || !point->hasTransform || !otherPoint->hasTransform || point->worldRadius == 0f || otherPoint->worldRadius == 0f) {
            return new float3(0f, 0f, 0f);
        }
        switch (collider.type) {
            case JiggleCollider.JiggleColliderType.Sphere: {
                var hardness = 1f;
                var colliderPosition = collider.localToWorldMatrix.c3.xyz;
                var pointPosition = point->workingPosition + point->collisionOffset;
                var otherPosition = otherPoint->workingPosition + otherPoint->collisionOffset;
                var pointRadius = point->worldRadius;
                var colliderRadius = collider.worldRadius;
                var boneClosestPoint = GetClosestPointOnLineSegment(
                        colliderPosition,
                        pointPosition,
                        otherPosition,
                        out var tValue
                        );
                // The bone's own radius tapers across the segment, so the collisionRadius curve shapes the
                // volume within a segment instead of only stepping between them. tValue runs from this point
                // (0) to its parent (1); with a flat curve both radii match and this is a no-op.
                var combinedRadius = GetBoneRadius(point, otherPoint, tValue) + colliderRadius;
                var sphere_diff = boneClosestPoint - colliderPosition;
                var sphere_distance = math.length(sphere_diff);
                var depenetrationMagnitude = combinedRadius - sphere_distance;
                if (depenetrationMagnitude <= 0f) {
                    return float3.zero;
                }
                var depenetrationDir = math.normalizesafe(sphere_diff, new float3(0, 0, 1));
                var depenetrationVector = depenetrationDir * depenetrationMagnitude;
                var pValue = math.clamp(2f - tValue * 2f, 0f, 1f);
                depenetrationVector *= hardness * pValue;
                // TODO: find decent rigidbody solve instead of just pushing them both naively
                // This second pass measures from this point itself rather than along the segment, so it takes
                // this point's own radius (the segment taper evaluated at t = 0).
                sphere_diff = pointPosition - colliderPosition;
                sphere_distance = math.length(sphere_diff);
                depenetrationMagnitude = pointRadius + colliderRadius - sphere_distance;
                if (depenetrationMagnitude > 0f) {
                    depenetrationDir = math.normalizesafe(sphere_diff, new float3(0, 0, 1));
                    var depenetrationVector2 = depenetrationDir * depenetrationMagnitude;
                    depenetrationVector2 *= hardness;
                    // Same combination rule as the capsule case: sum clamped to the deeper push, so redundant
                    // measurements that agree keep their depth while disagreement cancels instead of being
                    // renormalized into a full-strength push along a noise direction.
                    var mag1 = math.length(depenetrationVector);
                    var mag2 = math.length(depenetrationVector2);
                    var sphere_maxMagnitude = math.max(mag1, mag2);
                    depenetrationVector += depenetrationVector2;
                    var sphere_combinedLength = math.length(depenetrationVector);
                    if (sphere_combinedLength > sphere_maxMagnitude) {
                        depenetrationVector *= sphere_maxMagnitude / sphere_combinedLength;
                    }
                }
                if (!(otherPointParameters->angleElasticity == 1f
                      && otherPointParameters->rootElasticity == 1f
                      && otherPointParameters->lengthElasticity == 1f)) {
                    return depenetrationVector;
                }
                break;
            }
            case JiggleCollider.JiggleColliderType.Capsule: {
                var hardness = 1f;
                collider.GetWorldCapsuleSegment(out var capsuleA, out var capsuleB);
                var capsuleSegment = capsuleB - capsuleA;
                var capsuleSegmentLengthSq = math.lengthsq(capsuleSegment);
                var pointPosition = point->workingPosition + point->collisionOffset;
                var otherPosition = otherPoint->workingPosition + otherPoint->collisionOffset;
                var pointRadius = point->worldRadius;
                // Two independent tests, neither of which implies the other: the bone's whole span against the
                // capsule, and this point's own sphere against it. The span is measured with the bone radius
                // interpolated at the closest approach, so an approach that lands near the parent is compared
                // against the parent's radius, which can be far smaller than this point's - leaving this point
                // inside the collider while the span reports clear. Running the point test only when the span
                // test already penetrated (the previous structure) therefore switched the push on and off
                // across that boundary. It showed up worst around the caps, where clamping the closest point to
                // an endpoint pins the capsule side in place while the bone side keeps sliding, so the two
                // tests disagree the most and the point buzzed against the cap instead of resting on it.
                // Find closest points between capsule axis and bone segment
                ClosestPointsOnTwoSegments(capsuleA, capsuleB, pointPosition, otherPosition, out var closestOnCapsule, out var closestOnBone, out var tValueBone);
                var cap_diff = closestOnBone - closestOnCapsule;
                var cap_radii = GetCapsuleRadii(collider, closestOnCapsule, capsuleA, capsuleSegment, capsuleSegmentLengthSq);
                // Same per-segment taper as the sphere case: tValueBone runs from this point (0) to its parent (1).
                var cap_hasSegmentPush = TryGetEllipsoidDepenetration(collider, cap_diff, cap_radii, GetBoneRadius(point, otherPoint, tValueBone), out var cap_segmentPush);

                // Point-to-capsule direct check
                ClosestPointOnSegment(pointPosition, capsuleA, capsuleB, out var closestOnCapsuleToPoint);
                var cap_pointDiff = pointPosition - closestOnCapsuleToPoint;
                var cap_pointRadii = GetCapsuleRadii(collider, closestOnCapsuleToPoint, capsuleA, capsuleSegment, capsuleSegmentLengthSq);
                var cap_hasPointPush = TryGetEllipsoidDepenetration(collider, cap_pointDiff, cap_pointRadii, pointRadius, out var cap_pointPush);

                if (!cap_hasSegmentPush && !cap_hasPointPush) {
                    return float3.zero;
                }

                var cap_depenetrationVector = float3.zero;
                if (cap_hasSegmentPush) {
                    var cap_pValue = math.clamp(2f - tValueBone * 2f, 0f, 1f);
                    cap_depenetrationVector = cap_segmentPush * (hardness * cap_pValue);
                }
                if (cap_hasPointPush) {
                    var cap_depenetrationVector2 = cap_pointPush * hardness;
                    // Sum, clamped to the deeper of the two - not average-then-renormalize-to-max. The two
                    // tests are redundant measurements of the same contact; when they agree the clamp leaves
                    // the deeper push intact, but around the caps they disagree the most, and renormalizing a
                    // near-cancelled average re-inflated it to a full-strength push in a noise direction (with
                    // the old (0,0,1) fallback, a world-Z shove) - the point buzzed on the cap instead of resting.
                    var mag1 = math.length(cap_depenetrationVector);
                    var mag2 = math.length(cap_depenetrationVector2);
                    var cap_maxMagnitude = math.max(mag1, mag2);
                    cap_depenetrationVector += cap_depenetrationVector2;
                    var cap_combinedLength = math.length(cap_depenetrationVector);
                    if (cap_combinedLength > cap_maxMagnitude) {
                        cap_depenetrationVector *= cap_maxMagnitude / cap_combinedLength;
                    }
                }
                if (!(otherPointParameters->angleElasticity == 1f
                      && otherPointParameters->rootElasticity == 1f
                      && otherPointParameters->lengthElasticity == 1f)) {
                    return cap_depenetrationVector;
                }
                break;
            }
            case JiggleCollider.JiggleColliderType.Plane: {
                var colliderPosition = collider.localToWorldMatrix.c3.xyz;
                var planeNormal = math.normalizesafe(collider.localToWorldMatrix.c1.xyz, new float3(0, 1, 0));
                var pointPosition = point->workingPosition + point->collisionOffset;
                var pointRadius = point->worldRadius;
                var signedDistance = math.dot(pointPosition - colliderPosition, planeNormal);
                var penetration = pointRadius - signedDistance;
                if (penetration <= 0f) {
                    return float3.zero;
                }
                var plane_depenetrationVector = planeNormal * penetration;
                if (!(otherPointParameters->angleElasticity == 1f
                      && otherPointParameters->rootElasticity == 1f
                      && otherPointParameters->lengthElasticity == 1f)) {
                    return plane_depenetrationVector;
                }
                break;
            }
        }
        return new float3(0f, 0f, 0f);
    }

    // Depenetrates a sphere of radius `extraRadius` centered `diff` away from a point on the capsule axis, out
    // of the ellipsoid whose per-local-axis semi-axes are `radii`. Returns false when they do not overlap.
    //
    // The push is directed along the ellipsoid's surface normal, not along `diff`: on a stretched cap the two
    // differ badly, and pushing radially both moved the point further than the surface is (the radial chord is
    // never the shortest way out) and kicked it sideways along the surface - the constraints dragged it back,
    // the next step disagreed again, and the contact buzzed on the cap instead of resting. The magnitude is
    // solved analytically so the pushed point lands exactly on the surface along that normal, neither short
    // (still penetrating) nor long (thrown clear, to fall back in next step).
    //
    // The surface used is the ellipsoid inflated per-axis by `extraRadius` - an approximation of the true
    // rounded-out surface, in line with the taper's authorable-over-accurate tradeoff (see
    // JiggleCollider.startRadius) - and with all three radii equal it reduces exactly to the legacy
    // sphere-vs-sphere behavior.
    private bool TryGetEllipsoidDepenetration(JiggleCollider collider, float3 diff, float3 radii, float extraRadius, out float3 depenetration) {
        depenetration = float3.zero;
        var inflated = math.max(radii + new float3(extraRadius), new float3(1e-6f));
        if (inflated.x == inflated.y && inflated.y == inflated.z) {
            // Uniform radii reduce to the plain sphere case; kept branch-light and bit-identical to the old path.
            var radius = inflated.x;
            var distanceSq = math.lengthsq(diff);
            if (distanceSq >= radius * radius) {
                return false;
            }
            depenetration = math.normalizesafe(diff, new float3(0f, 0f, 1f)) * (radius - math.sqrt(distanceSq));
            return true;
        }
        collider.GetWorldAxes(out var xAxis, out var yAxis, out var zAxis);
        var local = new float3(math.dot(diff, xAxis), math.dot(diff, yAxis), math.dot(diff, zAxis));
        var scaled = local / inflated;
        // Signed "inside-ness" in the scaled space where the ellipsoid is the unit sphere; also the constant
        // term of the exit quadratic below.
        var c = math.lengthsq(scaled) - 1f;
        if (c >= 0f) {
            return false;
        }
        // Ellipsoid gradient at the point - the surface normal direction. A (nearly) centered point has no
        // meaningful gradient and exits along the smallest axis instead, the cheapest way out.
        var normalLocal = local / (inflated * inflated);
        if (math.lengthsq(normalLocal) < 1e-18f) {
            normalLocal = inflated.x <= inflated.y && inflated.x <= inflated.z ? new float3(1f, 0f, 0f)
                : inflated.y <= inflated.z ? new float3(0f, 1f, 0f) : new float3(0f, 0f, 1f);
        }
        normalLocal = math.normalize(normalLocal);
        // Exit distance along the normal: solve |(local + t * normal) / inflated|^2 = 1 for the positive root.
        // c < 0 guarantees the discriminant is positive and the root lands outward.
        var scaledDir = normalLocal / inflated;
        var a = math.lengthsq(scaledDir);
        var b = 2f * math.dot(local / (inflated * inflated), normalLocal);
        var t = (-b + math.sqrt(b * b - 4f * a * c)) / (2f * a);
        depenetration = (normalLocal.x * xAxis + normalLocal.y * yAxis + normalLocal.z * zAxis) * t;
        return true;
    }

    // Returns the per-axis radii at `closest` (a point on the capsule's a-b segment) by interpolating between
    // the two end radii along the segment parameter t. This is an approximation, not a true tapered-capsule
    // (frustum) SDF: see the comment on JiggleCollider.startRadius.
    // Returns the bone's collision radius at parameter t along the segment running from `point` (t = 0) to its
    // parent `otherPoint` (t = 1). Each point carries its own radius from the collisionRadius curve, so
    // interpolating lets a single segment taper rather than taking one endpoint's radius for its whole length.
    private unsafe float GetBoneRadius(JiggleSimulatedPoint* point, JiggleSimulatedPoint* otherPoint, float t) {
        return math.lerp(point->worldRadius, otherPoint->worldRadius, t);
    }

    private float3 GetCapsuleRadii(JiggleCollider collider, float3 closest, float3 segA, float3 segVec, float segLengthSq) {
        var t = segLengthSq < 1e-12f ? 0f : math.saturate(math.dot(closest - segA, segVec) / segLengthSq);
        return math.lerp(collider.worldStartRadius, collider.worldEndRadius, t);
    }

    private void ClosestPointOnSegment(float3 point, float3 segA, float3 segB, out float3 closest) {
        var ab = segB - segA;
        var lengthSq = math.dot(ab, ab);
        if (lengthSq == 0f) {
            closest = segA;
            return;
        }
        var t = math.clamp(math.dot(point - segA, ab) / lengthSq, 0f, 1f);
        closest = segA + t * ab;
    }

    private void ClosestPointsOnTwoSegments(float3 a0, float3 a1, float3 b0, float3 b1, out float3 closestA, out float3 closestB, out float tB) {
        var d1 = a1 - a0;
        var d2 = b1 - b0;
        var r = a0 - b0;
        var a = math.dot(d1, d1);
        var e = math.dot(d2, d2);
        var f = math.dot(d2, r);
        float s, t;
        if (a <= 1e-8f && e <= 1e-8f) {
            s = 0f; t = 0f;
        } else if (a <= 1e-8f) {
            s = 0f;
            t = math.clamp(f / e, 0f, 1f);
        } else {
            var c = math.dot(d1, r);
            if (e <= 1e-8f) {
                t = 0f;
                s = math.clamp(-c / a, 0f, 1f);
            } else {
                var b = math.dot(d1, d2);
                var denom = a * e - b * b;
                if (denom != 0f) {
                    s = math.clamp((b * f - c * e) / denom, 0f, 1f);
                } else {
                    s = 0f;
                }
                t = (b * s + f) / e;
                if (t < 0f) {
                    t = 0f;
                    s = math.clamp(-c / a, 0f, 1f);
                } else if (t > 1f) {
                    t = 1f;
                    s = math.clamp((b - c) / a, 0f, 1f);
                }
            }
        }
        closestA = a0 + d1 * s;
        closestB = b0 + d2 * t;
        tB = t;
    }
    
    private float3 GetClosestPointOnLineSegment(float3 inputPoint, float3 segmentPoint1, float3 segmentPoint2, out float tValue) {
        tValue = 0f;
        var segment = segmentPoint2 - segmentPoint1;
        var segmentLengthSq = math.dot(segment, segment);
        if (segmentLengthSq == 0f) {
            return segmentPoint1;
        }
        tValue = math.dot(inputPoint - segmentPoint1, segment) / segmentLengthSq;
        tValue = math.clamp(tValue, 0f, 1f);
        return segmentPoint1 + tValue * segment;
    }

    // Collects one collider's depenetration into the point's pending push rather than applying it. Applying per
    // collider moved workingPosition between colliders, so each collider judged the previous one's result: a
    // point that cannot satisfy all of them at once - wedged between several - chased them in turn, the outcome
    // depended on the order they happened to be visited in, and it never came to rest. The caller combines the
    // collected pushes and applies them once, which is order-independent and settles on a compromise instead.
    private unsafe void DepenetrateCollider(JiggleTreeJobData tree, JiggleSimulatedPoint* point, JiggleSimulatedPoint* parent, JigglePointParameters* pointParameters, JigglePointParameters* parentParameters, JiggleCollider collider, ref float3 pendingDepenetration, ref float pendingMaxMagnitude) {
        var collisionDepenetration = new float3(0f, 0f, 0f);
        collisionDepenetration = DoDepenetration(point, parent, parentParameters, collider);
        var maxDepenetrationMagnitude = math.length(collisionDepenetration);
        for (int childIndex = 0; childIndex < point->childrenCount; childIndex++) {
            var child = tree.points + point->childrenIndices[childIndex];
            var newCollisionDepenetration = DoDepenetration(point, child, pointParameters, collider);
            maxDepenetrationMagnitude = math.max(maxDepenetrationMagnitude, math.length(newCollisionDepenetration));
            collisionDepenetration += newCollisionDepenetration;
        }
        // Sum clamped to the deepest single push, so overlapping segments cannot add up into a push larger
        // than any of them asks for, while opposing segments cancel toward zero instead of being renormalized
        // into a full-magnitude shove along whatever direction the near-zero sum happened to point (or, with
        // the old (0,0,1) fallback, along world Z).
        var collisionSumLength = math.length(collisionDepenetration);
        if (collisionSumLength > maxDepenetrationMagnitude && collisionSumLength > 0f) {
            collisionDepenetration *= maxDepenetrationMagnitude / collisionSumLength;
        }
        pendingDepenetration += collisionDepenetration;
        pendingMaxMagnitude = math.max(pendingMaxMagnitude, math.length(collisionDepenetration));

        // Feed GetNormalizedPush's input (contact-driven elasticity softening): a point can be depenetrated
        // against several colliders/segments in the same frame (this method runs once per nearby collider), so
        // summing every call's push could grow unbounded (and even change direction) as more colliders touch it.
        // Keeping only the single largest push instead gives a bounded, stable "how hard is this point being
        // pressed right now" signal.
        if (math.lengthsq(collisionDepenetration) > math.lengthsq(point->contactPush)) {
            point->contactPush = collisionDepenetration;
        }
    }

    private unsafe bool ContainsIndex(int* array, int arrayCount, int index) {
        for (int i = 0; i < arrayCount; i++) {
            if (array[i] == index) {
                return true;
            }
        }
        return false;
    }

    // Upper bound on how much contactSoftening may relax the length constraint (see Constrain). Length
    // elasticity must never be softened all the way to 0: that removes the restoring force that pulls a
    // squashed-in bone back to its rest length, so contact would leave the bone permanently collapsed instead of
    // recovering once released. Capping it below 1 guarantees some restoring force always remains.
    private const float MaxLengthContactSoftening = 0.9f;

    private unsafe void Constrain(JiggleTreeJobData tree) {
        for (int i = 0; i < tree.pointCount; i++) {
            var point = tree.points+i;
            var pointParameters = tree.parameters + i;
            
            if (point->parentIndex == -1) {
                continue;
            }

            var parent = tree.points+point->parentIndex;
            var parentParameters = tree.parameters + point->parentIndex;

            #region Collisions

            // Every collider is measured against the same starting position and applied together below, so no
            // collider can react to another's correction. See DepenetrateCollider.
            var pendingDepenetration = float3.zero;
            var pendingMaxMagnitude = 0f;

            var global = globalCell.Value;
            for (int index = 0; index < global.count; index++) {
                var sceneCollider = sceneColliders[global.colliderIndices[index]];
                DepenetrateCollider(tree, point, parent, pointParameters, parentParameters, sceneCollider, ref pendingDepenetration, ref pendingMaxMagnitude);
            }

            // TODO: to convert a float to a grid location we just cast, but this always rounds towards zero. Probably should be a math.round()
            int2 min = tree.minExtentPosition;
            int2 max = tree.maxExtentPosition;
            for (int x = min.x; x <= max.x; x++) {
                for (int y = min.y; y <= max.y; y++) {
                    int2 grid = new int2(x, y);
                    if (broadPhaseMap.TryGetValue(grid, out var gridCell)) {
                        for (int index = 0; index < gridCell.count; index++) {
                            var sceneCollider = sceneColliders[gridCell.colliderIndices[index]];
                            DepenetrateCollider(tree, point, parent, pointParameters, parentParameters, sceneCollider, ref pendingDepenetration, ref pendingMaxMagnitude);
                        }
                    }
                }
            }

            var endIndex = tree.colliderIndexOffset + tree.colliderCount;
            for (int index = (int)tree.colliderIndexOffset; index < endIndex; index++) {
                DepenetrateCollider(tree, point, parent, pointParameters, parentParameters, personalColliders[index], ref pendingDepenetration, ref pendingMaxMagnitude);
            }

            // Same combination rule the individual colliders already use internally, now across all of them:
            // the summed push, clamped to the deepest single one. With one collider touching this is exactly
            // that collider's push, agreeing colliders keep their depth without stacking, and a wedge's
            // opposing pushes cancel toward a stable compromise instead of oscillating.
            if (pendingMaxMagnitude > 0f) {
                var combinedDepenetration = pendingDepenetration;
                var combinedLength = math.length(combinedDepenetration);
                if (combinedLength > pendingMaxMagnitude) {
                    combinedDepenetration *= pendingMaxMagnitude / combinedLength;
                }
                point->workingPosition += combinedDepenetration;
                // Net displacement collisions caused this step; FinishStep takes its contact normal from it.
                point->stepDepenetration += combinedDepenetration;
            }

            #endregion
            
            #region Special root particle solve

            if (parent->parentIndex == -1) {
                var child = tree.points[point->childrenIndices[0]];
                point->workingPosition = point->workingPosition = math.lerp(point->workingPosition, point->pose,
                    pointParameters->rootElasticity * pointParameters->rootElasticity);
                var head = point->pose;
                var tail = child.pose;
                var diffasdf = head - tail;
                parent->workingPosition = point->workingPosition + diffasdf;
                continue;
            }

            #endregion

            // How much to relax this point's own elasticities this frame, given the contact push that was just
            // resolved in the Collisions region above. Computed once and reused by every read below (both this
            // point's own parameters and its parent's, since both ultimately resist this same point's position)
            // so they can never disagree about how hard the contact was.
            var contactSoftening = GetContactSoftening(point, pointParameters);
            // Length-based squash needs the length constraint itself to actually relax so the bone can shorten
            // under a straight-on push; angle softening alone can't dent a bone whose orientation isn't changing.
            // Capped (see MaxLengthContactSoftening) so the restoring force never fully disappears.
            // Gated on squash actually being enabled: relaxing the length constraint is squash's enabler and
            // nothing else's, and on a squashless rig it silently hands an inextensible chain (Stretch 0) an
            // axial degree of freedom whenever contact ramps the softening - which contact then pumps, reading
            // as the chain bouncing along its own axis. With squash off, contact softness now softens angles only.
            var lengthContactSoftening = pointParameters->squash > 0f
                ? math.min(contactSoftening, MaxLengthContactSoftening)
                : 0f;

            #region Back-propagated motion for collisions

            if (point->childrenCount > 0) {
                // Back-propagated motion specifically for collision enabled chains
                var child = tree.points+point->childrenIndices[0];
                if (child->hasTransform) {
                    var child_length_elasticity = pointParameters->lengthElasticity * pointParameters->lengthElasticity;
                    var parentToChildPose = child->pose - parent->pose;
                    var parentToChild = child->workingPosition - parent->workingPosition;
                    var parentToChildPoseNormalized = math.normalizesafe(parentToChildPose, new float3(0,0,1));
                    var parentToChildNormalized = math.normalizesafe(parentToChild, new float3(0,0,1));
                    var parentToChildRotCorrection = FromToRotationFromNormalizedVectors(parentToChildPoseNormalized, parentToChildNormalized);
                    var targetVect = point->pose - parent->pose;
                    var currentPointLength = math.length(point->workingPosition-parent->workingPosition);
                    targetVect = math.normalizesafe(targetVect, new float3(0,0,1)) * currentPointLength;
                    var targetPos = math.rotate(parentToChildRotCorrection, targetVect) + parent->workingPosition;

                    var targetFromChild = targetPos - child->workingPosition;
                    var targetfromChildDist = math.length(targetFromChild);
                    targetPos = child->workingPosition + math.lerp(
                        targetFromChild,
                        math.normalizesafe(targetFromChild, new float3(0,0,1)) * child->desiredLengthToParent,
                        child_length_elasticity
                        );

                    //var errorBackwardConstraint = math.length(point->workingPosition - targetPos);
                    //if (targetfromChildDist != 0) {
                    //    errorBackwardConstraint /= targetfromChildDist;
                    //}
                    //errorBackwardConstraint = math.min(errorBackwardConstraint, 1.0f);
                    //errorBackwardConstraint = math.pow(errorBackwardConstraint, parent->parameters.elasticitySoften);
                    var notFoldedBack = math.clamp(-math.dot(math.normalizesafe(parent->workingPosition - point->workingPosition), math.normalizesafe(child->workingPosition - point->workingPosition))+1f,0f,1f);
                    var childAngleElasticity = pointParameters->angleElasticity * pointParameters->angleElasticity * (1f - contactSoftening);
                    point->workingPosition = math.lerp(point->workingPosition, targetPos, childAngleElasticity * notFoldedBack);

                    //point->workingPosition = math.lerp(point->workingPosition, backward_constraint, notFoldedBack);
                }
            }

            #endregion
            
            #region Angle Constraint
            
            var parentParentWorkingPosition = parent->parentPose;
            if (parent->parentIndex != -1) {
                var parentParent = tree.points+parent->parentIndex;
                parentParentWorkingPosition = parentParent->workingPosition;
            }
            var length_elasticity = parentParameters->lengthElasticity * parentParameters->lengthElasticity * (1f - lengthContactSoftening);
            var parentAimPose = math.normalizesafe(point->parentPose - parent->parentPose, new float3(0,0,1));
            var parentAim = math.normalizesafe(parent->workingPosition - parentParentWorkingPosition, new float3(0,0,1));
            if (parent->parentIndex != -1) {
                var parentParent = tree.points+parent->parentIndex;
                parentAim = math.normalizesafe(parent->workingPosition - parentParent->workingPosition, new float3(0,0,1));
            }

            var currentLength = math.length(point->workingPosition - parent->workingPosition);
            var from_to_rot = FromToRotationFromNormalizedVectors(parentAimPose, parentAim);
            var constraintTarget = math.rotate(from_to_rot, point->pose - point->parentPose);

            var desiredPosition = parent->workingPosition + constraintTarget;

            var error = math.distance(point->workingPosition, desiredPosition);
            if (currentLength != 0) {
                error /= currentLength;
            }
            error = math.min(error, 1.0f);
            error = math.pow(error, parentParameters->elasticitySoften);
            for (int j = 0; j < timeIncrements; j++) {
                point->workingPosition = math.lerp(point->workingPosition, desiredPosition, parentParameters->angleElasticity * (1f - contactSoftening) * error);
            }

            #endregion

            // TODO: Early out if collisions are disabled (or don't for a more accurate solve)

            //continue;

            #region Length Constraint

            var offsetFromParent = point->workingPosition - parent->workingPosition;
            var offsetFromParentNormalized = math.normalizesafe(offsetFromParent, new float3(0, 0, 1));
            point->workingPosition = parent->workingPosition + math.lerp(offsetFromParent, offsetFromParentNormalized * point->desiredLengthToParent, length_elasticity);

            #endregion
            
            #region Angle Limit Constraint

            if (parentParameters->angleLimited) {
                var angleLimitParentAimPose = math.normalizesafe(point->parentPose - parent->parentPose, new float3(0,0,1));
                var angleLimitParentAim = math.normalizesafe(parent->workingPosition - parentParentWorkingPosition, new float3(0,0,1));
                var angleLimitFromTo = FromToRotationFromNormalizedVectors(angleLimitParentAimPose, angleLimitParentAim);
                var angleLimitConstraintTarget = math.rotate(angleLimitFromTo, point->pose - point->parentPose);

                var angleLimitDesiredPosition = parent->workingPosition + angleLimitConstraintTarget;
                
                var currentAngleError = float3Angle(
                    math.normalizesafe(point->workingPosition - parent->workingPosition, new float3(0f, 0f, 1f)),
                    math.normalizesafe(angleLimitDesiredPosition - parent->workingPosition, new float3(0f, 0f, 1f))
                );
                
                var A = parentParameters->angleLimit * math.PI * 0.5f;

                if (currentAngleError > A) {
                    var C = float3Angle(
                        point->workingPosition - angleLimitDesiredPosition,
                        parent->workingPosition - angleLimitDesiredPosition
                    ); // known included angle C

                    var b = math.distance(parent->workingPosition, angleLimitDesiredPosition); // known side opposite angle B

                    var B = math.PI - A - C;
                
                    var oppositeCheck = math.sin(B);
                    var a = oppositeCheck == 0f ? 0f : b * math.sin(A) / oppositeCheck;

                    var correctionVector = angleLimitDesiredPosition - point->workingPosition;
                    var correctionDir = math.normalizesafe(correctionVector, new float3(0,0,1));
                    var correctionDistance = math.length(correctionVector);

                    var angleCorrectionDistance = math.max(0f, correctionDistance - a);
                    var angleCorrection =
                        (correctionDir * angleCorrectionDistance) * (1f - parentParameters->angleLimitSoften * 0.5f);
                    point->workingPosition += angleCorrection;
                }
                
            }

            #endregion
            
        }
    }

    private unsafe void FinishStep(JiggleTreeJobData tree) {
        for (int i = 0; i < tree.pointCount; i++) {
            var point = tree.points+i;
            var parameters = tree.parameters + i;
            var newPosition = point->workingPosition;
            var previousPosition = point->position;
            // Velocity here is just the gap between the two stored positions, so a collision's push-out also
            // becomes speed. That is what makes a point wedged between colliders gain a little every step until
            // it escapes and launches away. Cancelling the whole push instead would break resting contact: at
            // rest the push is precisely what offsets the sinking from gravity, and removing it lets gravity
            // accumulate into the velocity every step until the chain pumps. So only the component along the
            // contact normal is removed - the inelastic part - leaving sliding along the surface untouched.
            var damping = parameters->contactDamping;
            if (damping > 0f) {
                var depenetration = point->stepDepenetration;
                var depenetrationLengthSq = math.lengthsq(depenetration);
                // A point that touched nothing this step has no normal to speak of and is left exactly as before.
                if (depenetrationLengthSq > 1e-12f) {
                    var normal = depenetration * math.rsqrt(depenetrationLengthSq);
                    var normalSpeed = math.dot(newPosition - previousPosition, normal);
                    previousPosition += normal * (normalSpeed * damping);
                }
            }
            point->lastPosition = previousPosition;
            point->position = newPosition;
            // Cleared here rather than only in Cache, so a step that runs without a fresh Cache cannot reuse
            // the previous step's push.
            point->stepDepenetration = float3.zero;
        }
    }

    // How deep this frame's largest depenetration push is, relative to this point's own collision radius,
    // saturated to [0,1]: a push as large as the point's own radius already counts as "fully pressed," so
    // arbitrarily deep or fast depenetration can't drive anything past that. Used by contact-driven elasticity
    // softening (GetContactSoftening/Constrain). Squash itself no longer uses this - it reads bone length
    // directly in ApplyPose instead - but softening the length constraint (see MaxLengthContactSoftening) is
    // still what lets a straight-on push actually shorten the bone for squash to react to.
    private unsafe float GetNormalizedPush(JiggleSimulatedPoint* point) {
        var pushMagnitude = math.length(point->contactPush);
        if (pushMagnitude <= 1e-5f || point->worldRadius <= 1e-5f) {
            return 0f;
        }
        // Measure how deep the press is, not how much force it took this frame. The raw depenetration only says
        // how far the previous constraint pass shoved this point back into the collider, which stays small and
        // roughly constant however hard you lean in - so it read as barely varying with press depth. The
        // displacement away from the pose the animation asked for, along the push direction, is the depth
        // itself, and it only counts contact because the direction comes from the push.
        var pushDir = point->contactPush / pushMagnitude;
        var pressDepth = math.max(0f, math.dot(point->workingPosition - point->pose, pushDir));
        return math.saturate(pressDepth / point->worldRadius);
    }

    // Multiplicative softening (0 = no change, 1 = fully softened) applied to this point's length/angle
    // elasticities in Constrain(), so a deliberate press can sink into an otherwise stiff bone instead of being
    // largely undone by the very next constraint pass. Contact shallower than contactSoftnessThreshold leaves
    // elasticity completely untouched (stiffness is preserved for light/incidental touches), then ramps up
    // smoothly to contactSoftness at normalizedPush == 1.
    private unsafe float GetContactSoftening(JiggleSimulatedPoint* point, JigglePointParameters* pointParameters) {
        var normalizedPush = GetNormalizedPush(point);
        var threshold = pointParameters->contactSoftnessThreshold;
        // Guards the threshold approaching 1: there's no headroom left above it to ramp through anyway, so
        // clamping the denominator away from 0 just makes that a hard step at the threshold instead of a
        // division blowup.
        var headroom = math.max(1f - threshold, 0.01f);
        var t = math.saturate((normalizedPush - threshold) / headroom);
        return pointParameters->contactSoftness * t;
    }

    private unsafe void ApplyPose(JiggleTreeJobData tree) {
        var rootPoint = tree.points[1];
        var rootSimulationPosition = rootPoint.position;
        var rootPose = rootPoint.pose;
        var rootParameters = tree.parameters[1];
        var rootParameterElasticity = 1f-(1f-rootParameters.rootElasticity) * rootParameters.airDrag;
        
        for (int i = 0; i < tree.pointCount; i++) {
            var point = tree.points+i;
            var parameters = tree.parameters + i;
            if (point->childrenCount <= 0) {
                continue;
            }

            var child = tree.points[point->childrenIndices[0]];

            var local_pose = point->pose;
            var local_child_pose = child.pose;
            var local_child_working_position = child.workingPosition;
            var local_working_position = point->workingPosition;

            if (point->parentIndex == -1) {
                continue;
            }

            float3 cachedAnimatedVector = new float3(0f);
            float3 simulatedVector = cachedAnimatedVector;

            if (point->childrenCount <= 1) {
                cachedAnimatedVector = math.normalizesafe(local_child_pose - local_pose, new float3(0,0,1));
                simulatedVector = math.normalizesafe(local_child_working_position - local_working_position, new float3(0,0,1));
            } else {
                var cachedAnimatedVectorSum = new float3(0f);
                var simulatedVectorSum = cachedAnimatedVectorSum;
                for (var j = 0; j < point->childrenCount; j++) {
                    var child_also = tree.points[point->childrenIndices[j]];
                    var local_child_pose_also = child_also.pose;
                    var local_child_working_position_also = child_also.workingPosition;
                    cachedAnimatedVectorSum += math.normalizesafe(local_child_pose_also - local_pose, new float3(0,0,1));
                    simulatedVectorSum +=
                        math.normalizesafe(local_child_working_position_also - local_working_position, new float3(0,0,1));
                }

                cachedAnimatedVector = math.normalizesafe(cachedAnimatedVectorSum * (1f / point->childrenCount), new float3(0,0,1));
                simulatedVector = math.normalizesafe(simulatedVectorSum * (1f / point->childrenCount), new float3(0,0,1));
            }

            var animPoseToPhysicsPose = math.slerp(quaternion.identity,
                FromToRotationFromNormalizedVectors(cachedAnimatedVector, simulatedVector), parameters->blend);

            var transform = new JiggleTransform() {
                isVirtual = !point->hasTransform,
                position = point->workingPosition,
                rotation = math.mul(animPoseToPhysicsPose, tree.GetInputPose(inputPoses, i).rotation),
            };
            // Backward-compat gate (see JiggleTransform.writeScale): only ever compute/write a scale when this
            // point has opted into squash (or is finishing relaxing back to rest below), so a rig that never
            // touches squash never has its localScale written.
            var restLocalScale = tree.GetInputPose(restPoseTransforms, i).scale;
            var restScaleIsSane = math.all(math.isfinite(restLocalScale)) && math.all(restLocalScale > 0f);
            // A first child with no transform is the projected tip the tree appends past the last real bone, not
            // a bone this one actually spans, so its length says nothing about how this bone is being deformed.
            // Same rule collisions and the bone gizmo already apply to segments that end on a virtual point.
            if (parameters->squash > 0f && child.hasTransform) {
                // Classic squash & stretch, driven directly by how much this bone's length (point -> its first
                // child, the same segment child->desiredLengthToParent already describes) currently differs from
                // its rest length - not by push direction, so a straight-on press (which shortens the bone
                // without changing its orientation) squashes it correctly. No smoothing/state of its own: length
                // already changes continuously through the existing physics, so recomputing it fresh every frame
                // is already smooth.
                var restLength = child.desiredLengthToParent;
                var localScale = new float3(1f);
                if (restLength > 1e-5f) {
                    var currentLength = math.length(child.workingPosition - point->workingPosition);
                    // Floor avoids a division blowup (crossScale -> infinity) if the bone collapses to ~0 length.
                    var lengthRatio = math.max(currentLength / restLength, 0.01f);
                    var axialScale = lengthRatio;
                    // Exponent form so squashBulge == 1 lands exactly on volume preservation (the square root),
                    // 0 compresses without widening at all, and above 1 widens more than the lost length gives
                    // back. The "volume" here stands in for the mesh's rather than being measured from it, so
                    // treating exact preservation as one point on a dial is more useful than as a hard rule.
                    var crossScale = math.pow(1f / lengthRatio, 0.5f * parameters->squashBulge);
                    var finalAxial = math.lerp(1f, axialScale, parameters->squash);
                    var finalCross = math.lerp(1f, crossScale, parameters->squash);

                    // localScale only scales along local X/Y/Z, so approximate by blending each axis between the
                    // cross and axial factor by how much of the bone's local rest direction to its child (i.e.
                    // its axis) lies along that axis - the same approximation the old push-direction squash used.
                    var childIndex = point->childrenIndices[0];
                    var localAxisDir = math.normalizesafe(tree.GetInputPose(restPoseTransforms, childIndex).position, new float3(0f, 1f, 0f));
                    var axisWeight = math.abs(localAxisDir);
                    localScale = math.lerp(new float3(finalCross), new float3(finalAxial), axisWeight);
                }

                // Never write a scale built from a value that isn't a sane positive size: a rest scale that was
                // never captured, or a degenerate localScale, would otherwise collapse the bone outright - far
                // worse than squash silently doing nothing.
                if (restScaleIsSane && math.all(math.isfinite(localScale)) && math.all(localScale > 0f)) {
                    transform.scale = restLocalScale * localScale;
                    transform.writeScale = true;
                    point->hasWrittenScale = true;
                }
            } else if (point->hasWrittenScale) {
                // squash just dropped to 0 (or was always 0 and hasWrittenScale is stale - either way there's
                // nothing left to relax towards on its own, since squash keeps no state of its own anymore): write
                // the rest scale back exactly once so the bone doesn't stay stuck at its last squashed scale, then
                // stop - from here on this behaves exactly like a rig that never used squash.
                if (restScaleIsSane) {
                    transform.scale = restLocalScale;
                    transform.writeScale = true;
                }
                point->hasWrittenScale = false;
            }
            tree.WriteOutputPose(outputPoses, i, transform, rootSimulationPosition - rootPose, rootSimulationPosition, rootParameterElasticity);
        }
    }

    private bool Validate(JiggleTreeJobData tree) {
        if (!tree.GetIsValid(out string failReason)) {
            throw new InvalidOperationException(failReason);
        }

        return true;
    }

    public void Execute(int index) {
        var tree = jiggleTrees[index];
        #if UNITY_EDITOR
        if (!Validate(tree)) {
            return;
        }
        #endif
        Cache(ref tree);
        VerletIntegrate(tree);
        Constrain(tree);
        FinishStep(tree);
        ApplyPose(tree);
        jiggleTrees[index].Sanitize();
    }
}

}
