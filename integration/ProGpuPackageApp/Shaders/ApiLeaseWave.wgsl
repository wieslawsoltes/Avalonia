// Algorithm: Blend two colors with a time-varying planar sine wave, then apply a radial vignette.
// Time complexity: O(1) per fragment with one sine and one distance evaluation.
// Space complexity: O(1) per fragment with no auxiliary storage or texture samples.
fn mainImage(fragCoord: vec2<f32>) -> vec4<f32> {
    let resolution = max(inputs.iResolution.xy, vec2<f32>(1.0, 1.0));
    let uv = fragCoord / resolution;
    let wave = 0.5 + 0.5 * sin(
        uv.x * 8.0 + uv.y * 5.0 + inputs.iTime * 1.8);
    let cyan = vec3<f32>(0.05, 0.78, 0.95);
    let violet = vec3<f32>(0.58, 0.24, 0.94);
    let color = mix(cyan, violet, wave);
    let vignette = 1.0 - smoothstep(
        0.2, 0.78, distance(uv, vec2<f32>(0.5, 0.5)));
    return vec4<f32>(color * (0.72 + 0.28 * vignette), 1.0);
}
