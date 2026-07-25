using Unity.Mathematics;

namespace GatorDragonGames.JigglePhysics {

public struct JiggleTransform {
    public bool isVirtual;
    public float3 position;
    public quaternion rotation;
    public float3 scale;
    // Whether `scale` should actually be written to the bone's localScale (see JiggleJobTransformWrite). Gates
    // the squash feature: when false, downstream consumers must not touch localScale at all, which is what
    // keeps squash == 0 (default) byte-for-byte backward compatible with rigs that never write scale.
    public bool writeScale;

    public static JiggleTransform Lerp(JiggleTransform a, JiggleTransform b, float t) {
        return new JiggleTransform() {
            isVirtual = a.isVirtual,
            position = math.lerp(a.position, b.position, t),
            rotation = math.slerp(a.rotation, b.rotation, t),
            scale = math.lerp(a.scale, b.scale, t),
            // Structural flag, not an animatable value: carried from `a` the same way isVirtual is.
            writeScale = a.writeScale,
        };
    }

    public override string ToString() {
        return $"Virtual: {isVirtual}, Position: {position}, Quaternion: {rotation}, Scale: {scale}, WriteScale: {writeScale}";
    }
}

}