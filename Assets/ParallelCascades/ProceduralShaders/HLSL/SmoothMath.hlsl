// Original work (smooth min and max) Copyright (C) 2020 Sebastian Lague.
// License: Distributed under the MIT License. See LICENSE file.
// https://github.com/SebLague/Solar-System/blob/Development/Assets/Scripts/Celestial/Shaders/Includes/Math.cginc
// Adapted for Shadergraph by Emil Tonev.

float smoothMin(float a, float b, float k) {
    k = max(0, k);
    // https://www.iquilezles.org/www/articles/smin/smin.htm
    float h = max(0, min(1, (b - a + k) / (2 * k)));
    return a * h + b * (1 - h) - k * h * (1 - h);
}

// Smooth maximum of two values, controlled by smoothing factor k
// When k = 0, this behaves identically to max(a, b)
float smoothMax(float a, float b, float k) {
    k = min(0, -k);
    float h = max(0, min(1, (b - a + k) / (2 * k)));
    return a * h + b * (1 - h) - k * h * (1 - h);
}

// Smooth minimum of two values, controlled by smoothing factor k
// When k = 0, this behaves identically to min(a, b)
void smoothMin_float(float a, float b, float k, out float result) {
    result = smoothMin(a, b, k);
}

// Smooth maximum of two values, controlled by smoothing factor k
// When k = 0, this behaves identically to max(a, b)
void smoothMax_float(float a, float b, float k, out float result) {
    result = smoothMax(a, b, k);
}