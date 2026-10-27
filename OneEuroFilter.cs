using System;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// First-order Low-Pass Filter.
    /// </summary>
    public class LowPassFilter
    {
        private float y;
        private float a;
        private float s;
        private bool initialized;

        public void SetAlpha(float alpha)
        {
            if (alpha <= 0.0f || alpha > 1.0f)
                alpha = Mathf.Clamp(alpha, 1e-5f, 1.0f);
            a = alpha;
        }

        public float Filter(float value)
        {
            float result;
            if (initialized)
                result = a * value + (1.0f - a) * s;
            else
            {
                result = value;
                initialized = true;
            }
            y = value;
            s = result;
            return result;
        }

        public float FilterWithAlpha(float value, float alpha)
        {
            SetAlpha(alpha);
            return Filter(value);
        }

        public bool HasLastRawValue() => initialized;
        public float LastRawValue() => y;
        public float LastFilteredValue() => s;

        public void Reset()
        {
            initialized = false;
            y = 0f;
            s = 0f;
        }
    }

    /// <summary>
    /// OneEuroFilter: Speed-adaptive cutoff frequency filter (1€ Filter - CHI 2012).
    /// Filters jitter at low speed; minimizes latency during fast motions.
    /// </summary>
    public class OneEuroFilter
    {
        public float minCutoff;
        public float beta;
        public float dCutoff;

        private LowPassFilter xFilter = new LowPassFilter();
        private LowPassFilter dxFilter = new LowPassFilter();
        private float lastTime = -1f;

        public OneEuroFilter(float minCutoff = 1.0f, float beta = 15.0f, float dCutoff = 1.0f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.dCutoff = dCutoff;
        }

        private static float Alpha(float rate, float cutoff)
        {
            float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
            float te = 1.0f / rate;
            return 1.0f / (1.0f + tau / te);
        }

        public float Filter(float x, float timestamp = -1f)
        {
            float dt = (lastTime > 0f && timestamp > 0f) ? (timestamp - lastTime) : Time.deltaTime;
            if (dt <= 1e-5f) dt = 0.0166f;
            lastTime = timestamp;

            float rate = 1.0f / dt;
            float dx = xFilter.HasLastRawValue() ? (x - xFilter.LastRawValue()) * rate : 0.0f;
            float edx = dxFilter.FilterWithAlpha(dx, Alpha(rate, dCutoff));
            float cutoff = minCutoff + beta * Mathf.Abs(edx);
            return xFilter.FilterWithAlpha(x, Alpha(rate, cutoff));
        }

        public void Reset()
        {
            xFilter.Reset();
            dxFilter.Reset();
            lastTime = -1f;
        }
    }

    /// <summary>
    /// OneEuroFilterVector3: 1€ Filter for 3D Vector3 positions.
    /// </summary>
    public class OneEuroFilterVector3
    {
        private OneEuroFilter xFilter;
        private OneEuroFilter yFilter;
        private OneEuroFilter zFilter;

        public OneEuroFilterVector3(float minCutoff = 1.0f, float beta = 15.0f, float dCutoff = 1.0f)
        {
            xFilter = new OneEuroFilter(minCutoff, beta, dCutoff);
            yFilter = new OneEuroFilter(minCutoff, beta, dCutoff);
            zFilter = new OneEuroFilter(minCutoff, beta, dCutoff);
        }

        public void UpdateParams(float minCutoff, float beta, float dCutoff = 1.0f)
        {
            xFilter.minCutoff = minCutoff; xFilter.beta = beta; xFilter.dCutoff = dCutoff;
            yFilter.minCutoff = minCutoff; yFilter.beta = beta; yFilter.dCutoff = dCutoff;
            zFilter.minCutoff = minCutoff; zFilter.beta = beta; zFilter.dCutoff = dCutoff;
        }

        public Vector3 Filter(Vector3 v, float timestamp = -1f)
        {
            return new Vector3(
                xFilter.Filter(v.x, timestamp),
                yFilter.Filter(v.y, timestamp),
                zFilter.Filter(v.z, timestamp)
            );
        }

        public Vector3 Filter(Vector3 v, float minCutoff, float beta, float dCutoff = 1.0f, float timestamp = -1f)
        {
            UpdateParams(minCutoff, beta, dCutoff);
            return Filter(v, timestamp);
        }

        public void Reset()
        {
            xFilter.Reset();
            yFilter.Reset();
            zFilter.Reset();
        }
    }

    /// <summary>
    /// OneEuroFilterQuaternion: 1€ Filter for 3D rotations (Quaternion SO(3)).
    /// Uses angular velocity for adaptive cutoff and Slerp interpolation.
    /// </summary>
    public class OneEuroFilterQuaternion
    {
        public float minCutoff;
        public float beta;
        public float dCutoff;

        private Quaternion lastFilteredQ = Quaternion.identity;
        private LowPassFilter dOmegaFilter = new LowPassFilter();
        private bool initialized = false;
        private float lastTime = -1f;

        public OneEuroFilterQuaternion(float minCutoff = 1.0f, float beta = 2.5f, float dCutoff = 1.0f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.dCutoff = dCutoff;
        }

        public void UpdateParams(float minCutoff, float beta, float dCutoff = 1.0f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.dCutoff = dCutoff;
        }

        private static float Alpha(float rate, float cutoff)
        {
            float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
            float te = 1.0f / rate;
            return 1.0f / (1.0f + tau / te);
        }

        public Quaternion Filter(Quaternion q, float minCutoff, float beta, float dCutoff = 1.0f, float timestamp = -1f)
        {
            UpdateParams(minCutoff, beta, dCutoff);
            return Filter(q, timestamp);
        }

        public Quaternion Filter(Quaternion q, float timestamp = -1f)
        {
            if (!initialized)
            {
                lastFilteredQ = q;
                initialized = true;
                lastTime = timestamp;
                return q;
            }

            float dt = (lastTime > 0f && timestamp > 0f) ? (timestamp - lastTime) : Time.deltaTime;
            if (dt <= 1e-5f) dt = 0.0166f;
            lastTime = timestamp;
            float rate = 1.0f / dt;

            // Antipodal flip protection: shortest rotation path
            if (Quaternion.Dot(lastFilteredQ, q) < 0.0f)
            {
                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            }

            // Angular distance and velocity (rad/s)
            float angleRad = Quaternion.Angle(lastFilteredQ, q) * Mathf.Deg2Rad;
            float omega = angleRad * rate;

            // Filter angular velocity
            float alphaD = Alpha(rate, dCutoff);
            float filteredOmega = dOmegaFilter.FilterWithAlpha(omega, alphaD);

            // Dynamic cutoff frequency
            float cutoff = minCutoff + beta * filteredOmega;
            float alpha = Alpha(rate, cutoff);

            // Slerp to target
            lastFilteredQ = Quaternion.Slerp(lastFilteredQ, q, alpha);
            return lastFilteredQ;
        }

        public void Reset()
        {
            initialized = false;
            dOmegaFilter.Reset();
            lastTime = -1f;
        }
    }
}
