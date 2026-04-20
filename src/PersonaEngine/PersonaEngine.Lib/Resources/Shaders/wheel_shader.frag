uniform float u_numSlices;       // Number of slices (configurable)
uniform float u_targetSegment;   // Target segment (0 to numSlices-1)
uniform float u_startSegment;    // Starting segment (0 to numSlices-1)
uniform float u_spinDuration;    // Duration of spin in seconds
uniform float u_minRotations;    // Minimum number of full rotations
uniform float u_spinStartTime;   // Time when spinning started
uniform vec2 u_resolution;       // Viewport resolution
uniform float u_time;            // Current time

// From vertex shader
varying vec2 v_texCoords;

// Constants
#define PI 3.14159265359
#define TAU (2.0 * PI)
#define MAX_SLICES 24.0  // Maximum supported slices (increased from 20)

// Appearance constants
#define LINE_THICKNESS_FACTOR 0.002
#define DIVIDER_THICKNESS_FACTOR 0.002
#define PIP_SIZE_FACTOR 0.01
#define HEART_SIZE_FACTOR 0.038
#define CENTER_RADIUS_FACTOR 0.2
#define TICKER_SIZE_FACTOR 0.09

// Glow parameters
#define GLOW_INTENSITY 1.2
#define GLOW_RADIUS 0.003
#define DIVIDER_GLOW_INTENSITY 1.5
#define DIVIDER_GLOW_RADIUS 0.0035
#define GLOW_COLOR vec3(1.0, 0.8, 0.4)

// Helper functions
float getGlow(float dist, float radius, float intensity){
    return pow(radius/dist, intensity);
}

vec3 pal(vec3 a, vec3 b, vec3 c, vec3 d, float t) {
    return a + b * cos(TAU * (c * t + d));
}

vec3 rainbow(float t) {
    return pal(
        vec3(0.5),
        vec3(0.3),
        vec3(0.7),
        vec3(0.0, 1.0, 2.0) / 3.0,
        t
    );
}

float heartSDF(vec2 p, float size) {
    p.y = -p.y;
    p /= size;
    
    const float offset = 0.3;
    float k = 1.2 * p.y - sqrt(abs(p.x) + offset);
    return p.x * p.x + k * k - 1.;
}

float tickerSDF(vec2 p, float size) {
    p /= size;
    
    vec2 bodyDim = vec2(0.4, 0.9);
    vec2 q = abs(p) - bodyDim;
    float cornerRadius = 0.3;
    float roundedRect = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - cornerRadius;
    
    float pointerRadius = 0.45;
    float pointerOffset = bodyDim.y + pointerRadius * 0.3;
    float pointer = length(p - vec2(0.0, -pointerOffset)) - pointerRadius;
  
    float result = min(roundedRect, pointer);
    
    return result * size;
}

// Match the easing function in the C# code exactly
float easeOutQuint(float t) {
    return 1.0 - pow(1.0 - t, 5.0);
}

float getSpinProgress(float currentTime, float startTime, float duration) {
    float elapsedTime = max(currentTime - startTime, 0.0);
    return min(elapsedTime / max(duration, 0.001), 1.0);
}

void main() {
    float screenMin = min(u_resolution.x, u_resolution.y);
    float OUTER_RADIUS = screenMin * 0.8;
    float INNER_RADIUS = OUTER_RADIUS * 0.8;
    float PIP_RADIUS = (INNER_RADIUS + OUTER_RADIUS) * 0.49;
    float CENTER_RADIUS = INNER_RADIUS * CENTER_RADIUS_FACTOR;
    float TICKER_SIZE = OUTER_RADIUS * TICKER_SIZE_FACTOR;
    
    float LINE_THICKNESS = OUTER_RADIUS * LINE_THICKNESS_FACTOR;
    float DIVIDER_THICKNESS = OUTER_RADIUS * DIVIDER_THICKNESS_FACTOR;
    float PIP_SIZE = OUTER_RADIUS * PIP_SIZE_FACTOR;
    float HEART_SIZE = OUTER_RADIUS * HEART_SIZE_FACTOR;
    
    // Convert texCoords (0-1) to centered coordinates
    vec2 fragCoord = v_texCoords * u_resolution;
    vec2 uv = (fragCoord - u_resolution * 0.5) / screenMin;
    uv *= 2.0;
    
    float segmentAngle = TAU / u_numSlices;
    float progress = getSpinProgress(u_time, u_spinStartTime, u_spinDuration);
    
    float easedProgress = easeOutQuint(progress);
    
    float startAngle = -(u_startSegment + 0.5) * segmentAngle + PI * 1.5;
    float targetAngle = -(u_targetSegment + 0.5) * segmentAngle + PI * 1.5;
    
    // Ensure we spin in the correct direction, same as C# code
    float adjustedExtraAngle = targetAngle - startAngle;
    if (adjustedExtraAngle > 0.0)
        adjustedExtraAngle -= TAU;
    
    // Calculate spinning angle with minimum rotations
    float spinningAngle = startAngle + (adjustedExtraAngle - u_minRotations * TAU) * easedProgress;
    
    float angle = progress >= 1.0 ? targetAngle : spinningAngle;
    
    float c = cos(angle);
    float s = sin(angle);
    vec2 rot = vec2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
    
    float radius = length(uv);
    float rotRadius = length(rot);
    float lineWidth = 1.0 / screenMin;
    
    float dividerAngle = TAU / u_numSlices;
    float pixelAngle = atan(rot.y, rot.x);
    
    float isOdd = mod(mod(u_numSlices, 2.0), 2.0);
    float angleOffset = dividerAngle * 0.5 * isOdd;
    
    float normalizedAngle = (pixelAngle + PI + angleOffset) / TAU;
    normalizedAngle = fract(normalizedAngle);
    float segmentIndex = floor(normalizedAngle * u_numSlices);
    
    vec3 segmentColor = rainbow(segmentIndex / u_numSlices);
    
    float colorMask = smoothstep(INNER_RADIUS + LINE_THICKNESS*4.0, INNER_RADIUS - LINE_THICKNESS*4.0, radius * screenMin);
    
    // Create a wheel mask to determine what's part of the wheel (opaque) vs background (transparent)
    float wheelArea = step(radius * screenMin, OUTER_RADIUS + LINE_THICKNESS*2.0) * 
                      step(CENTER_RADIUS - LINE_THICKNESS*2.0, radius * screenMin);
                      
    // Set alpha based on whether we're in wheel area, but apply color intensity using colorMask
    vec4 col = vec4(segmentColor * 0.8 * colorMask, wheelArea);
    
    float wheelMask = smoothstep(INNER_RADIUS - LINE_THICKNESS*4.0, INNER_RADIUS, radius * screenMin) * 
                     (1.0 - smoothstep(OUTER_RADIUS, OUTER_RADIUS + LINE_THICKNESS*4.0, radius * screenMin));
    vec3 bezel = vec3(0.2, 0.1, 0.3);
    col.rgb = mix(col.rgb, bezel, wheelMask);
    
    float segmentFrac = fract(normalizedAngle * u_numSlices);
    float angularDist = min(segmentFrac, 1.0 - segmentFrac) * dividerAngle;
    float divDistance = rotRadius * screenMin * sin(angularDist) - DIVIDER_THICKNESS;
    
    float radiusMask = smoothstep(INNER_RADIUS, 0.0, rotRadius * screenMin);
    
    float dividerGlow = getGlow(max(divDistance, 0.001), DIVIDER_GLOW_RADIUS * screenMin, DIVIDER_GLOW_INTENSITY);
    dividerGlow *= radiusMask;
    
    float hardLine = smoothstep(lineWidth * screenMin, 0.0, divDistance);
    col = mix(col, vec4(GLOW_COLOR, 1.0), hardLine * radiusMask);
    
    vec3 glowEffect = mix(col.rgb, GLOW_COLOR, min(dividerGlow, 1.0));
    col.rgb = glowEffect;
    col.a = max(col.a, min(dividerGlow, 1.0));
    
    float outerDist = abs(radius * screenMin - OUTER_RADIUS) - LINE_THICKNESS;
    float outerGlow = getGlow(max(outerDist, 0.001), GLOW_RADIUS * screenMin, GLOW_INTENSITY);
    col.rgb = mix(col.rgb, GLOW_COLOR, min(outerGlow, 1.0));
    col.a = max(col.a, min(outerGlow, 1.0));
    
    float innerDist = abs(radius * screenMin - INNER_RADIUS) - LINE_THICKNESS;
    float innerGlow = getGlow(max(innerDist, 0.001), GLOW_RADIUS * screenMin, GLOW_INTENSITY);
    col.rgb = mix(col.rgb, GLOW_COLOR, min(innerGlow, 1.0));
    col.a = max(col.a, min(innerGlow, 1.0));
    
    // Hearts at divider positions
    float minHeartDist = 1000.0;
    for (float i = 0.0; i < MAX_SLICES; i++) {
        if (i >= u_numSlices) break;  // Ensure compatibility with variable slice count
        
        float heartAngle = i * dividerAngle;
        vec2 heartPos = PIP_RADIUS * vec2(cos(heartAngle), sin(heartAngle)) / screenMin;
        vec2 heartUV = rot - heartPos;
        
        float heartRotation = -heartAngle - PI/2.0;
        float cs = cos(heartRotation);
        float sn = sin(heartRotation);
        heartUV = vec2(
            heartUV.x * cs - heartUV.y * sn,
            heartUV.x * sn + heartUV.y * cs
        );
        
        float heartDist = heartSDF(heartUV * screenMin, HEART_SIZE);
        minHeartDist = min(minHeartDist, heartDist);
    }
    
    // Pips at segment centers
    float minPipDist = 1000.0;
    for (float i = 0.0; i < MAX_SLICES; i++) {
        if (i >= u_numSlices) break;  // Ensure compatibility with variable slice count
        
        float pipAngle = (i + 0.5) * dividerAngle;
        vec2 pipPos = PIP_RADIUS * vec2(cos(pipAngle), sin(pipAngle)) / screenMin;
        float pipDist = length(rot - pipPos) * screenMin - PIP_SIZE;
        minPipDist = min(minPipDist, pipDist);
    }
    
    float pipGlow = getGlow(max(minPipDist, 0.001), GLOW_RADIUS * screenMin, GLOW_INTENSITY);
    col.rgb = mix(col.rgb, GLOW_COLOR, min(pipGlow, 1.0));
    col.a = max(col.a, min(pipGlow, 1.0));
    
    float heartBlend = smoothstep(0.2 * lineWidth * screenMin, -0.2 * lineWidth * screenMin, minHeartDist);
    vec4 heartColor = vec4(1.0, 0.4, 0.6, 1.0);
    col = mix(col, heartColor, heartBlend);
    
    vec3 centerColor = vec3(0.2, 0.1, 0.3);
    float centerMask = smoothstep(CENTER_RADIUS + LINE_THICKNESS*2.0, CENTER_RADIUS - LINE_THICKNESS*2.0, radius * screenMin);
    col = mix(col, vec4(centerColor, 1.0), centerMask);
    
    float centerEdgeDist = abs(radius * screenMin - CENTER_RADIUS) - LINE_THICKNESS;
    float centerGlow = getGlow(max(centerEdgeDist, 0.001), GLOW_RADIUS * screenMin, GLOW_INTENSITY);
    col.rgb = mix(col.rgb, GLOW_COLOR, min(centerGlow, 1.0));
    col.a = max(col.a, min(centerGlow, 1.0));
    
    // Ticker (the pointer at the top)
    vec2 tickerPos = vec2(0.0, 1.0) * (OUTER_RADIUS - 0.01 * screenMin) / screenMin;
    vec2 tickerUV = uv - tickerPos;
    float tickerDist = tickerSDF(tickerUV * screenMin, TICKER_SIZE);
    
    float tickerMask = smoothstep(lineWidth * screenMin, -lineWidth * screenMin, tickerDist);
    
    vec2 normTickerUV = tickerUV * screenMin / TICKER_SIZE;
    
    vec3 tickerColor = vec3(0.6, 0.5, 0.7);
    vec3 tickerColorGem = vec3(1.0, 0.4, 0.3);
    
    vec2 gemPosition = vec2(0.0, -0.9 - 0.25 * 0.6);
    float gemDistance = length(normTickerUV - gemPosition);
    float gemFactor = smoothstep(0.25, 0.25 * 0.7, gemDistance);
        
    tickerColor = mix(tickerColor, tickerColorGem, gemFactor);
    
    col = mix(col, vec4(tickerColor, 1.0), tickerMask);
    
    gl_FragColor = col;
}