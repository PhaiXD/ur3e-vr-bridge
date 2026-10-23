using System;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// LowPassFilter: ตัวกรองความถี่ต่ำอันดับ 1 (First-order Low-Pass Filter)
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
    /// OneEuroFilter: ตัวกรองสัญญาณแบบปรับความถี่ตัดตามความเร็ว (1€ Filter - CHI 2012)
    /// - เมื่อมือนิ่ง/เคลื่อนที่ช้า: กรองสัญญาณรบกวน (Jitter & Tremor) ให้หยุดนิ่งสนิท
    /// - เมื่อมือขยับ/หมุนเร็ว: ปรับเพิ่ม Cutoff Frequency ทันที ทำให้ตอบสนองฉับไว ไร้ Lag
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
    /// OneEuroFilterVector3: ตัวกรอง One Euro Filter สำหรับพิกัดตำแหน่ง 3 มิติ (X, Y, Z)
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
    /// OneEuroFilterQuaternion: ตัวกรอง One Euro Filter สำหรับการหมุนข้อมือ 3 มิติ (Quaternion SO(3))
    /// - ใช้ความเร็วเชิงมุม (Angular Velocity) ปรับ Cutoff Frequency อัตโนมัติ
    /// - ผสานด้วย Slerp ไร้ Gimbal Lock และป้องกันปัญหา Antipodal Flip
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

            // ป้องกัน Antipodal Flip: ถ้า dot < 0 ให้กลับเครื่องหมาย q เพื่อให้หมุนผ่านทางที่สั้นที่สุดเสมอ
            if (Quaternion.Dot(lastFilteredQ, q) < 0.0f)
            {
                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            }

            // คำนวณระยะการหมุนเป็นเรเดียน และหาความเร็วเชิงมุม (rad/s)
            float angleRad = Quaternion.Angle(lastFilteredQ, q) * Mathf.Deg2Rad;
            float omega = angleRad * rate;

            // กรองความเร็วเชิงมุมผ่าน Low-pass dCutoff
            float alphaD = Alpha(rate, dCutoff);
            float filteredOmega = dOmegaFilter.FilterWithAlpha(omega, alphaD);

            // คำนวณ Dynamic Cutoff Frequency สำหรับการหมุน
            float cutoff = minCutoff + beta * filteredOmega;
            float alpha = Alpha(rate, cutoff);

            // หมุนเข้าหาเป้าหมายด้วย Slerp แบบไดนามิก
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
